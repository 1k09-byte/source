using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using kaliteConfig.Native;

namespace kaliteConfig.Gaming.GameMode;

public sealed class GameModeExecutor
{
    private readonly GameModeJournal _journal;
    private readonly GameModeLog _log;
    private readonly object _actionGate = new();
    private const uint PowerThrottlingVersion = 1;

    /// <summary>
    /// Optional guard evaluated inside <c>_actionGate</c> right before a change is
    /// applied. The engine sets this so a release that starts while an apply is queued
    /// behind the gate makes the apply bail out instead of re-tweaking a process that
    /// was just restored.
    /// </summary>
    public Func<bool>? ApplyAllowed { get; set; }

    private bool ApplyPermitted() => ApplyAllowed?.Invoke() != false;

    public GameModeExecutor(GameModeJournal journal, GameModeLog log)
    {
        _journal = journal;
        _log = log;
    }

    public BackgroundDecision Apply(
        ProcessFact fact,
        BackgroundAction requested,
        GameModeOutcome initial,
        BackgroundAction planned)
    {
        var decision = new BackgroundDecision(fact.Pid, fact.CreationTicks, fact.Name, fact.ImagePath,
            BackgroundCategory.UserAdded, string.Empty, requested, planned, initial,
            planned == requested ? "Planned" : "Downgraded to a safer action");

        if (initial == GameModeOutcome.SkippedProtected || initial == GameModeOutcome.SkippedSafety || initial == GameModeOutcome.Refused)
            return decision;

        if (!SameInstance(fact.Pid, fact.CreationTicks))
            return decision with { Outcome = GameModeOutcome.SkippedSafety, Message = "Process exited before the action ran" };

        lock (_actionGate)
        {
            if (!ApplyPermitted())
                return decision with { Outcome = GameModeOutcome.SkippedSafety, Planned = BackgroundAction.None, Message = "Release started before the action ran" };
            if (planned == BackgroundAction.None)
                return decision with { Outcome = GameModeOutcome.AlreadyInState, Message = "Left alone by its rule" };

            using var handle = NativeMethods.Handles.OpenProcess(
                NativeMethods.ProcessAccess.QueryLimitedInformation
                | NativeMethods.ProcessAccess.SetInformation,
                false, (uint)fact.Pid);
            if (handle.IsInvalid)
                return decision with { Outcome = GameModeOutcome.Refused, Planned = BackgroundAction.None, Message = "Refused (access denied)" };

            return planned == BackgroundAction.LowerPriority
                ? ApplyPriorityLevel(fact, decision, handle, NativeMethods.GameMode.BelowNormalPriorityClass, "Below normal priority")
                : ApplyPriorityLevel(fact, decision, handle, NativeMethods.Priority.Idle, "Low priority");
        }
    }

