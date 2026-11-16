using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace kaliteConfig.Services.ChangeLog;

public static class ChangeLedger
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private static string? _root;
    private static List<ChangeRecord>? _cache;

    public static string CurrentSessionId { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);

    public static string Root
    {
        get
        {
            lock (Gate)
            {
                _root ??= Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "kaliteConfig", "changes");
                return _root;
            }
        }
    }

    public static void ConfigureStorage(string directory)
    {
        lock (Gate)
        {
            _root = directory;
            _cache = null;
        }
    }

    public static void Reload()
    {
        lock (Gate) { _cache = null; }
    }

    public static ChangeRecord RecordRegistryChange(
        string subsystem, string summary, string keyPath, string valueName,
        int? before, int? after, string result = ChangeResults.Verified, string? detail = null)
    {
        var record = new ChangeRecord
        {
            Subsystem = subsystem,
            Summary = summary,
            KeyPath = keyPath,
            ValueName = valueName,
            BeforeValue = before,
            AfterValue = after,
            Result = result,
            SessionId = CurrentSessionId,
            Detail = detail,
        };

        Append(record);
        return record;
    }

    public static void Append(ChangeRecord record)
    {
        if (string.IsNullOrEmpty(record.SessionId)) record.SessionId = CurrentSessionId;

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Root);
                string path = PathFor(record.AtUtc, Root);
                string line = JsonSerializer.Serialize(record, JsonOptions);
                File.AppendAllText(path, line + "\n", new UTF8Encoding(false));
                _cache = null;
            }
            catch { }
        }
    }

    public static IReadOnlyList<ChangeRecord> Recent(int max = 200)
    {
        var all = ReadAll();
        var reverted = new HashSet<string>(
            all.Where(r => r.RevertsId is not null).Select(r => r.RevertsId!),
            StringComparer.OrdinalIgnoreCase);

        var ordered = new List<ChangeRecord>();
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var record = all[i];
            if (record.RevertsId is not null) continue;
            if (reverted.Contains(record.Id)) record.Result = ChangeResults.Reverted;
            ordered.Add(record);
            if (ordered.Count >= max) break;
        }

        return ordered;
    }

    public static string? Revert(string id)
    {
        var all = ReadAll();
        var record = all.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (record is null) return "That change is no longer in the journal.";

        if (all.Any(r => string.Equals(r.RevertsId, record.Id, StringComparison.OrdinalIgnoreCase)))
            return "That change was already reverted.";

        if (record.Result == ChangeResults.Informational)
            return "This change is restored by its own page, not from the journal.";

        if (record.Result != ChangeResults.Verified)
            return "That change cannot be reverted (" + record.Result + ").";

        int? current = ReadDword(record.KeyPath, record.ValueName);
        if (current != record.AfterValue)
            return "Something else changed this value since kaliteConfig wrote it, so it was left alone.";

        if (!WriteDword(record.KeyPath, record.ValueName, record.BeforeValue))
            return "The old value could not be written back. Run kaliteConfig as administrator.";

        int? check = ReadDword(record.KeyPath, record.ValueName);
        if (check != record.BeforeValue)
            return "The old value was written but reading it back did not match.";

        Append(new ChangeRecord
        {
            Subsystem = record.Subsystem,
            Summary = "Reverted: " + record.Summary,
            KeyPath = record.KeyPath,
            ValueName = record.ValueName,
            BeforeValue = record.AfterValue,
            AfterValue = record.BeforeValue,
            Result = ChangeResults.Reverted,
            SessionId = record.SessionId,
            RevertsId = record.Id,
        });

        return null;
    }

    public static (int Reverted, int Skipped, string[] Messages) RevertSession(string sessionId)
    {
        var messages = new List<string>();
        int reverted = 0;
        int skipped = 0;

        foreach (var record in Recent(500).Where(r =>
                     string.Equals(r.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)))
        {
            string? error = Revert(record.Id);
            if (error is null) reverted++;
            else
            {
                skipped++;
                messages.Add(record.Summary + ": " + error);
            }
        }

        return (reverted, skipped, messages.ToArray());
    }

    private static List<ChangeRecord> ReadAll()
    {
        lock (Gate)
        {
            if (_cache is not null) return _cache;

            var records = new List<ChangeRecord>();
            try
            {
                if (Directory.Exists(Root))
                {
                    foreach (string file in Directory.GetFiles(Root, "changes-*.jsonl").OrderBy(f => f, StringComparer.Ordinal))
                    {
                        foreach (string line in File.ReadAllLines(file))
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            try
                            {
                                var record = JsonSerializer.Deserialize<ChangeRecord>(line, JsonOptions);
                                if (record is not null) records.Add(record);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            records.Sort((a, b) => a.AtUtc.CompareTo(b.AtUtc));
            _cache = records;
            return records;
        }
    }

    private static string PathFor(DateTime atUtc, string root) =>
        Path.Combine(root, "changes-" + atUtc.ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".jsonl");

    private static int? ReadDword(string keyPath, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(keyPath, false);
            return key?.GetValue(valueName) switch
            {
                int i => i,
                long l => (int)l,
                _ => null,
            };
        }
        catch { return null; }
    }

    private static bool WriteDword(string keyPath, string valueName, int? value)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            if (value is null)
            {
                using var existing = baseKey.OpenSubKey(keyPath, true);
                existing?.DeleteValue(valueName, false);
                return true;
            }

            using var key = baseKey.CreateSubKey(keyPath, true);
            if (key is null) return false;
            key.SetValue(valueName, value.Value, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }
}
