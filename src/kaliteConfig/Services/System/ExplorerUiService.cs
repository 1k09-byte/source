using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace kaliteConfig.Services;

/// <summary>
/// Switches File Explorer between the "Old" classic preset (Explorer.reg) and the
/// "Default" Windows 11 UI (Explorer-Default-UI.reg). The presets are bundled .reg
/// files imported with reg.exe, because they span both hives and mix value writes,
/// value deletions and key deletions. The current state is detected from the classic
/// context-menu marker key, which the Old preset creates and the Default preset deletes.
/// </summary>
public sealed class ExplorerUiService
{
    private const string OldMarkerKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

    private static string RegFilePath(bool old) => Path.Combine(
        AppContext.BaseDirectory, "Assets", "Registry", old ? "Explorer.reg" : "Explorer-Default-UI.reg");

    public bool IsOld()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(OldMarkerKey);
            return key is not null;
        }
        catch { return false; }
    }

    /// <summary>Imports the preset's .reg file and restarts explorer. Returns null on success, otherwise an error message.</summary>
    public string? Apply(bool old)
    {
        string file = RegFilePath(old);
        if (!File.Exists(file))
            return "The " + (old ? "Old" : "Default") + " Explorer preset file is missing from the app folder.";

        using (var process = Process.Start(new ProcessStartInfo
        {
            FileName = "reg.exe",
            Arguments = "import \"" + file + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        }))
        {
            if (process is null) return "reg.exe could not be started.";
            if (!process.WaitForExit(120_000))
            {
                try { process.Kill(); } catch { }
                return "The registry import timed out.";
            }
            if (process.ExitCode != 0)
                return $"reg.exe failed with exit code {process.ExitCode}. Run kaliteConfig as administrator.";
        }

        RestartExplorer();
        return null;
    }

    private static void RestartExplorer()
    {
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try { process.Kill(); } catch { }
        }

        // The shell sometimes relaunches itself; only start a new one if it did not come back.
        for (int waited = 0; waited < 4000; waited += 200)
        {
            Thread.Sleep(200);
            if (Process.GetProcessesByName("explorer").Length > 0) return;
        }

        try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true }); }
        catch { }
    }
}