    private BackgroundDecision ApplyPriorityLevel(
        ProcessFact fact,
        BackgroundDecision decision,
        SafeProcessHandle handle,
        uint targetClass,
        string label)
    {
        IntPtr raw = handle.DangerousGetHandle();
        uint original = NativeMethods.GameMode.GetPriorityClass(raw);
        if (original == 0)
            return decision with { Outcome = GameModeOutcome.Refused, Planned = BackgroundAction.None, Message = "Refused (could not read the current priority)" };
        if (original == NativeMethods.Priority.Realtime)
            return decision with { Outcome = GameModeOutcome.SkippedSafety, Planned = BackgroundAction.None, Message = "Left alone (real-time priority)" };

        if (original == NativeMethods.GameMode.HighPriorityClass)
            return decision with { Outcome = GameModeOutcome.SkippedSafety, Planned = BackgroundAction.None, Message = "Left alone (already high priority)" };

        int throttlingSize = Marshal.SizeOf<NativeMethods.GameMode.PowerThrottlingState>();
        var throttling = new NativeMethods.GameMode.PowerThrottlingState { Version = PowerThrottlingVersion };
        bool throttlingReadable = NativeMethods.GameMode.GetProcessInformation(raw,
            NativeMethods.GameMode.ProcessPowerThrottling, ref throttling, throttlingSize);
        bool ecoWasOn = throttlingReadable
            && (throttling.ControlMask & NativeMethods.GameMode.PowerThrottlingExecutionSpeed) != 0
            && (throttling.StateMask & NativeMethods.GameMode.PowerThrottlingExecutionSpeed) != 0;

        bool boostWasDisabled = false;
        bool boostReadable = NativeMethods.Priority.GetProcessPriorityBoost(handle, out boostWasDisabled);

        if (original == targetClass && boostReadable && boostWasDisabled && ecoWasOn)
            return decision with { Outcome = GameModeOutcome.AlreadyInState, Message = label + ", efficiency mode and priority boost already set" };

        var entry = NewEntry(fact, decision.Planned, original,
            throttlingReadable ? throttling.Version : 0,
            throttlingReadable ? throttling.ControlMask : 0,
            throttlingReadable ? throttling.StateMask : 0);
        entry.OriginalProcessBoostDisabled = boostWasDisabled;
        _journal.Add(entry);

        bool ecoOn = ecoWasOn;
        if (throttlingReadable && !ecoWasOn)
        {
            var state = new NativeMethods.GameMode.PowerThrottlingState
            {
                Version = throttling.Version == 0 ? PowerThrottlingVersion : throttling.Version,
                ControlMask = throttling.ControlMask | NativeMethods.GameMode.PowerThrottlingExecutionSpeed,
                StateMask = throttling.StateMask | NativeMethods.GameMode.PowerThrottlingExecutionSpeed,
            };
            if (NativeMethods.GameMode.SetProcessInformation(raw, NativeMethods.GameMode.ProcessPowerThrottling,
                    ref state, Marshal.SizeOf<NativeMethods.GameMode.PowerThrottlingState>()))
            {
                var check = new NativeMethods.GameMode.PowerThrottlingState { Version = state.Version };
                if (NativeMethods.GameMode.GetProcessInformation(raw, NativeMethods.GameMode.ProcessPowerThrottling,
                        ref check, Marshal.SizeOf<NativeMethods.GameMode.PowerThrottlingState>()))
                    ecoOn = (check.StateMask & NativeMethods.GameMode.PowerThrottlingExecutionSpeed) != 0;
            }
        }

        bool boostOff = boostWasDisabled;
        if (boostReadable && !boostWasDisabled
            && NativeMethods.Priority.SetProcessPriorityBoost(handle, true)
            && NativeMethods.Priority.GetProcessPriorityBoost(handle, out bool boostCheck))
        {
            boostOff = boostCheck;
            entry.ProcessBoostChanged = boostOff;
            _journal.Add(entry);
        }

        bool priorityOk = original == targetClass;
        if (!priorityOk)
        {
            if (NativeMethods.GameMode.SetPriorityClass(raw, targetClass))
                priorityOk = NativeMethods.GameMode.GetPriorityClass(raw) == targetClass;
            if (!priorityOk)
                TryRestorePriority(raw, original);
        }

        if (!priorityOk && !boostOff && !ecoOn)
        {
            _journal.Remove(fact.Pid, fact.CreationTicks);
            return decision with { Outcome = GameModeOutcome.Refused, Planned = BackgroundAction.None, Message = "Refused (neither change stuck)" };
        }

        var boostTids = DisableThreadBoosts(fact.Pid);
        _journal.UpsertThreadState(fact.Pid, fact.CreationTicks, string.Join(',', boostTids), string.Empty);

        // I/O priority follows the CPU priority: Idle goes to VeryLow, Below normal to Low.
        uint ioTarget = targetClass == NativeMethods.Priority.Idle
            ? NativeMethods.Ntdll.IoPriorityVeryLow
            : NativeMethods.Ntdll.IoPriorityLow;
        var (ioThreads, ioProcess) = ApplyIoPriority(fact, handle, ioTarget);

        string message = priorityOk ? label : "Priority boost off (priority change refused)";
        if (boostOff) message += ", boost off";
        if (ecoOn) message += ", efficiency mode";
        if (boostTids.Count > 0) message += $", {boostTids.Count} thread boost(s) off";
        if (ioProcess) message += ioTarget == NativeMethods.Ntdll.IoPriorityVeryLow ? ", I/O very low" : ", I/O low";
        if (ioThreads > 0) message += $", {ioThreads} thread I/O";
        message += ", verified";
        return decision with { Outcome = GameModeOutcome.AppliedVerified, Message = message };
    }

    public BackgroundDecision DisableBoostOnly(ProcessFact fact)
    {
        var decision = new BackgroundDecision(fact.Pid, fact.CreationTicks, fact.Name, fact.ImagePath,
            BackgroundCategory.UserAdded, string.Empty, BackgroundAction.BoostOff, BackgroundAction.BoostOff,
            GameModeOutcome.Applicable, "Planned");

        if (!SameInstance(fact.Pid, fact.CreationTicks))
            return decision with { Outcome = GameModeOutcome.SkippedSafety, Message = "Process exited before the action ran" };

        lock (_actionGate)
        {
            if (!ApplyPermitted())
                return decision with { Outcome = GameModeOutcome.SkippedSafety, Planned = BackgroundAction.None, Message = "Release started before the action ran" };
            using var handle = NativeMethods.Handles.OpenProcess(
                NativeMethods.ProcessAccess.QueryLimitedInformation
                | NativeMethods.ProcessAccess.SetInformation,
                false, (uint)fact.Pid);
            if (handle.IsInvalid)
                return decision with { Outcome = GameModeOutcome.Refused, Planned = BackgroundAction.None, Message = "Refused (access denied)" };

            IntPtr raw = handle.DangerousGetHandle();
            uint original = NativeMethods.GameMode.GetPriorityClass(raw);
            if (original == 0)
                return decision with { Outcome = GameModeOutcome.Refused, Planned = BackgroundAction.None, Message = "Refused (could not read the current priority)" };
            if (original == NativeMethods.Priority.Realtime)
                return decision with { Outcome = GameModeOutcome.SkippedSafety, Planned = BackgroundAction.None, Message = "Left alone (real-time priority)" };

            if (!NativeMethods.Priority.GetProcessPriorityBoost(handle, out bool wasDisabled))
                return decision with { Outcome = GameModeOutcome.Refused, Planned = BackgroundAction.None, Message = "Refused (could not read priority boost)" };
            if (wasDisabled)
                return decision with { Outcome = GameModeOutcome.AlreadyInState, Message = "Priority boost already off" };

            var entry = NewEntry(fact, BackgroundAction.BoostOff, 0, 0, 0, 0);
            entry.ProcessBoostChanged = true;
            entry.OriginalProcessBoostDisabled = false;
            _journal.Add(entry);

            if (!NativeMethods.Priority.SetProcessPriorityBoost(handle, true)
                || !NativeMethods.Priority.GetProcessPriorityBoost(handle, out bool check) || !check)
            {
                _journal.Remove(fact.Pid, fact.CreationTicks);
                return decision with { Outcome = GameModeOutcome.Refused, Planned = BackgroundAction.None, Message = "Refused (the boost change did not stick)" };
            }

            var tids = DisableThreadBoosts(fact.Pid);
            _journal.UpsertThreadState(fact.Pid, fact.CreationTicks, string.Join(',', tids), string.Empty);

            string message = "Priority boost off, verified";
            if (tids.Count > 0) message += $", {tids.Count} thread boost(s) off";
            return decision with { Outcome = GameModeOutcome.AppliedVerified, Message = message };
        }
    }

