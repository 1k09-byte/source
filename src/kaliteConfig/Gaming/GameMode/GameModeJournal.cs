using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace kaliteConfig.Gaming.GameMode;

public sealed class GameModeJournalEntry
{
    public int Pid { get; set; }
    public long CreationTicks { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public uint OriginalPriorityClass { get; set; }
    public uint ThrottlingVersion { get; set; }
    public uint ThrottlingControlMask { get; set; }
    public uint ThrottlingStateMask { get; set; }
    public bool WasSuspended { get; set; }
    public string SuspendedTids { get; set; } = string.Empty;
    public bool CpuSetsChanged { get; set; }
    public bool OriginalCpuSetsRestricted { get; set; }
    public string OriginalCpuSetIds { get; set; } = string.Empty;
    public string SystemCpuSetIds { get; set; } = string.Empty;
    public string BoostDisabledTids { get; set; } = string.Empty;
    public string ThreadPriorityTweaks { get; set; } = string.Empty;
    public bool ProcessBoostChanged { get; set; }
    public bool OriginalProcessBoostDisabled { get; set; }
    public bool IoPriorityChanged { get; set; }
    public uint OriginalIoPriority { get; set; }
    public string ThreadIoPriorityTweaks { get; set; } = string.Empty;
}

public sealed class GameModeJournal
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly string _path;
    private List<GameModeJournalEntry> _entries = new();

    public GameModeJournal(string path)
    {
        _path = path;
        _entries = Load();
    }

    public string Path => _path;

    public bool LoadFailed { get; private set; }

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public IReadOnlyList<GameModeJournalEntry> Snapshot()
    {
        lock (_gate) return _entries.ToArray();
    }

    public void Add(GameModeJournalEntry entry)
    {
        lock (_gate)
        {
            _entries.RemoveAll(e => e.Pid == entry.Pid);
            _entries.Add(entry);
            SaveLocked();
        }
    }

    public void UpsertCpuState(int pid, long ticks, string originalIds, bool originallyRestricted, string systemIds)
    {
        lock (_gate)
        {
            var existing = _entries.Find(e => e.Pid == pid && e.CreationTicks == ticks);
            if (existing is null) return;
            existing.CpuSetsChanged = true;
            existing.OriginalCpuSetIds = originalIds;
            existing.OriginalCpuSetsRestricted = originallyRestricted;
            existing.SystemCpuSetIds = systemIds;
            SaveLocked();
        }
    }

    public GameModeJournalEntry? Find(int pid, long ticks)
    {
        lock (_gate) return _entries.Find(e => e.Pid == pid && e.CreationTicks == ticks);
    }

    public void UpsertThreadState(int pid, long ticks, string boostTids, string priorityTweaks)
    {
        lock (_gate)
        {
            var existing = _entries.Find(e => e.Pid == pid && e.CreationTicks == ticks);
            if (existing is null) return;
            existing.BoostDisabledTids = MergeIds(existing.BoostDisabledTids, boostTids);
            existing.ThreadPriorityTweaks = MergeTweaks(existing.ThreadPriorityTweaks, priorityTweaks);
            SaveLocked();
        }
    }

    public void ClearSuspendedTids(int pid, long ticks)
    {
        lock (_gate)
        {
            var existing = _entries.Find(e => e.Pid == pid && e.CreationTicks == ticks);
            if (existing is null) return;
            existing.SuspendedTids = string.Empty;
            SaveLocked();
        }
    }

    public void ClearThreadTweaks(int pid, long ticks)
    {
        lock (_gate)
        {
            var existing = _entries.Find(e => e.Pid == pid && e.CreationTicks == ticks);
            if (existing is null) return;
            existing.ThreadPriorityTweaks = string.Empty;
            SaveLocked();
        }
    }

    public void UpsertIoPriority(int pid, long ticks, uint original)
    {
        lock (_gate)
        {
            var existing = _entries.Find(e => e.Pid == pid && e.CreationTicks == ticks);
            if (existing is null || existing.IoPriorityChanged) return;
            existing.IoPriorityChanged = true;
            existing.OriginalIoPriority = original;
            SaveLocked();
        }
    }

    public void UpsertThreadIoTweaks(int pid, long ticks, string tweaks)
    {
        lock (_gate)
        {
            var existing = _entries.Find(e => e.Pid == pid && e.CreationTicks == ticks);
            if (existing is null) return;
            existing.ThreadIoPriorityTweaks = MergeTweaks(existing.ThreadIoPriorityTweaks, tweaks);
            SaveLocked();
        }
    }

    public void ClearThreadIoTweaks(int pid, long ticks)
    {
        lock (_gate)
        {
            var existing = _entries.Find(e => e.Pid == pid && e.CreationTicks == ticks);
            if (existing is null) return;
            existing.ThreadIoPriorityTweaks = string.Empty;
            SaveLocked();
        }
    }

    public void UpsertSuspendedTids(int pid, long ticks, string tids)
    {
        lock (_gate)
        {
            var existing = _entries.Find(e => e.Pid == pid && e.CreationTicks == ticks);
            if (existing is null) return;
            existing.SuspendedTids = MergeIds(existing.SuspendedTids, tids);
            SaveLocked();
        }
    }

    private static string MergeIds(string existing, string incoming)
    {
        var ids = new HashSet<uint>();
        foreach (var part in existing.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (uint.TryParse(part, out uint id)) ids.Add(id);
        foreach (var part in incoming.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (uint.TryParse(part, out uint id)) ids.Add(id);
        return string.Join(',', ids);
    }

    private static string MergeTweaks(string existing, string incoming)
    {
        var tweaks = new Dictionary<uint, int>();
        foreach (var part in existing.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split(':');
            if (pair.Length == 2 && uint.TryParse(pair[0], out uint tid) && int.TryParse(pair[1], out int priority))
                tweaks.TryAdd(tid, priority);
        }
        foreach (var part in incoming.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split(':');
            if (pair.Length == 2 && uint.TryParse(pair[0], out uint tid) && int.TryParse(pair[1], out int priority))
                tweaks.TryAdd(tid, priority);
        }
        return string.Join(';', tweaks.Select(pair => pair.Key + ":" + pair.Value));
    }

    public void Remove(int pid, long ticks)
    {
        lock (_gate)
        {
            _entries.RemoveAll(e => e.Pid == pid && e.CreationTicks == ticks);
            SaveLocked();
        }
    }

    public void Replace(IEnumerable<GameModeJournalEntry> entries)
    {
        lock (_gate)
        {
            _entries = new List<GameModeJournalEntry>(entries);
            SaveLocked();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            SaveLocked();
        }
    }

    private List<GameModeJournalEntry> Load()
    {
        try
        {
            bool exists = File.Exists(_path);
            string text = exists ? File.ReadAllText(_path) : string.Empty;
            if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
            var list = exists
                ? JsonSerializer.Deserialize<List<GameModeJournalEntry>>(text, Options) ?? new List<GameModeJournalEntry>()
                : new List<GameModeJournalEntry>();
            return list;
        }
        catch
        {
            LoadFailed = true;
            try { File.Copy(_path, _path + ".unreadable", true); } catch { }
            return new List<GameModeJournalEntry>();
        }
    }

    private void SaveLocked()
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_entries, Options));
            File.Move(temp, _path, true);
        }
        catch
        {
        }
    }
}
