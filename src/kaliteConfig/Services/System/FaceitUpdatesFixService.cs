using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace kaliteConfig.Services;

/// <summary>
/// Replicates the FACEIT updates .bat: clears the Windows Update policy blocks that keep
/// FACEIT from downloading updates, turns the Microsoft Update service back on, refreshes
/// group policy and restarts the Windows Update service.
/// </summary>
public sealed class FaceitUpdatesFixService
{
    private const string Subsystem = "faceit-updates";

    private const string UpdateRoot = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string AuSubKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
    private const string AllowValueName = "AllowMUUpdateService";
    private const string WuauservName = "wuauserv";

    private static readonly (string KeyPath, string ValueName)[] BlockingValues =
    {
        (UpdateRoot, "SetDisableUXWUAccess"),
        (AuSubKey, "NoAutoUpdate"),
        (UpdateRoot, "DisableDualScan"),
    };

    public ServiceFixStatus Status()
    {
        var present = new List<string>();
        foreach (var (keyPath, valueName) in BlockingValues)
            if (ReadInt(keyPath, valueName) is not null)
                present.Add(valueName);

        int? allow = ReadInt(AuSubKey, AllowValueName);
        bool clean = present.Count == 0 && allow == 1;

        string summary = clean
            ? "Already clean: no blocking Windows Update policies, AllowMUUpdateService is 1."
            : "Current: " + (present.Count > 0 ? string.Join(", ", present) + " present" : "no blocking policies")
              + (allow == 1 ? ", AllowMUUpdateService is 1" : ", AllowMUUpdateService not set");

        return new ServiceFixStatus(clean, summary);
    }

    public ServiceFixResult Apply()
    {
        var removed = new List<string>();
        var failed = new List<string>();

        foreach (var (keyPath, valueName) in BlockingValues)
        {
            int? before = ReadInt(keyPath, valueName);
            if (before is null) continue; // nothing to remove; the .bat deletes silently too

            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(keyPath, writable: true);
                if (key is null)
                {
                    failed.Add(valueName);
                    continue;
                }

                key.DeleteValue(valueName, throwOnMissingValue: false);
                int? after = ReadInt(keyPath, valueName);

                if (after is null) removed.Add(valueName);
                else failed.Add(valueName);
            }
            catch
            {
                failed.Add(valueName);
            }
        }

        if (failed.Count > 0)
            return new ServiceFixResult(false,
                "Could not remove " + string.Join(", ", failed) + ". Run kaliteConfig as administrator.");

        // reg add ...\AU /v AllowMUUpdateService /t REG_DWORD /d 1 /f
        int? allowBefore = ReadInt(AuSubKey, AllowValueName);
        bool allowOk = true;
        if (allowBefore != 1)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = baseKey.CreateSubKey(AuSubKey, writable: true)
                    ?? throw new UnauthorizedAccessException("The WindowsUpdate\\AU key could not be opened.");

                key.SetValue(AllowValueName, 1, RegistryValueKind.DWord);
                int? after = ReadInt(AuSubKey, AllowValueName);
                allowOk = after == 1;
            }
            catch
            {
                return new ServiceFixResult(false,
                    "Could not set AllowMUUpdateService. Run kaliteConfig as administrator.");
            }
        }

        // gpupdate /force
        string? gpError = Run("gpupdate.exe", "/force");

        // net stop wuauserv && net start wuauserv
        string? stopError = Run("net.exe", "stop " + WuauservName);
        string? startError = Run("net.exe", "start " + WuauservName);

        var message = new StringBuilder();
        message.Append(removed.Count > 0
            ? "Faceit Updates: removed " + string.Join(", ", removed) + "."
            : "Faceit Updates: no blocking policies found.");
        if (allowOk) message.Append(" AllowMUUpdateService is on.");

        if (gpError is null && stopError is null && startError is null)
            message.Append(" Group policy refreshed and the Windows Update service restarted.");
        else
            message.Append(" The group policy refresh or service restart did not finish")
                .Append(gpError is null ? "" : " (gpupdate: " + gpError + ")")
                .Append(" - try again as administrator.");

        return new ServiceFixResult(true, message.ToString());
    }

    private static int? ReadInt(string keyPath, string valueName)
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

    private static string? Run(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null) return "could not start";
            process.WaitForExit(60000);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