    public BackgroundDecision ApplyGame(ProcessFact fact)
    {
        var decision = new BackgroundDecision(fact.Pid, fact.CreationTicks, fact.Name, fact.ImagePath,
            BackgroundCategory.Game, string.Empty, BackgroundAction.GameBoost, BackgroundAction.GameBoost,
            GameModeOutcome.Applicable, "Planned");

        if (!SameInstance(fact.Pid, fact.CreationTicks))
            return decision with { Outcome = GameModeOutcome.SkippedSafety, Message = "Process exited before the action ran" };

        lock (_actionGate)
        {
            if (!ApplyPermitted())
                return decision with { Outcome = GameModeOutcome.SkippedSafety, Planned = BackgroundAction.None, Message = "Release started before the action ran" };
            using var handle = NativeMethods.Handles.OpenProcess(
                NativeMethods.ProcessAccess.QueryLimitedInformation
                | NativeMethods.ProcessAccess.SetInformation,
                false, (uint)fact.Pid);
            if (handle.IsInvalid)
                return decision with { Outcome = GameModeOutcome.Refused, Message = "Refused (access denied)" };

            IntPtr raw = handle.DangerousGetHandle();
            uint original = NativeMethods.GameMode.GetPriorityClass(raw);
            if (original == 0)
                return decision with { Outcome = GameModeOutcome.Refused, Message = "Refused (could not read the current priority)" };
            if (original == NativeMethods.Priority.Realtime)
                return decision with { Outcome = GameModeOutcome.SkippedSafety, Message = "Left alone (real-time priority)" };

            bool boostPriority = original != NativeMethods.GameMode.AboveNormalPriorityClass
                && original != NativeMethods.GameMode.HighPriorityClass;

            if (_journal.Find(fact.Pid, fact.CreationTicks) is null)
                _journal.Add(NewEntry(fact, BackgroundAction.GameBoost, original, 0, 0, 0));

            var sweep = SweepGameThreads(fact);
            if (sweep.BoostOff.Count > 0 || sweep.Lowered.Count > 0)
                _journal.UpsertThreadState(fact.Pid, fact.CreationTicks, string.Join(',', sweep.BoostOff), FormatThreadTweaks(sweep.Lowered));

            if (!boostPriority)
            {
                bool tweaked = sweep.BoostOff.Count > 0 || sweep.Lowered.Count > 0;
                return decision with
                {
                    Outcome = tweaked ? GameModeOutcome.AppliedVerified : GameModeOutcome.AlreadyInState,
                    Message = "Game priority already high; no change" + DescribeGameThreads(sweep),
                };
            }

            if (!NativeMethods.GameMode.SetPriorityClass(raw, NativeMethods.GameMode.AboveNormalPriorityClass)
                || NativeMethods.GameMode.GetPriorityClass(raw) != NativeMethods.GameMode.AboveNormalPriorityClass)
            {
                TryRestorePriority(raw, original);
                RestoreThreadBoosts(sweep.BoostOff);
                RestoreThreadPriorities(sweep.Lowered);
                _journal.Remove(fact.Pid, fact.CreationTicks);
                return decision with { Outcome = GameModeOutcome.Refused, Message = "Refused (the game priority change did not stick)" };
            }

            return decision with { Outcome = GameModeOutcome.AppliedVerified, Message = "Game boosted, verified" + DescribeGameThreads(sweep) };
        }
    }

    private static string DescribeGameThreads((List<uint> BoostOff, Dictionary<uint, int> Lowered) sweep)
    {
        if (sweep.BoostOff.Count == 0 && sweep.Lowered.Count == 0) return string.Empty;
        return $"; {sweep.BoostOff.Count} useless thread boost(s) off, {sweep.Lowered.Count} useless thread(s) at low priority";
    }

