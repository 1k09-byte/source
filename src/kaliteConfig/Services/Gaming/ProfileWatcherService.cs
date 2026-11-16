using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using kaliteConfig.Models;

namespace kaliteConfig.Services;

public sealed class RuleApplyResult
{
    public bool ProcessFound { get; init; }
    public int Attempted { get; init; }
    public int Succeeded { get; init; }
    public DateTime Time { get; init; } = DateTime.Now;
    public List<int> ThreadTargets { get; init; } = new();
}

public sealed class ProfileWatcherService : IDisposable
{
    private readonly ProcessTuningService _tuning;
    private readonly CpuSetService _cpuSets;
    private readonly ThreadTuningService _threads;
    private readonly DispatcherQueue? _dispatcher;
    private readonly object _lock = new();
    private ManagementEventWatcher? _watcher;
    private string _profilePath;
    private string _logPath;

    private readonly HashSet<int> _gamingModePids = new();
    private readonly object _gamingLock = new();
    private System.Threading.Timer? _gamingLivenessTimer;

    public List<TunerProfile> ActiveProfiles { get; private set; } = new();

    public event EventHandler? RulesChanged;

    public ProfileWatcherService(ProcessTuningService tuning, CpuSetService cpuSets, ThreadTuningService threads, string? profilePath = null)
    {
        _tuning = tuning;
        _cpuSets = cpuSets;
        _threads = threads;
        try
        {
            _dispatcher = DispatcherQueue.GetForCurrentThread();
        }
        catch
        {
            _dispatcher = null;
        }
        _profilePath = profilePath
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "kaliteConfig", "threadtuner-profiles.json");
        _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "kaliteConfig", "rules-debug.log");
    }

    internal void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            try
            {
                if (new FileInfo(_logPath).Length > 200 * 1024)
                {
                    File.Delete(_logPath);
                }
            }
            catch
            {
            }
            File.AppendAllText(_logPath,
                $"{DateTime.Now:HH:mm:ss.fff} [pid={Environment.ProcessId}] {message}\r\n");
        }
        catch
        {
        }
    }

    public async Task LoadProfilesAsync()
    {
        try
        {
            if (File.Exists(_profilePath))
            {
                var text = await File.ReadAllTextAsync(_profilePath);
                Log($"load: {text.Length} bytes from {_profilePath}");
                var loaded = JsonSerializer.Deserialize<List<TunerProfile>>(text);
                Log($"load: deserialized {(loaded == null ? "null" : loaded.Count.ToString())} profiles");
                if (loaded != null)
                {
                    lock (_lock)
                    {
                        ActiveProfiles = loaded;
                    }
                    if (MergeRulesForSameTarget() > 0)
                    {
                        await SaveProfilesAsync();
                    }
                }
            }
            else
            {
                Log("load: no file yet, generating built-in defaults");
                lock (_lock)
                {
                    ActiveProfiles = CreateBuiltInDefaults();
                }
                _ = SaveProfilesAsync();
            }
        }
        catch (Exception ex)
        {
            Log("load FAILED: " + ex.GetType().Name + ": " + ex.Message);
        }
        await EnsureBuiltInDefaultsAsync();
        RaiseChanged();
    }

    public const string SelfProcessPattern = "kaliteConfig.exe";

    internal const string ProtectedRuleResult = "Not applied - protected system process";

    private List<TunerProfile> CreateBuiltInDefaults()
    {
        return new List<TunerProfile>();
    }

    private static readonly string[] RetiredBuiltInRuleNames =
    {
        "FortniteClient-Win64-Shipping",
        "FortniteClient-Win64-Shipping_EAC_EOS",
        "EasyAntiCheat_EOS",
    };

    internal static bool IsRetiredBuiltInRule(TunerProfile p) =>
        RetiredBuiltInRuleNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase);

    public async Task EnsureBuiltInDefaultsAsync()
    {
        bool changed = false;
        var defaults = CreateBuiltInDefaults();
        lock (_lock)
        {
            for (int i = ActiveProfiles.Count - 1; i >= 0; i--)
            {
                if (IsRetiredBuiltInRule(ActiveProfiles[i]))
                {
                    Log($"defaults: removed retired built-in '{ActiveProfiles[i].Name}'");
                    ActiveProfiles.RemoveAt(i);
                    changed = true;
                }
                else if (ActiveProfiles[i].Name == "DWM & Master Input" && IsOldDwmMasterInputShipment(ActiveProfiles[i]))
                {
                    Log($"defaults: removed retired built-in 'DWM & Master Input'");
                    ActiveProfiles.RemoveAt(i);
                    changed = true;
                }
                else if (TouchesProtectedSystemProcess(ActiveProfiles[i].Pattern))
                {
                    if (ActiveProfiles[i].Enabled)
                    {
                        ActiveProfiles[i].Enabled = false;
                        ActiveProfiles[i].LastResult = ProtectedRuleResult;
                        ActiveProfiles[i].RefreshSummaries();
                        Log($"defaults: disabled profile that targets a protected system process ([{ActiveProfiles[i].Name}]) - kept in the list");
                        changed = true;
                    }
                }
            }

            foreach (var def in defaults)
            {
                var existing = ActiveProfiles.FirstOrDefault(p => p.Name == def.Name);
                if (existing == null)
                {
                    ActiveProfiles.Add(def);
                    Log($"defaults: added missing built-in '{def.Name}'");
                    changed = true;
                }
                else if (def.Name == "kaliteConfig (this app)" && IsOursToRefreshSelfRule(existing))
                {
                    Log($"defaults: refreshed built-in '{def.Name}' to Idle / no boost / Efficiency mode");
                    existing.Pattern = def.Pattern;
                    existing.PriorityClass = def.PriorityClass;
                    existing.BoostEnabled = def.BoostEnabled;
                    existing.EfficiencyMode = def.EfficiencyMode;
                    existing.AutoApply = def.AutoApply;
                    changed = true;
                }
                else if (IsOldShippedContent(existing))
                {
                    existing.Pattern = def.Pattern;
                    existing.PriorityClass = def.PriorityClass;
                    existing.AutoApply = def.AutoApply;
                    existing.ThreadRules = def.ThreadRules;
                    Log($"defaults: refreshed built-in '{def.Name}' to highest-priority content");
                    changed = true;
                }
                else if (RulePatternGuard.RepairPattern(existing.Pattern, existing.Name, def.Pattern) is { } repaired)
                {
                    Log($"defaults: repaired pattern for built-in '{existing.Name}' " +
                        $"([{existing.Pattern}] → [{repaired}])");
                    existing.Pattern = repaired;
                    changed = true;
                }
            }
        }
        if (changed)
        {
            await SaveProfilesAsync();
            RaiseChanged();
        }
    }

    internal static bool PatternIsDisplayName(TunerProfile p) =>
        RulePatternGuard.IsRuleNameUsedAsPattern(p.Pattern, p.Name);

    private static bool IsOursToRefreshSelfRule(TunerProfile p)
    {
        if (p.GamingModeAuto) return false;
        if (p.ProtectThreads) return false;
        if (p.ThreadRules != null && p.ThreadRules.Count > 0) return false;
        if (p.AffinityMask.HasValue) return false;
        if (p.CpuSetIds != null && p.CpuSetIds.Count > 0) return false;

        bool nothingSet = !p.PriorityClass.HasValue && !p.BoostEnabled.HasValue && !p.EfficiencyMode.HasValue;
        if (nothingSet) return true;

        return (p.PriorityClass.HasValue && p.PriorityClass.Value == (uint)TunerPriorityClass.Idle)
            && (p.BoostEnabled.HasValue && p.BoostEnabled.Value == false)
            && (p.EfficiencyMode.HasValue && p.EfficiencyMode.Value == true);
    }

    private static bool IsOldShippedContent(TunerProfile p)
    {
        if (p.Name == "DWM & Master Input")
        {
            return IsOldDwmMasterInputShipment(p);
        }
        return false;
    }

    private static readonly string[] ProtectedProcessPatterns =
    {
        "winlogon", "wininit", "services", "lsass", "smss", "system", "registry", "memory compression",
        "kaliteconfig",
    };

    internal static bool TouchesProtectedSystemProcess(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        foreach (string part in pattern.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string name = part.Trim('*', ' ').ToLowerInvariant();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            if (ProtectedProcessPatterns.Contains(name)) return true;
        }
        return false;
    }

    private static bool IsConnectivityProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return Gaming.GameMode.GameModeGuard.IsConnectivityImage(process.ProcessName);
        }
        catch { return true; }
    }

    private static bool IsProtectedProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (ProtectedProcessPatterns.Contains(process.ProcessName.ToLowerInvariant())) return true;
            return Gaming.GameMode.GameModeGuard.IsConnectivityImage(process.ProcessName);
        }
        catch { return true; }
    }

    private static bool IsOldDwmMasterInputShipment(TunerProfile p)
    {
        if (p.Pattern != "dwm.exe, csrss.exe"
            || p.PriorityClass != (uint?)ProcessPriorityClass.High
            || p.GamingModeAuto)
        {
            return false;
        }
        if (p.ThreadRules == null || p.ThreadRules.Count == 0)
        {
            return true;
        }
        if (p.ThreadRules.Count == 1)
        {
            var r = p.ThreadRules[0];
            return r.MatchAllThreads
                && r.Priority == (int)ThreadPriorityLevel.Highest
                && string.IsNullOrWhiteSpace(r.Description) && r.StartAddress == string.Empty
                && r.EfficiencyMode == null && r.BoostEnabled == null
                && r.AffinityMask == null && r.IdealGroup == null && r.MemoryPriority == null;
        }
        return false;
    }

    internal static string TargetKey(TunerProfile profile)
    {
        string key = (profile.Pattern ?? string.Empty).Trim();
        if (key.Length == 0) key = (profile.Name ?? string.Empty).Trim();
        key = key.ToLowerInvariant().Replace(" ", string.Empty);
        if (key.EndsWith(".*", StringComparison.Ordinal)) key = key[..^2];
        else if (key.EndsWith("*", StringComparison.Ordinal)) key = key[..^1];
        if (key.EndsWith(".exe", StringComparison.Ordinal)) key = key[..^4];
        return key;
    }

    internal static string NameKey(TunerProfile profile)
    {
        string name = (profile.Name ?? string.Empty).Trim().ToLowerInvariant();
        return name == "*" ? string.Empty : name;
    }

    internal static bool SameThreadRule(TunerThreadRule a, TunerThreadRule b) =>
        a.MatchAllThreads == b.MatchAllThreads
        && string.Equals((a.Description ?? string.Empty).Trim(), (b.Description ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals((a.StartAddress ?? string.Empty).Trim(), (b.StartAddress ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    internal static void MergeProfile(TunerProfile target, TunerProfile source)
    {
        target.Enabled |= source.Enabled;
        target.AutoApply |= source.AutoApply;
        target.GamingModeAuto |= source.GamingModeAuto;
        target.ProtectThreads |= source.ProtectThreads;
        target.PriorityClass ??= source.PriorityClass;
        target.BoostEnabled ??= source.BoostEnabled;
        target.EfficiencyMode ??= source.EfficiencyMode;
        target.AffinityMask ??= source.AffinityMask;
        if ((target.CpuSetIds == null || target.CpuSetIds.Count == 0) && source.CpuSetIds is { Count: > 0 })
            target.CpuSetIds = new List<ulong>(source.CpuSetIds);
        if (target.PresetAffinity == AffinityScope.Unset && source.PresetAffinity != AffinityScope.Unset)
            target.PresetAffinity = source.PresetAffinity;

        target.ThreadRules ??= new ObservableCollection<TunerThreadRule>();
        if (source.ThreadRules != null)
        {
            foreach (var incoming in source.ThreadRules)
            {
                var existing = target.ThreadRules.FirstOrDefault(r => SameThreadRule(r, incoming));
                if (existing == null)
                {
                    target.ThreadRules.Add(incoming.Clone());
                    continue;
                }
                existing.Priority ??= incoming.Priority;
                existing.EfficiencyMode ??= incoming.EfficiencyMode;
                existing.BoostEnabled ??= incoming.BoostEnabled;
                existing.AffinityMask ??= incoming.AffinityMask;
                existing.AffinityGroup ??= incoming.AffinityGroup;
                existing.IdealGroup ??= incoming.IdealGroup;
                existing.IdealIndex ??= incoming.IdealIndex;
                existing.MemoryPriority ??= incoming.MemoryPriority;
                if (incoming.TargetCount > existing.TargetCount) existing.TargetCount = incoming.TargetCount;
            }
        }
        target.RefreshSummaries();
    }

    internal int MergeRulesForSameTarget()
    {
        int merged = 0;
        lock (_lock)
        {
            var kept = new List<TunerProfile>(ActiveProfiles.Count);
            var byTarget = new Dictionary<string, TunerProfile>(StringComparer.Ordinal);
            foreach (var profile in ActiveProfiles)
            {
                string key = TargetKey(profile);
                if (key.Length == 0 || key == "*")
                {
                    kept.Add(profile);
                    continue;
                }

                TunerProfile? target = null;
                if (!byTarget.TryGetValue(key, out target)
                    && NameKey(profile) is { Length: > 0 } nameKey)
                {
                    byTarget.TryGetValue("name:" + nameKey, out target);
                }
                if (target != null)
                {
                    MergeProfile(target, profile);
                    merged++;
                    continue;
                }

                byTarget[key] = profile;
                if (NameKey(profile) is { Length: > 0 } ownName) byTarget["name:" + ownName] = profile;
                kept.Add(profile);
            }
            if (merged > 0) ActiveProfiles = kept;
        }
        if (merged > 0)
        {
            Log($"merge: {merged} rule(s) folded into the row of the process they target");
        }
        return merged;
    }

    private readonly System.Threading.SemaphoreSlim _saveLock = new(1, 1);

    public async Task<bool> SaveProfilesAsync()
    {
        try
        {
            List<TunerProfile> snapshot;
            lock (_lock)
            {
                snapshot = ActiveProfiles.ToList();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_profilePath)!);
            var text = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });

            await _saveLock.WaitAsync();
            try
            {
                string tmp = _profilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmp, text);
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        File.Move(tmp, _profilePath, overwrite: true);
                        break;
                    }
                    catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 6)
                    {
                        await Task.Delay(80 * attempt);
                    }
                }
            }
            finally
            {
                _saveLock.Release();
            }

            Log($"save ok: {snapshot.Count} profiles, {text.Length} bytes");
            return true;
        }
        catch (Exception ex)
        {
            Log("save FAILED: " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    public sealed record RuleImportResult(int Added, int Updated, int Applied, int Skipped, bool Saved, string Message);

    public sealed class RulePack
    {
        public string Schema { get; set; } = "kaliteconfig.thread-rules";
        public int Version { get; set; } = 1;
        public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.Now;
        public List<TunerProfile> Rules { get; set; } = new();
    }

    private static readonly JsonSerializerOptions RulePackOptions = new() { WriteIndented = true };

    public string ExportRulesJson(out int count)
    {
        List<TunerProfile> snapshot;
        lock (_lock)
        {
            snapshot = ActiveProfiles.ToList();
        }
        count = snapshot.Count;
        return JsonSerializer.Serialize(new RulePack { Rules = snapshot }, RulePackOptions);
    }

    public async Task<RuleImportResult> ImportRulesJsonAsync(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new RuleImportResult(0, 0, 0, 0, false, "That file is empty.");

        List<TunerProfile>? incoming;
        try
        {
            using var document = JsonDocument.Parse(json);
            incoming = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => JsonSerializer.Deserialize<List<TunerProfile>>(json),
                JsonValueKind.Object => ReadRuleArray(document.RootElement),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            return new RuleImportResult(0, 0, 0, 0, false, "That file is not a valid rules file: " + ex.Message);
        }

        if (incoming is null)
            return new RuleImportResult(0, 0, 0, 0, false,
                "No rules were found in that file. Export your rules first to see the expected format.");

        int skipped = 0;
        var valid = new List<TunerProfile>();
        foreach (var rule in incoming)
        {
            if (rule is null)
            {
                skipped++;
                continue;
            }
            rule.Name = (rule.Name ?? string.Empty).Trim();
            rule.Pattern = (rule.Pattern ?? string.Empty).Trim();
            if (rule.Name.Length == 0 && rule.Pattern.Length == 0)
            {
                skipped++;
                continue;
            }
            if (rule.Name.Length == 0) rule.Name = rule.Pattern;
            if (rule.Pattern.Length == 0) rule.Pattern = rule.Name;

            rule.ThreadRules ??= new ObservableCollection<TunerThreadRule>();
            foreach (var threadRule in rule.ThreadRules)
            {
                threadRule.Description ??= string.Empty;
                threadRule.StartAddress ??= string.Empty;
            }
            valid.Add(rule);
        }

        if (valid.Count == 0)
            return new RuleImportResult(0, 0, 0, skipped, false, "No usable rules were found in that file.");

        int added = 0;
        int updated = 0;
        var touched = new List<TunerProfile>();
        lock (_lock)
        {
            foreach (var rule in valid)
            {
                var existing = ActiveProfiles.FirstOrDefault(p =>
                    string.Equals(p.Name, rule.Name, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    ActiveProfiles.Add(rule);
                    touched.Add(rule);
                    added++;
                }
                else
                {
                    existing.CopyFrom(rule);
                    touched.Add(existing);
                    updated++;
                }
            }
        }

        int merged = MergeRulesForSameTarget();
        var touchedKeys = new HashSet<string>(touched.Select(TargetKey), StringComparer.Ordinal);
        List<TunerProfile> toApply;
        lock (_lock)
        {
            toApply = ActiveProfiles.Where(p => touchedKeys.Contains(TargetKey(p))).ToList();
        }

        bool saved = await SaveProfilesAsync();
        RaiseChanged();
        EvaluateGamingModeRules();

        int applied = 0;
        foreach (var rule in toApply)
        {
            if (!rule.Enabled || !rule.AutoApply) continue;
            try
            {
                await ApplyProfileNowAsync(rule, quiet: true);
                applied++;
            }
            catch (Exception ex)
            {
                Log($"import: apply [{rule.Name}] failed: {ex.Message}");
            }
        }

        string message = $"Imported {added + updated} rule(s): {added} new, {updated} updated"
            + (merged > 0 ? $", {merged} duplicate row(s) merged into one" : string.Empty)
            + (skipped > 0 ? $", {skipped} skipped" : string.Empty)
            + $". Applied {applied} to running processes.";
        if (!saved) message += " The rule file could not be saved, so these rules are active for this session only.";
        return new RuleImportResult(added, updated, applied, skipped, saved, message);
    }

    private static List<TunerProfile>? ReadRuleArray(JsonElement root)
    {
        foreach (var name in new[] { "rules", "Rules" })
        {
            if (!root.TryGetProperty(name, out var rules)) continue;
            if (rules.ValueKind != JsonValueKind.Array) return null;
            return rules.Deserialize<List<TunerProfile>>();
        }
        return null;
    }

    public async Task<bool> AddOrUpdate(TunerProfile profile)
    {
        lock (_lock)
        {
            if (!ActiveProfiles.Contains(profile))
            {
                ActiveProfiles.Add(profile);
            }
        }
        Log($"add: pattern=[{profile.Pattern}] actions={profile.ProcessActionCount} threadrules={profile.ThreadRules?.Count ?? 0}");
        if (!profile.Enabled) await ClearBoostPreferencesAsync(profile);
        bool saved = await SaveProfilesAsync();
        RaiseChanged();

        EvaluateGamingModeRules();
        return saved;
    }

    public async Task<bool> Remove(TunerProfile profile)
    {
        lock (_lock)
        {
            ActiveProfiles.Remove(profile);
        }
        Log($"remove: pattern=[{profile.Pattern}]");
        await ClearBoostPreferencesAsync(profile);
        bool saved = await SaveProfilesAsync();
        RaiseChanged();
        EvaluateGamingModeRules();
        return saved;
    }

    private static async Task ClearBoostPreferencesAsync(TunerProfile profile)
    {
        string pattern = profile.Pattern ?? string.Empty;
        foreach (string part in pattern.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string name = part.Trim('*', ' ').ToLowerInvariant();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            if (name.Length == 0 || name.Contains('*') || name.Contains('?')) continue;
            try { await BoostPreferenceService.Instance.ClearProcessAsync(name); } catch { }
        }
    }

    public List<TunerProfile> FindMatches(string processName)
    {
        lock (_lock)
        {
            return ActiveProfiles
                .Where(p => p.Enabled && p.AutoApply && ProfileMatcher.Matches(p.Pattern, processName))
                .ToList();
        }
    }

    public async Task<RuleApplyResult> ApplyToProcessAsync(TunerProfile profile, int pid)
    {
        int attempted = 0;
        int succeeded = 0;
        var targets = new List<int>();

        if (profile.PriorityClass.HasValue)
        {
            attempted++;
            try
            {
                await _tuning.SetPriorityAsync(pid, profile.PriorityClass.Value);
                succeeded++;
            }
            catch
            {
            }
        }

        if (profile.BoostEnabled.HasValue)
        {
            attempted++;
            try
            {
                await _tuning.SetBoostAsync(pid, profile.BoostEnabled.Value);
                succeeded++;
            }
            catch
            {
            }
        }

        if (profile.EfficiencyMode.HasValue)
        {
            attempted++;
            try
            {
                await _tuning.SetEfficiencyAsync(pid, profile.EfficiencyMode.Value);
                succeeded++;
            }
            catch
            {
            }
        }

        if (profile.AffinityMask.HasValue)
        {
            attempted++;
            try
            {
                await _tuning.SetAffinityAsync(pid, profile.AffinityMask.Value);
                succeeded++;
            }
            catch
            {
            }
        }

        if (profile.CpuSetIds != null && profile.CpuSetIds.Count > 0)
        {
            attempted++;
            try
            {
                await _cpuSets.SetProcessCpuSetsAsync(pid, profile.CpuSetIds);
                succeeded++;
            }
            catch
            {
            }
        }

        if (profile.ThreadRules != null && profile.ThreadRules.Count > 0)
        {
            foreach (var rule in profile.ThreadRules)
            {
                var applied = await ApplyThreadRuleAsync(rule, pid);
                targets.Add(applied.Matched);
                attempted += applied.Attempted;
                succeeded += applied.Succeeded;
            }
        }

        return new RuleApplyResult
        {
            ProcessFound = true,
            Attempted = attempted,
            Succeeded = succeeded,
            ThreadTargets = targets,
        };
    }

    public async Task<RuleApplyResult> ApplyProfileNowAsync(TunerProfile profile, bool quiet = false)
    {
        if (!profile.Enabled)
        {
            return new RuleApplyResult { ProcessFound = false };
        }
        var pids = FindPids(profile.Pattern).Where(pid => !IsConnectivityProcess(pid)).ToList();
        bool blockedByProtection = false;
        if (TouchesProtectedSystemProcess(profile.Pattern))
        {
            int beforeFilter = pids.Count;
            pids = pids.Where(pid => !IsProtectedProcess(pid)).ToList();
            blockedByProtection = beforeFilter > 0 && pids.Count == 0;
        }
        Log($"apply-now: pattern=[{profile.Pattern}] pids=[{string.Join(",", pids)}]{(quiet ? " (keeper)" : "")}");
        if (pids.Count == 0)
        {
            string waiting = blockedByProtection
                ? ProtectedRuleResult
                : PatternIsDisplayName(profile)
                    ? "Pattern is the rule name - nothing can match it"
                    : "Waiting for process";
            bool waitingChanged = profile.LastResult != waiting;
            profile.LastResult = waiting;
            profile.RefreshSummaries();
            if (waitingChanged)
            {
                await SaveProfilesAsync();
                RaiseChanged();
            }
            return new RuleApplyResult { ProcessFound = false };
        }

        int attempted = 0;
        int succeeded = 0;
        List<int> targets = new();
        foreach (int pid in pids)
        {
            var r = await ApplyToProcessAsync(profile, pid);
            attempted += r.Attempted;
            succeeded += r.Succeeded;
            if (targets.Count == 0)
            {
                targets = r.ThreadTargets;
            }
        }

        string previousResult = profile.LastResult;
        var previousTargets = profile.ThreadRules?.Select(r => r.TargetCount).ToList();
        CommitResult(profile, attempted, succeeded, targets, quiet);

        bool resultChanged = profile.LastResult != previousResult
            || (previousTargets is not null && profile.ThreadRules is not null
                && !previousTargets.SequenceEqual(profile.ThreadRules.Select(r => r.TargetCount)));

        if (!resultChanged)
        {
            return new RuleApplyResult
            {
                ProcessFound = true,
                Attempted = attempted,
                Succeeded = succeeded,
                ThreadTargets = targets,
            };
        }

        bool tracked;
        lock (_lock)
        {
            tracked = ActiveProfiles.Contains(profile);
        }
        if (tracked)
        {
            await SaveProfilesAsync();
        }
        else
        {
            Log($"apply-now: pattern=[{profile.Pattern}] not in store, save skipped");
        }
        RaiseChanged();
        return new RuleApplyResult
        {
            ProcessFound = true,
            Attempted = attempted,
            Succeeded = succeeded,
            ThreadTargets = targets,
        };
    }

    public async Task<(int Matched, int Attempted, int Succeeded)> ApplyThreadRuleAsync(TunerThreadRule rule, int pid)
    {
        var live = await ThreadQueryService.ListThreadsAsync(pid);
        int matched = 0, attempted = 0, succeeded = 0;

        foreach (var thread in live.Where(t => ThreadMatches(rule, t)))
        {
            matched++;
            uint tid = (uint)thread.Tid;

            if (rule.Priority is int priority)
            {
                attempted++;
                try
                {
                    await _threads.SetPriorityAsync(tid, priority);
                    succeeded++;
                }
                catch
                {
                }
            }

            if (rule.EfficiencyMode.HasValue)
            {
                attempted++;
                try
                {
                    await _threads.SetEfficiencyAsync(tid, rule.EfficiencyMode.Value);
                    succeeded++;
                }
                catch
                {
                }
            }

            if (rule.BoostEnabled.HasValue)
            {
                attempted++;
                try
                {
                    await _threads.SetBoostAsync(tid, rule.BoostEnabled.Value);
                    succeeded++;
                }
                catch
                {
                }
            }

            if (rule.AffinityMask.HasValue)
            {
                attempted++;
                try
                {
                    await _threads.SetAffinityAsync(tid, rule.AffinityGroup, rule.AffinityMask.Value);
                    succeeded++;
                }
                catch
                {
                }
            }

            if (rule.IdealGroup.HasValue && rule.IdealIndex.HasValue)
            {
                attempted++;
                try
                {
                    await _threads.SetIdealProcessorAsync(tid, rule.IdealGroup.Value, rule.IdealIndex.Value);
                    succeeded++;
                }
                catch
                {
                }
            }

            if (rule.MemoryPriority.HasValue)
            {
                attempted++;
                try
                {
                    await _threads.SetMemoryPriorityAsync(tid, rule.MemoryPriority.Value);
                    succeeded++;
                }
                catch
                {
                }
            }
        }

        return (matched, attempted, succeeded);
    }

    public static bool ThreadMatches(TunerThreadRule rule, LiveThreadInfo thread)
    {
        if (rule.MatchAllThreads) return true;

        string description = (rule.Description ?? string.Empty).Trim();
        string address = (rule.StartAddress ?? string.Empty).Trim();
        if (address == "-") address = string.Empty;
        if (description.Length == 0 && address.Length == 0) return false;

        bool namedRule = description.Length > 0;
        if (namedRule
            && !(thread.Description ?? string.Empty).Contains(description, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool stableAddress = address.IndexOf('+') > 0;
        bool narrows = address.Length > 0 && (!namedRule || stableAddress);
        if (!narrows) return true;

        return string.Equals(thread.StartAddress ?? string.Empty, address, StringComparison.OrdinalIgnoreCase);
    }

    public static void CommitResult(TunerProfile profile, int attempted, int succeeded, List<int> targets, bool quiet = false)
    {
        profile.LastResult = attempted == 0
            ? "No actions defined"
            : quiet
                ? $"{succeeded}/{attempted} actions applied"
                : $"{succeeded}/{attempted} actions applied · {DateTime.Now:HH:mm:ss}";
        if (profile.ThreadRules != null)
        {
            for (int i = 0; i < profile.ThreadRules.Count && i < targets.Count; i++)
            {
                profile.ThreadRules[i].TargetCount = targets[i];
            }
        }
        profile.RefreshSummaries();
    }

    public static List<int> FindPids(string pattern)
    {
        var pids = new List<int>();
        if (string.IsNullOrWhiteSpace(pattern)) return pids;
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (ProfileMatcher.Matches(pattern, proc.ProcessName))
                    {
                        pids.Add(proc.Id);
                    }
                }
                catch
                {
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
        }
        return pids;
    }

    public void StartWatcher()
    {
        StopWatcher();
        try
        {
            _watcher = new ManagementEventWatcher(new EventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            _watcher.EventArrived += OnProcessStarted;
            _watcher.Start();
        }
        catch
        {
            _watcher = null;
        }
    }

    public void StopWatcher()
    {
        if (_watcher != null)
        {
            try
            {
                _watcher.Stop();
                _watcher.EventArrived -= OnProcessStarted;
                _watcher.Dispose();
            }
            catch
            {
            }
            _watcher = null;
        }
    }

    private async void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var name = e.NewEvent.Properties["ProcessName"]?.Value as string;
            var pidProp = e.NewEvent.Properties["ProcessID"]?.Value;
            if (string.IsNullOrEmpty(name) || pidProp == null) return;

            int pid = Convert.ToInt32(pidProp);

            var matched = FindMatches(name);

            bool gamingRule = matched.Any(p => p.GamingModeAuto);

            if (gamingRule)
            {
                List<int> armed;
                lock (_gamingLock)
                {
                    _gamingModePids.RemoveWhere(p =>
                    {
                        try { Process.GetProcessById(p).Dispose(); return false; }
                        catch { return true; }
                    });
                    _gamingModePids.Add(pid);
                    armed = _gamingModePids.ToList();
                }

                StartGamingModeExitWatcher();
                StartGamingModeLivenessMonitor();
                await App.Current.LegacyGamingMode.AcquireAsync(
                    GameModeOwner.Rule, pid, armed, $"automatic gaming mode rule matched {name}");
                Log($"gaming-mode: rule hold taken for [{name}] pid={pid}, protected={armed.Count}");
            }

            if (matched.Count == 0)
            {
                _ = ReapplyBoostPreferencesAsync(pid, name);
                return;
            }

            _ = ReapplyBoostPreferencesAsync(pid, name);

            foreach (var profile in matched)
            {
                var result = await ApplyToProcessAsync(profile, pid);
                if (_dispatcher != null)
                {
                    _dispatcher.TryEnqueue(() =>
                    {
                        CommitResult(profile, result.Attempted, result.Succeeded, result.ThreadTargets);
                        _ = SaveProfilesAsync();
                        RaiseChanged();
                    });
                }

                if (profile.ThreadRules.Count > 0)
                    _ = SettleThreadRulesAsync(profile, pid);
            }
        }
        catch
        {
        }
    }

    private async Task SettleThreadRulesAsync(TunerProfile profile, int pid)
    {
        var launchedAt = DateTime.UtcNow;
        foreach (int atMs in new[] { 150, 600, 1500, 4000, 10000, 20000 })
        {
            try
            {
                int waitMs = atMs - (int)(DateTime.UtcNow - launchedAt).TotalMilliseconds;
                if (waitMs > 0) await Task.Delay(waitMs);
                if (!profile.Enabled || !IsPidAlive(pid)) return;

                var result = await ApplyToProcessAsync(profile, pid);
                if (_dispatcher != null)
                {
                    _dispatcher.TryEnqueue(() =>
                    {
                        CommitResult(profile, result.Attempted, result.Succeeded, result.ThreadTargets, quiet: true);
                        RaiseChanged();
                    });
                }
            }
            catch
            {
                return;
            }
        }
    }

    private static async Task ReapplyBoostPreferencesAsync(int pid, string name)
    {
        try
        {
            await BoostPreferenceRules.ApplyBoostLayersInOrderAsync(
                () => ProcessBoostPreferenceService.Instance.ApplyToProcessAsync(pid, name),
                () => BoostPreferenceService.Instance.ApplyToProcessAsync(pid, name));
        }
        catch
        {
        }
    }

    private static bool IsPidAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch { return false; }
    }

    public void StartGamingModeWatcher()
    {
        _ = ApplyAllRulesToRunningProcessesAsync();
        EvaluateGamingModeRules();
    }

    public async Task ApplyAllRulesToRunningProcessesAsync()
    {
        List<TunerProfile> profiles;
        lock (_lock)
        {
            profiles = ActiveProfiles.Where(p => p.Enabled && p.AutoApply).ToList();
        }

        foreach (var profile in profiles)
        {
            try
            {
                await ApplyProfileNowAsync(profile, quiet: true);
            }
            catch (Exception ex)
            {
                Log($"startup-sweep: [{profile.Name}] failed: {ex.Message}");
            }
        }
    }

    private System.Threading.Timer? _keeperTimer;
    private int _keeperRunning;

    public static readonly TimeSpan DefaultKeeperInterval = TimeSpan.FromSeconds(20);

    public void StartKeeper(TimeSpan? interval = null)
    {
        var period = interval ?? DefaultKeeperInterval;
        if (period <= TimeSpan.Zero) return;

        _keeperTimer ??= new System.Threading.Timer(_ => _ = KeepAppliedAsync(), null, 1000, (int)period.TotalMilliseconds);
        _keeperTimer.Change(1000, (int)period.TotalMilliseconds);
        Log($"keeper: sweeping every {period.TotalSeconds:0} s");
    }

    public void StopKeeper()
    {
        _keeperTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
    }

    public async Task KeepAppliedAsync()
    {
        if (System.Threading.Interlocked.Exchange(ref _keeperRunning, 1) == 1) return;
        try
        {
            await ApplyAllRulesToRunningProcessesAsync();
            await BoostPreferenceRules.ApplyBoostLayersInOrderAsync(
                static () => ProcessBoostPreferenceService.Instance.ApplyToRunningProcessesAsync(),
                static () => BoostPreferenceService.Instance.ApplyToRunningProcessesAsync());
        }
        catch (Exception ex)
        {
            Log("keeper FAILED: " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _keeperRunning, 0);
        }
    }

    public void EvaluateGamingModeRules()
    {
        try
        {
            List<TunerProfile> gamingRules;
            lock (_lock)
            {
                gamingRules = ActiveProfiles.Where(p => p.Enabled && p.GamingModeAuto).ToList();
            }

            var expected = new HashSet<int>();
            foreach (var profile in gamingRules)
            {
                foreach (int pid in FindPids(profile.Pattern))
                {
                    expected.Add(pid);
                }
            }

            lock (_gamingLock)
            {
                _gamingModePids.RemoveWhere(p =>
                {
                    try { Process.GetProcessById(p).Dispose(); return false; }
                    catch { return true; }
                });
                _gamingModePids.RemoveWhere(p => !expected.Contains(p));
                _gamingModePids.UnionWith(expected);
            }

            List<int> armed;
            lock (_gamingLock)
            {
                armed = _gamingModePids.ToList();
            }

            if (armed.Count > 0)
            {
                StartGamingModeExitWatcher();
                StartGamingModeLivenessMonitor();
                _ = App.Current.LegacyGamingMode.AcquireAsync(
                        GameModeOwner.Rule, armed[0], armed, "automatic gaming mode rule")
                    .ContinueWith(_ => Log($"gaming-mode: evaluated, armed pids=[{string.Join(",", armed)}]"));
            }
            else if (App.Current.LegacyGamingMode.IsHeldBy(GameModeOwner.Rule))
            {
                Log("gaming-mode: no armed processes remain (rules changed), releasing the rule's hold");
                App.Current.LegacyGamingMode.Release(GameModeOwner.Rule);
            }
        }
        catch (Exception ex)
        {
            Log("gaming-mode evaluate FAILED: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private ManagementEventWatcher? _exitWatcher;

    private void StartGamingModeLivenessMonitor()
    {
        _gamingLivenessTimer ??= new System.Threading.Timer(
            _ => CheckGamingProcessLiveness(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        _gamingLivenessTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void CheckGamingProcessLiveness()
    {
        try
        {
            bool hasArmed;
            bool anyAlive = false;
            lock (_gamingLock)
            {
                hasArmed = _gamingModePids.Count > 0;
                _gamingModePids.RemoveWhere(p =>
                {
                    try
                    {
                        using var process = Process.GetProcessById(p);
                        anyAlive = true;
                        return false;
                    }
                    catch
                    {
                        return true;
                    }
                });

                if (_gamingModePids.Count > 0)
                {
                    anyAlive = true;
                }
            }

            if (hasArmed && !anyAlive && App.Current.LegacyGamingMode.IsHeldBy(GameModeOwner.Rule))
            {
                Log("gaming-mode: liveness monitor found no armed game processes, releasing the rule's hold");
                App.Current.LegacyGamingMode.Release(GameModeOwner.Rule);
                _gamingLivenessTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            }
            else if (anyAlive && App.Current.LegacyGamingMode.IsHeldBy(GameModeOwner.Rule))
            {
                int? target = App.Current.LegacyGamingMode.SessionTargetPid;
                if (target.HasValue && !IsPidAlive(target.Value))
                {
                    Log("gaming-mode: session target exited while other armed games run - handing over");
                    EvaluateGamingModeRules();
                }
            }
        }
        catch
        {
        }
    }

    private void StartGamingModeExitWatcher()
    {
        if (_exitWatcher != null)
        {
            return;
        }

        try
        {
            _exitWatcher = new ManagementEventWatcher(new EventQuery("SELECT * FROM Win32_ProcessStopTrace"));
            _exitWatcher.EventArrived += OnProcessStopped;
            _exitWatcher.Start();
        }
        catch
        {
            _exitWatcher = null;
        }
    }

    public void StopGamingModeExitWatcher()
    {
        if (_exitWatcher != null)
        {
            try
            {
                _exitWatcher.Stop();
                _exitWatcher.EventArrived -= OnProcessStopped;
                _exitWatcher.Dispose();
            }
            catch
            {
            }
            _exitWatcher = null;
        }
    }

    private void OnProcessStopped(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var pidProp = e.NewEvent.Properties["ProcessID"]?.Value;
            if (pidProp == null) return;

            int pid = Convert.ToInt32(pidProp);
            bool wasLast;
            lock (_gamingLock)
            {
                if (!_gamingModePids.Remove(pid))
                {
                    return;
                }

                wasLast = _gamingModePids.Count == 0;
            }

            if (wasLast && App.Current.LegacyGamingMode.IsHeldBy(GameModeOwner.Rule))
            {
                Log("gaming-mode: last armed process exited, releasing the rule's hold");
                App.Current.LegacyGamingMode.Release(GameModeOwner.Rule);
                _gamingLivenessTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            }
            else if (wasLast)
            {
                Log("gaming-mode: armed processes gone; manual Gaming mode session left untouched");
            }
        }
        catch
        {
        }
    }

    private void RaiseChanged()
    {
        if (_dispatcher != null)
        {
            _dispatcher.TryEnqueue(() => RulesChanged?.Invoke(this, EventArgs.Empty));
        }
        else
        {
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        StopKeeper();
        _keeperTimer?.Dispose();
        _keeperTimer = null;
        StopWatcher();
        StopGamingModeExitWatcher();
        _gamingLivenessTimer?.Dispose();
        _gamingLivenessTimer = null;
    }
}
