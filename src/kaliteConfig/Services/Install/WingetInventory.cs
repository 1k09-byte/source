using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace kaliteConfig.Services;

public static class WingetInventory
{
    private static readonly object Gate = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(90);

    private static Dictionary<string, string> _packages = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _builtAtUtc = DateTime.MinValue;
    private static Task? _refresh;

    public static bool HasSnapshot
    {
        get { lock (Gate) return _builtAtUtc != DateTime.MinValue; }
    }

    public static bool IsInstalled(string? wingetId) => TryGetVersion(wingetId, out _);

    public static bool TryGetVersion(string? wingetId, out string version)
    {
        version = "";
        if (string.IsNullOrWhiteSpace(wingetId)) return false;

        lock (Gate)
        {
            if (_packages.TryGetValue(wingetId.Trim(), out string? found))
            {
                version = found;
                return true;
            }
        }

        return false;
    }

    public static void Invalidate()
    {
        lock (Gate)
        {
            _builtAtUtc = DateTime.MinValue;
            _packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static Task WarmUpAsync()
    {
        lock (Gate)
        {
            if (_builtAtUtc != DateTime.MinValue && DateTime.UtcNow - _builtAtUtc < Ttl)
            {
                return Task.CompletedTask;
            }
        }

        return RefreshAsync();
    }

    public static Task RefreshAsync()
    {
        lock (Gate)
        {
            if (_refresh is { IsCompleted: false }) return _refresh;
            _refresh = Task.Run(Build);
            return _refresh;
        }
    }

    public static void RefreshNow()
    {
        try { RefreshAsync().GetAwaiter().GetResult(); } catch { }
    }

    private static void Build()
    {
        var packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "list --accept-source-agreements --disable-interactivity",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var process = Process.Start(psi);
            if (process is not null)
            {
                string stdout = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit(120_000);

                int idColumn = -1;
                int versionColumn = -1;

                foreach (string raw in stdout.Split('\n'))
                {
                    string line = raw.TrimEnd('\r');

                    if (idColumn < 0)
                    {
                        int idIndex = line.IndexOf("Id", StringComparison.Ordinal);
                        int versionIndex = line.IndexOf("Version", StringComparison.Ordinal);
                        if (idIndex >= 0 && versionIndex > idIndex && line.StartsWith("Name", StringComparison.OrdinalIgnoreCase))
                        {
                            idColumn = idIndex;
                            versionColumn = versionIndex;
                        }
                        continue;
                    }

                    if (line.Length <= idColumn) continue;

                    string[] idFields = line.Substring(idColumn)
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (idFields.Length == 0) continue;

                    string id = idFields[0];
                    bool looksLikeId = id.Contains('.')
                        ? !id.StartsWith('.') && !id.EndsWith('.')
                        : id.Length >= 8 && id.All(char.IsLetterOrDigit);
                    if (!looksLikeId) continue;

                    string version = "";
                    if (line.Length > versionColumn)
                    {
                        string[] versionFields = line.Substring(versionColumn)
                            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (versionFields.Length > 0) version = versionFields[0];
                    }

                    packages[id] = version;
                }
            }
        }
        catch { }

        lock (Gate)
        {
            _packages = packages;
            _builtAtUtc = DateTime.UtcNow;
        }
    }
}