    private static GameModeJournalEntry NewEntry(ProcessFact fact, BackgroundAction action, uint priority, uint version, uint control, uint state) => new()
    {
        Pid = fact.Pid,
        CreationTicks = fact.CreationTicks,
        Name = fact.Name,
        ImagePath = fact.ImagePath ?? string.Empty,
        Action = action.ToString(),
        OriginalPriorityClass = priority,
        ThrottlingVersion = version,
        ThrottlingControlMask = control,
        ThrottlingStateMask = state,
    };

    public BackgroundDecision SuspendBackgroundThreads(ProcessFact fact, BackgroundAction action)
    {
        var decision = new BackgroundDecision(fact.Pid, fact.CreationTicks, fact.Name, fact.ImagePath,
            BackgroundCategory.UserAdded, string.Empty, action, action, GameModeOutcome.Applicable, "Planned");

        if (!SameInstance(fact.Pid, fact.CreationTicks))
            return decision with { Outcome = GameModeOutcome.SkippedSafety, Message = "Process exited before the action ran" };

        lock (_actionGate)
        {
            if (!ApplyPermitted())
                return decision with { Outcome = GameModeOutcome.SkippedSafety, Planned = BackgroundAction.None, Message = "Release started before the action ran" };
            var existing = _journal.Find(fact.Pid, fact.CreationTicks);
            var alreadySuspended = existing is null
                ? new HashSet<uint>()
                : ParseTids(existing.SuspendedTids).ToHashSet();
            bool created = existing is null;
            if (created) _journal.Add(NewEntry(fact, action, 0, 0, 0, 0));

            var suspended = new List<uint>();
            int leftAlone = 0;
            foreach (uint tid in ListProcessThreads(fact.Pid))
            {
                // Threads already suspended by an earlier pass must not be suspended
                // again: SuspendThread stacks the count and a single ResumeThread on
                // release would leave them frozen forever.
                if (alreadySuspended.Contains(tid)) continue;
                try
                {
                    using var thread = NativeMethods.Handles.OpenThread(NativeMethods.ThreadAccess.SuspendResume, false, tid);
                    if (thread.IsInvalid)
                    {
                        leftAlone++;
                        continue;
                    }
                    if (NativeMethods.Threads.SuspendThread(thread) == uint.MaxValue)
                    {
                        leftAlone++;
                        continue;
                    }
                    suspended.Add(tid);
                }
                catch
                {
                    leftAlone++;
                }
            }

            if (suspended.Count == 0)
            {
                if (created) _journal.Remove(fact.Pid, fact.CreationTicks);
                return decision with
                {
                    Outcome = created ? GameModeOutcome.Refused : GameModeOutcome.AlreadyInState,
                    Planned = created ? BackgroundAction.None : action,
                    Message = created
                        ? "Refused (no thread could be suspended)"
                        : "Threads already suspended",
                };
            }

            _journal.UpsertSuspendedTids(fact.Pid, fact.CreationTicks, string.Join(',', suspended));
            string message = $"{suspended.Count} thread(s) suspended";
            if (leftAlone > 0) message += $", {leftAlone} left alone";
            return decision with { Outcome = GameModeOutcome.AppliedVerified, Message = message };
        }
    }

    public BackgroundDecision ApplyBackgroundMode(ProcessFact fact)
    {
        var decision = new BackgroundDecision(fact.Pid, fact.CreationTicks, fact.Name, fact.ImagePath,
            BackgroundCategory.UserAdded, string.Empty, BackgroundAction.BackgroundMode, BackgroundAction.BackgroundMode,
            GameModeOutcome.Applicable, "Planned");

        if (!SameInstance(fact.Pid, fact.CreationTicks))
            return decision with { Outcome = GameModeOutcome.SkippedSafety, Message = "Process exited before the action ran" };

        lock (_actionGate)
        {
            if (!ApplyPermitted())
                return decision with { Outcome = GameModeOutcome.SkippedSafety, Planned = BackgroundAction.None, Message = "Release started before the action ran" };
            using var handle = NativeMethods.Handles.OpenProcess(
                NativeMethods.ProcessAccess.QueryLimitedInformation
                | NativeMethods.ProcessAccess.SetInformation,
                false, (uint)fact.Pid);
            if (handle.IsInvalid)
                return decision with { Outcome = GameModeOutcome.Refused, Planned = BackgroundAction.None, Message = "Refused (access denied)" };

            IntPtr raw = handle.DangerousGetHandle();
            uint original = NativeMethods.GameMode.GetPriorityClass(raw);
            if (original == 0)
                return decision with { Outcome = GameModeOutcome.Refused, Planned = BackgroundAction.None, Message = "Refused (could not read the current priority)" };
            if (original == NativeMethods.Priority.Realtime)
                return decision with { Outcome = GameModeOutcome.SkippedSafety, Planned = BackgroundAction.None, Message = "Left alone (real-time priority)" };

            bool created = _journal.Find(fact.Pid, fact.CreationTicks) is null;
            if (created) _journal.Add(NewEntry(fact, BackgroundAction.BackgroundMode, original, 0, 0, 0));

            var lowered = new Dictionary<uint, int>();
            foreach (uint tid in ListProcessThreads(fact.Pid))
            {
                try
                {
                    using var thread = NativeMethods.Handles.OpenThread(
                        NativeMethods.ThreadAccess.SetInformation | NativeMethods.ThreadAccess.QueryInformation, false, tid);
                    if (thread.IsInvalid) continue;
                    int current = NativeMethods.Priority.GetThreadPriority(thread);
                    if (current <= NativeMethods.ThreadPriorityLevel.Idle) continue;
                    if (!NativeMethods.Priority.SetThreadPriority(thread, NativeMethods.ThreadPriorityLevel.Idle)) continue;
                    lowered[tid] = current;
                }
                catch
                {
                }
            }

            bool priorityOk = original == NativeMethods.Priority.Idle;
            if (!priorityOk && NativeMethods.GameMode.SetPriorityClass(raw, NativeMethods.Priority.Idle))
                priorityOk = NativeMethods.GameMode.GetPriorityClass(raw) == NativeMethods.Priority.Idle;

            if (!priorityOk && lowered.Count == 0)
            {
                RestoreThreadPriorities(lowered);
                if (created) _journal.Remove(fact.Pid, fact.CreationTicks);
                return decision with { Outcome = GameModeOutcome.Refused, Planned = BackgroundAction.None, Message = "Refused (neither change stuck)" };
            }

            if (lowered.Count > 0)
                _journal.UpsertThreadState(fact.Pid, fact.CreationTicks, string.Empty, FormatThreadTweaks(lowered));

            var (ioThreads, ioProcess) = ApplyIoPriority(fact, handle, NativeMethods.Ntdll.IoPriorityVeryLow);

            string message = priorityOk ? "Idle priority" : "Priority change refused";
            if (lowered.Count > 0) message += $", {lowered.Count} thread(s) at lowest priority";
            if (ioProcess || ioThreads > 0) message += ", I/O very low";
            return decision with { Outcome = GameModeOutcome.AppliedVerified, Message = message };
        }
    }

