using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace kaliteConfig.Services;

public static class WindhawkPostInstallService
{
    public const string IconCacheScriptName = "Clear_icon_cache.bat";

    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(90);

    public static string? BundledScriptPath
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(dir)) return null;

            foreach (var candidate in new[]
            {
                Path.Combine(dir, "Assets", "Windhawk", IconCacheScriptName),
                Path.Combine(dir, "Assets", IconCacheScriptName),
            })
            {
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }

    public static async Task<string> RefreshIconCacheAsync(IProgress<string>? status = null, CancellationToken ct = default)
    {
        string? script = BundledScriptPath;
        if (script is null)
            return "Icon cache refresh skipped: " + IconCacheScriptName + " is missing from the install.";

        status?.Report("Clearing the icon cache so the new Windhawk styles appear...");

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/d /c \"\"" + script + "\"\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return "Icon cache refresh could not start.";

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ScriptTimeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                return "Icon cache refresh timed out; Explorer was restarted, the cache may still be stale.";
            }

            return process.ExitCode == 0
                ? "Icon cache cleared and Explorer restarted."
                : $"Icon cache refresh finished with exit {process.ExitCode}.";
        }
        catch (Exception ex)
        {
            return "Icon cache refresh failed: " + ex.Message;
        }
    }
}