    public int ReleaseSuspendedThreads(string reason)
    {
        int released = 0;
        foreach (var entry in _journal.Snapshot())
        {
            if (string.IsNullOrEmpty(entry.SuspendedTids)) continue;
            ResumeThreads(ParseTids(entry.SuspendedTids));
            _journal.ClearSuspendedTids(entry.Pid, entry.CreationTicks);
            released++;
        }
        if (released > 0)
            _log.Add("Gaming Mode", "Threads", "Restored and verified", $"{released} process(es) resumed: {reason}");
        return released;
    }

    public int ReleaseBackgroundMode(string reason)
    {
        int released = 0;
        foreach (var entry in _journal.Snapshot())
        {
            bool hadTweaks = !string.IsNullOrEmpty(entry.ThreadPriorityTweaks);
            bool hadIo = !string.IsNullOrEmpty(entry.ThreadIoPriorityTweaks);
            if (!hadTweaks && !hadIo) continue;
            if (hadTweaks)
            {
                RestoreThreadPriorities(ParseThreadTweaks(entry.ThreadPriorityTweaks));
                _journal.ClearThreadTweaks(entry.Pid, entry.CreationTicks);
            }
            if (hadIo)
            {
                RestoreThreadIoPriorities(ParseThreadIoTweaks(entry.ThreadIoPriorityTweaks));
                _journal.ClearThreadIoTweaks(entry.Pid, entry.CreationTicks);
            }
            released++;
        }
        if (released > 0)
            _log.Add("Gaming Mode", "Threads", "Restored and verified", $"{released} process(es) left background mode: {reason}");
        return released;
    }

    public GameModeRestoreSummary RestoreAll(string reason)
    {
        int restored = 0;
        int failed = 0;
        lock (_actionGate)
        {
            foreach (var entry in _journal.Snapshot())
            {
                if (RestoreEntry(entry, out string message)) restored++;
                else
                {
                    failed++;
                    _log.Add(entry.Name, "Gaming Mode", "Restore", "Could not restore", message);
                }
            }
        }
        if (restored > 0)
            _log.Add("-", "Gaming Mode", "Restore", "Restored and verified", $"Restored {restored} process(es): {reason}");
        return new GameModeRestoreSummary(restored, failed);
    }

    public bool RestoreEntry(GameModeJournalEntry entry, out string message)
    {
        message = string.Empty;
        if (!SameInstance(entry.Pid, entry.CreationTicks))
        {
            _journal.Remove(entry.Pid, entry.CreationTicks);
            message = $"{entry.Name} is no longer running; nothing to restore";
            return true;
        }

        if (!string.IsNullOrEmpty(entry.SuspendedTids))
            ResumeThreads(ParseTids(entry.SuspendedTids));

        using var handle = NativeMethods.Handles.OpenProcess(
            NativeMethods.ProcessAccess.QueryLimitedInformation
            | NativeMethods.ProcessAccess.SetInformation,
            false, (uint)entry.Pid);

        if (handle.IsInvalid)
        {
            if (IsSuspensionOnly(entry))
            {
                _journal.Remove(entry.Pid, entry.CreationTicks);
                message = $"{entry.Name}: threads resumed";
                return true;
            }
            message = $"{entry.Name}: could not restore (access denied)";
            return false;
        }

        IntPtr raw = handle.DangerousGetHandle();
        bool ok = true;

        if (entry.Action == nameof(BackgroundAction.LowerPriority)
            || entry.Action == nameof(BackgroundAction.GameBoost)
            || entry.Action == nameof(BackgroundAction.Low)
            || entry.Action == nameof(BackgroundAction.Sweep)
            || entry.Action == nameof(BackgroundAction.ConfineCores)
            || entry.Action == nameof(BackgroundAction.BackgroundMode))
        {
            if (entry.OriginalPriorityClass != 0)
            {
                ok &= NativeMethods.GameMode.SetPriorityClass(raw, entry.OriginalPriorityClass);
                if (ok)
                {
                    uint check = NativeMethods.GameMode.GetPriorityClass(raw);
                    ok &= check != 0 && check == entry.OriginalPriorityClass;
                }
            }
        }
        if (entry.ThrottlingVersion != 0)
        {
            if (ok && entry.ThrottlingVersion != 0)
            {
                var original = new NativeMethods.GameMode.PowerThrottlingState
                {
                    Version = entry.ThrottlingVersion,
                    ControlMask = entry.ThrottlingControlMask,
                    StateMask = entry.ThrottlingStateMask,
                };
                ok &= TryRestoreThrottling(raw, original);
            }
        }

        if (entry.ProcessBoostChanged)
            ok &= NativeMethods.Priority.SetProcessPriorityBoost(handle, entry.OriginalProcessBoostDisabled);
        if (!string.IsNullOrEmpty(entry.BoostDisabledTids))
            RestoreThreadBoosts(ParseTids(entry.BoostDisabledTids));
        if (!string.IsNullOrEmpty(entry.ThreadPriorityTweaks))
            RestoreThreadPriorities(ParseThreadTweaks(entry.ThreadPriorityTweaks));

        if (entry.IoPriorityChanged)
        {
            bool ioOk = TrySetIoPriority(handle, entry.OriginalIoPriority)
                && TryGetIoPriority(handle, out uint ioCheck)
                && ioCheck == entry.OriginalIoPriority;
            if (!ioOk) ok = false;
        }
        if (!string.IsNullOrEmpty(entry.ThreadIoPriorityTweaks))
            RestoreThreadIoPriorities(ParseThreadIoTweaks(entry.ThreadIoPriorityTweaks));

        ok &= TryRestoreCpuSets(handle, entry);

        if (ok)
        {
            _journal.Remove(entry.Pid, entry.CreationTicks);
            message = $"{entry.Name}: original state restored";
            return true;
        }

        message = $"{entry.Name}: could not restore, kept in the journal";
        return false;
    }

    private static bool TryRestorePriority(IntPtr handle, uint original) =>
        NativeMethods.GameMode.SetPriorityClass(handle, original);

    private static bool TryRestoreThrottling(IntPtr handle, NativeMethods.GameMode.PowerThrottlingState original)
    {
        var state = original;
        if (!NativeMethods.GameMode.SetProcessInformation(handle, NativeMethods.GameMode.ProcessPowerThrottling, ref state,
                Marshal.SizeOf<NativeMethods.GameMode.PowerThrottlingState>()))
            return false;
        var check = new NativeMethods.GameMode.PowerThrottlingState { Version = state.Version };
        if (!NativeMethods.GameMode.GetProcessInformation(handle, NativeMethods.GameMode.ProcessPowerThrottling, ref check,
                Marshal.SizeOf<NativeMethods.GameMode.PowerThrottlingState>()))
            return false;
        return check.ControlMask == original.ControlMask && check.StateMask == original.StateMask;
    }

    private static bool TryRestoreCpuSets(SafeProcessHandle handle, GameModeJournalEntry entry)
    {
        if (!entry.CpuSetsChanged) return true;
        var original = ParseCpuSetIds(entry.OriginalCpuSetIds);
        var system = ParseCpuSetIds(entry.SystemCpuSetIds);
        var target = entry.OriginalCpuSetsRestricted && original.Length > 0 ? original : system;
        if (target.Length == 0) return false;
        if (!NativeMethods.CpuSets.SetProcessDefaultCpuSets(handle, target, (uint)target.Length)) return false;
        if (!NativeMethods.CpuSets.GetProcessDefaultCpuSets(handle, null, 0, out uint required)) return false;
        if (required != target.Length) return false;
        var buffer = new uint[required];
        if (!NativeMethods.CpuSets.GetProcessDefaultCpuSets(handle, buffer, required, out _)) return false;
        return new HashSet<uint>(buffer).SetEquals(target);
    }

    private static uint[] ParseCpuSetIds(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<uint>();
        var ids = new List<uint>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (uint.TryParse(part, out uint id)) ids.Add(id);
        return ids.ToArray();
    }

    private static readonly string[] GameJunkThreadHints = new[]
    {
        "telemetry", "analytic", "metric", "upload", "crash", "report",
        "watson", "feedback", "advert", "tracking", "cef", "crashpad", "breakpad", "bugreport",
    };

    private static List<uint> ListProcessThreads(int pid)
    {
        var tids = new List<uint>();
        IntPtr snapshot = NativeMethods.Toolhelp.CreateToolhelp32Snapshot(NativeMethods.Toolhelp.TH32CS_SNAPTHREAD, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return tids;
        try
        {
            var te = new NativeMethods.Toolhelp.THREADENTRY32 { dwSize = (uint)Marshal.SizeOf<NativeMethods.Toolhelp.THREADENTRY32>() };
            if (!NativeMethods.Toolhelp.Thread32First(snapshot, ref te)) return tids;
            do
            {
                if (te.th32OwnerProcessID == (uint)pid) tids.Add(te.th32ThreadID);
            }
            while (NativeMethods.Toolhelp.Thread32Next(snapshot, ref te));
        }
        catch
        {
        }
        finally
        {
            NativeMethods.Handles.CloseHandle(snapshot);
        }
        return tids;
    }

    private static bool TryDisableThreadBoost(uint tid)
    {
        try
        {
            using var thread = NativeMethods.Handles.OpenThread(
                NativeMethods.ThreadAccess.SetInformation | NativeMethods.ThreadAccess.QueryInformation, false, tid);
            if (thread.IsInvalid) return false;
            if (!NativeMethods.Priority.GetThreadPriorityBoost(thread, out bool boostDisabled) || boostDisabled) return false;
            if (!NativeMethods.Priority.SetThreadPriorityBoost(thread, true)) return false;
            return NativeMethods.Priority.GetThreadPriorityBoost(thread, out bool check) && check;
        }
        catch
        {
            return false;
        }
    }

    private static List<uint> DisableThreadBoosts(int pid)
    {
        var disabled = new List<uint>();
        foreach (var tid in ListProcessThreads(pid))
            if (TryDisableThreadBoost(tid)) disabled.Add(tid);
        return disabled;
    }

    private static (List<uint> BoostOff, Dictionary<uint, int> Lowered) SweepGameThreads(ProcessFact fact)
    {
        var boostOff = new List<uint>();
        var lowered = new Dictionary<uint, int>();
        foreach (var tid in ListProcessThreads(fact.Pid))
        {
            string? description = null;
            try { description = NativeSnapshotService.TryGetThreadDescription(tid); }
            catch { }
            if (string.IsNullOrEmpty(description)) continue;
            if (!GameJunkThreadHints.Any(hint => description.Contains(hint, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                using var thread = NativeMethods.Handles.OpenThread(
                    NativeMethods.ThreadAccess.SetInformation | NativeMethods.ThreadAccess.QueryInformation, false, tid);
                if (thread.IsInvalid) continue;
                if (NativeMethods.Priority.GetThreadPriorityBoost(thread, out bool boostDisabled) && !boostDisabled
                    && NativeMethods.Priority.SetThreadPriorityBoost(thread, true)
                    && NativeMethods.Priority.GetThreadPriorityBoost(thread, out bool check) && check)
                    boostOff.Add(tid);
                int current = NativeMethods.Priority.GetThreadPriority(thread);
                if (current > NativeMethods.ThreadPriorityLevel.Idle
                    && NativeMethods.Priority.SetThreadPriority(thread, NativeMethods.ThreadPriorityLevel.Idle))
                    lowered[tid] = current;
            }
            catch
            {
            }
        }
        return (boostOff, lowered);
    }

    private static string FormatThreadTweaks(Dictionary<uint, int> lowered)
        => string.Join(';', lowered.Select(pair => pair.Key + ":" + pair.Value));

    private static List<uint> ParseTids(string text)
    {
        var tids = new List<uint>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (uint.TryParse(part, out uint tid)) tids.Add(tid);
        return tids;
    }

    private static Dictionary<uint, int> ParseThreadTweaks(string text)
    {
        var tweaks = new Dictionary<uint, int>();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split(':');
            if (pair.Length == 2 && uint.TryParse(pair[0], out uint tid) && int.TryParse(pair[1], out int priority))
                tweaks[tid] = priority;
        }
        return tweaks;
    }

    private static void RestoreThreadBoosts(IEnumerable<uint> tids)
    {
        foreach (var tid in tids)
        {
            try
            {
                using var thread = NativeMethods.Handles.OpenThread(NativeMethods.ThreadAccess.SetInformation, false, tid);
                if (thread.IsInvalid) continue;
                NativeMethods.Priority.SetThreadPriorityBoost(thread, false);
            }
            catch
            {
            }
        }
    }

    private static bool IsSuspensionOnly(GameModeJournalEntry entry) =>
        !string.IsNullOrEmpty(entry.SuspendedTids)
        && entry.OriginalPriorityClass == 0
        && entry.ThrottlingVersion == 0
        && !entry.ProcessBoostChanged
        && !entry.IoPriorityChanged
        && !entry.CpuSetsChanged
        && string.IsNullOrEmpty(entry.BoostDisabledTids)
        && string.IsNullOrEmpty(entry.ThreadPriorityTweaks)
        && string.IsNullOrEmpty(entry.ThreadIoPriorityTweaks);

    private static void ResumeThreads(IEnumerable<uint> tids)
    {
        foreach (var tid in tids)
        {
            try
            {
                using var thread = NativeMethods.Handles.OpenThread(NativeMethods.ThreadAccess.SuspendResume, false, tid);
                if (thread.IsInvalid) continue;
                // SuspendThread stacks a count, and older sessions or other tools can
                // leave it above 1. Drain it fully so the thread always ends up running;
                // ResumeThread returns the count before decrementing, 0 once it is free.
                for (int i = 0; i < 130; i++)
                {
                    uint previous = NativeMethods.Threads.ResumeThread(thread);
                    if (previous == 0 || previous == uint.MaxValue) break;
                }
            }
            catch
            {
            }
        }
    }

    private static void RestoreThreadPriorities(Dictionary<uint, int> tweaks)
    {
        foreach (var pair in tweaks)
        {
            try
            {
                using var thread = NativeMethods.Handles.OpenThread(NativeMethods.ThreadAccess.SetInformation, false, pair.Key);
                if (thread.IsInvalid) continue;
                NativeMethods.Priority.SetThreadPriority(thread, pair.Value);
            }
            catch
            {
            }
        }
    }

    // ---- I/O priority (ntdll ProcessIoPriority class 33 / ThreadIoPriority class 22) ----
    // User mode can only set VeryLow(0), Low(1) or Normal(2). A process-level change
    // only affects threads created afterwards, so existing threads are enumerated and
    // demoted individually; their original values are journaled for restore.

    private static bool TryGetIoPriority(SafeProcessHandle process, out uint current)
    {
        uint value = 0;
        int status = NativeMethods.Ntdll.NtQueryInformationProcess(
            process, NativeMethods.Ntdll.ProcessIoPriority, ref value, sizeof(uint), out _);
        current = value;
        return status == 0;
    }

    private static bool TrySetIoPriority(SafeProcessHandle process, uint target)
    {
        uint value = target;
        return NativeMethods.Ntdll.NtSetInformationProcess(
            process, NativeMethods.Ntdll.ProcessIoPriority, ref value, sizeof(uint)) == 0;
    }

    private static bool TryGetThreadIoPriority(SafeThreadHandle thread, out uint current)
    {
        uint value = 0;
        int status = NativeMethods.Ntdll.NtQueryInformationThread(
            thread, NativeMethods.Ntdll.ThreadIoPriority, ref value, sizeof(uint), out _);
        current = value;
        return status == 0;
    }

    private static bool TrySetThreadIoPriority(SafeThreadHandle thread, uint target)
    {
        uint value = target;
        return NativeMethods.Ntdll.NtSetInformationThread(
            thread, NativeMethods.Ntdll.ThreadIoPriority, ref value, sizeof(uint)) == 0;
    }

    /// <summary>
    /// Demotes the process default I/O priority and every existing thread to <paramref name="target"/>,
    /// journaling the original values. Failures are ignored - I/O priority is a best-effort demotion
    /// that must never fail the surrounding priority action.
    /// </summary>
    private (int ThreadsLowered, bool ProcessLowered) ApplyIoPriority(ProcessFact fact, SafeProcessHandle handle, uint target)
    {
        bool processLowered = false;
        try
        {
            if (TryGetIoPriority(handle, out uint current) && current > target && TrySetIoPriority(handle, target))
            {
                _journal.UpsertIoPriority(fact.Pid, fact.CreationTicks, current);
                processLowered = true;
            }
        }
        catch
        {
        }

        int lowered = 0;
        var tweaks = new List<string>();
        foreach (uint tid in ListProcessThreads(fact.Pid))
        {
            try
            {
                using var thread = NativeMethods.Handles.OpenThread(
                    NativeMethods.ThreadAccess.SetInformation | NativeMethods.ThreadAccess.QueryInformation, false, tid);
                if (thread.IsInvalid) continue;
                if (!TryGetThreadIoPriority(thread, out uint threadCurrent) || threadCurrent <= target) continue;
                if (!TrySetThreadIoPriority(thread, target)) continue;
                tweaks.Add(tid + ":" + threadCurrent);
                lowered++;
            }
            catch
            {
            }
        }
        if (tweaks.Count > 0)
            _journal.UpsertThreadIoTweaks(fact.Pid, fact.CreationTicks, string.Join(';', tweaks));
        return (lowered, processLowered);
    }

    private static Dictionary<uint, uint> ParseThreadIoTweaks(string text)
    {
        var tweaks = new Dictionary<uint, uint>();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split(':');
            if (pair.Length == 2 && uint.TryParse(pair[0], out uint tid) && uint.TryParse(pair[1], out uint priority))
                tweaks[tid] = priority;
        }
        return tweaks;
    }

    private static void RestoreThreadIoPriorities(Dictionary<uint, uint> tweaks)
    {
        foreach (var pair in tweaks)
        {
            try
            {
                using var thread = NativeMethods.Handles.OpenThread(
                    NativeMethods.ThreadAccess.SetInformation | NativeMethods.ThreadAccess.QueryInformation, false, pair.Key);
                if (thread.IsInvalid) continue;
                TrySetThreadIoPriority(thread, pair.Value);
            }
            catch
            {
            }
        }
    }

    public static bool SameInstance(int pid, long ticks)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && process.StartTime.Ticks == ticks;
        }
        catch
        {
            return false;
        }
    }
}
