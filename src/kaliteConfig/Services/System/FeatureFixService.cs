using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace kaliteConfig.Services;

/// <summary>Enables Remote Desktop from Enable-RemoteDesktop.reg: services, Terminal Server policy and firewall rules.</summary>
public sealed class RemoteDesktopFixService
{
    private const string Subsystem = "remote-desktop";

    private const string TermServerKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server";
    private const string RdpTcpKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp";
    private const string TerminalPoliciesKey = @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";
    private const string FirewallRulesKey = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";

    private static readonly (string KeyPath, string ValueName, int Value)[] Values =
    {
        (@"SYSTEM\CurrentControlSet\Services\TermService", "Start", 2),
        (@"SYSTEM\CurrentControlSet\Services\UmRdpService", "Start", 3),
        (@"SYSTEM\CurrentControlSet\Services\SessionEnv", "Start", 3),
        (TermServerKey, "fDenyTSConnections", 0),
        (TermServerKey, "TSUserEnabled", 1),
        (TermServerKey, "AllowRemoteRPC", 1),
        (TermServerKey, "fSingleSessionPerUser", 1),
        (RdpTcpKey, "PortNumber", 3389),
        (RdpTcpKey, "fEnableWinStation", 1),
        (RdpTcpKey, "UserAuthentication", 1),
        (RdpTcpKey, "SecurityLayer", 2),
        (RdpTcpKey, "MinEncryptionLevel", 3),
        (TerminalPoliciesKey, "fDenyTSConnections", 0),
        (TerminalPoliciesKey, "UserAuthentication", 1),
        (TerminalPoliciesKey, "SecurityLayer", 2),
        (TerminalPoliciesKey, "MinEncryptionLevel", 3),
        (TerminalPoliciesKey, "DisablePasswordSaving", 0),
        (TerminalPoliciesKey, "fPromptForPassword", 1),
    };

    private static readonly (string ValueName, string Data)[] FirewallRules =
    {
        ("RemoteDesktop-UserMode-In-TCP",
         @"v2.33|Action=Allow|Active=TRUE|Dir=In|Protocol=6|LPort=3389|App=%SystemRoot%\system32\svchost.exe|Svc=TermService|Profile=Domain,Private|Name=Remote Desktop - User Mode (TCP-In)|"),
        ("RemoteDesktop-UserMode-In-UDP",
         @"v2.33|Action=Allow|Active=TRUE|Dir=In|Protocol=17|LPort=3389|App=%SystemRoot%\system32\svchost.exe|Svc=TermService|Profile=Domain,Private|Name=Remote Desktop - User Mode (UDP-In)|"),
    };

    public ServiceFixStatus Status()
    {
        int? term = FeatureFixRegistry.ReadInt(@"SYSTEM\CurrentControlSet\Services\TermService", "Start");
        int? deny = FeatureFixRegistry.ReadInt(TermServerKey, "fDenyTSConnections");
        bool rules = FirewallRules.All(r => FeatureFixRegistry.ReadString(FirewallRulesKey, r.ValueName) is not null);

        bool applied = term == 2 && deny == 0 && rules;
        string summary = (applied ? "Already set: " : "Current: ")
            + $"TermService {FeatureFixRegistry.Format(term)}, fDenyTSConnections {FeatureFixRegistry.Format(deny)}, "
            + (rules ? "firewall rules in place" : "firewall rules missing");
        return new ServiceFixStatus(applied, summary);
    }

    public ServiceFixResult Apply()
    {
        (var changed, var failed) = FeatureFixRegistry.WriteDwords(Subsystem, Values);
        if (failed.Count > 0)
            return new ServiceFixResult(false,
                "Could not write " + string.Join(", ", failed) + ". Run kaliteConfig as administrator.");

        string rulesMessage = FeatureFixRegistry.WriteFirewallRules(FirewallRules);
        if (rulesMessage.Length == 0)
            return new ServiceFixResult(false, "Could not write the Remote Desktop firewall rules. Run kaliteConfig as administrator.");

        string message = changed.Count > 0
            ? "Remote Desktop enabled: wrote " + string.Join(", ", changed) + "."
            : "Remote Desktop: nothing to change, everything is already in place.";
        message += " " + rulesMessage + " A restart is recommended.";
        return new ServiceFixResult(true, message);
    }
}

/// <summary>Restores printing from Enable-Printing.reg: spooler services, print policies and firewall rules.</summary>
public sealed class PrinterFixService
{
    private const string Subsystem = "printer-fix";

    private const string PrintersPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows NT\Printers";
    private const string PointAndPrintKey = @"SOFTWARE\Policies\Microsoft\Windows NT\Printers\PointAndPrint";
    private const string ExplorerPolicyKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private const string PrintControlKey = @"SYSTEM\CurrentControlSet\Control\Print";
    private const string FirewallRulesKey = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";

    private static readonly (string KeyPath, string ValueName, int Value)[] Values =
    {
        (@"SYSTEM\CurrentControlSet\Services\Spooler", "Start", 2),
        (@"SYSTEM\CurrentControlSet\Services\PrintNotify", "Start", 3),
        (PrintersPolicyKey, "DisableHTTPPrinting", 0),
        (PrintersPolicyKey, "DisableWebPnPDownload", 0),
        (PrintControlKey, "RemoveMPDW", 0),
        (PrintControlKey, "RemoveMXDW", 0),
        (ExplorerPolicyKey, "NoAddPrinter", 0),
        (ExplorerPolicyKey, "NoDeletePrinter", 0),
        (ExplorerPolicyKey, "NoPrinterTabs", 0),
        (PointAndPrintKey, "RestrictDriverInstallationToAdministrators", 0),
        (PointAndPrintKey, "Restricted", 0),
        (PointAndPrintKey, "NoWarningNoElevationOnInstall", 0),
        (PointAndPrintKey, "UpdatePromptSettings", 0),
    };

    private static readonly (string KeyPath, string ValueName, int Value)[] PerUserValues =
    {
        (@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoAddPrinter", 0),
        (@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDeletePrinter", 0),
        (@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoPrinterTabs", 0),
    };

    private static readonly string[] BlockValueNames =
    {
        "DisableHTTPPrinting", "DisableWebPnPDownload", "RemoveMPDW", "RemoveMXDW",
        "NoAddPrinter", "NoDeletePrinter", "NoPrinterTabs",
        "RestrictDriverInstallationToAdministrators", "Restricted", "NoWarningNoElevationOnInstall", "UpdatePromptSettings",
    };

    private static readonly (string ValueName, string Data)[] FirewallRules =
    {
        ("FPS-SMB-In-TCP",
         @"v2.33|Action=Allow|Active=TRUE|Dir=In|Protocol=6|LPort=445|App=System|Profile=Domain,Private|Name=File and Printer Sharing (SMB-In)|"),
        ("FPS-NB_Session-In-TCP",
         @"v2.33|Action=Allow|Active=TRUE|Dir=In|Protocol=6|LPort=139|App=System|Profile=Domain,Private|Name=File and Printer Sharing (NB-Session-In)|"),
        ("FPS-NB_Name-In-UDP",
         @"v2.33|Action=Allow|Active=TRUE|Dir=In|Protocol=17|LPort=137|App=System|Profile=Domain,Private|Name=File and Printer Sharing (NB-Name-In)|"),
        ("FPS-NB_Datagram-In-UDP",
         @"v2.33|Action=Allow|Active=TRUE|Dir=In|Protocol=17|LPort=138|App=System|Profile=Domain,Private|Name=File and Printer Sharing (NB-Datagram-In)|"),
        ("WSD-In-TCP",
         @"v2.33|Action=Allow|Active=TRUE|Dir=In|Protocol=6|LPort=5357|App=System|Profile=Domain,Private|Name=WSD (TCP-In)|"),
        ("WSD-Out-UDP",
         @"v2.33|Action=Allow|Active=TRUE|Dir=Out|Protocol=17|LPort=3702|App=System|Profile=Domain,Private|Name=WSD Discovery (UDP-Out)|"),
    };

    public ServiceFixStatus Status()
    {
        int? spooler = FeatureFixRegistry.ReadInt(@"SYSTEM\CurrentControlSet\Services\Spooler", "Start");
        int blocks = CountBlocks();
        bool rules = FirewallRules.All(r => FeatureFixRegistry.ReadString(FirewallRulesKey, r.ValueName) is not null);

        bool applied = spooler == 2 && blocks == 0 && rules;
        string summary = (applied ? "Already set: " : "Current: ")
            + $"Spooler {FeatureFixRegistry.Format(spooler)}, "
            + (blocks == 0 ? "print policies clear" : $"{blocks} print policy block(s) set") + ", "
            + (rules ? "firewall rules in place" : "firewall rules missing");
        return new ServiceFixStatus(applied, summary);
    }

    public ServiceFixResult Apply()
    {
        (var changed, var failed) = FeatureFixRegistry.WriteDwords(Subsystem, Values);
        FeatureFixRegistry.WritePerUserDwords(PerUserValues); // HKCU mirror of the Explorer policy, not journaled

        if (failed.Count > 0)
            return new ServiceFixResult(false,
                "Could not write " + string.Join(", ", failed) + ". Run kaliteConfig as administrator.");

        string rulesMessage = FeatureFixRegistry.WriteFirewallRules(FirewallRules);
        if (rulesMessage.Length == 0)
            return new ServiceFixResult(false, "Could not write the printer firewall rules. Run kaliteConfig as administrator.");

        string message = changed.Count > 0
            ? "Printing restored: wrote " + string.Join(", ", changed) + "."
            : "Printing: nothing to change, everything is already in place.";
        message += " " + rulesMessage + " A restart is recommended.";
        return new ServiceFixResult(true, message);
    }

    private static int CountBlocks()
    {
        int blocks = 0;
        foreach (string name in BlockValueNames)
        {
            foreach (var (keyPath, valueName, target) in Values)
            {
                if (valueName != name || target != 0) continue;
                if (FeatureFixRegistry.ReadInt(keyPath, valueName) is int v && v != 0) blocks++;
            }
        }
        return blocks;
    }
}

/// <summary>Shared registry helpers for the feature fixes. Only HKLM DWORD values are journaled (the ledger is HKLM DWORD based).</summary>
internal static class FeatureFixRegistry
{
    public static string Format(int? value) => value?.ToString() ?? "absent";

    public static int? ReadInt(string keyPath, string valueName)
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

    public static string? ReadString(string keyPath, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(keyPath, false);
            return key?.GetValue(valueName) as string;
        }
        catch { return null; }
    }

    public static (List<string> Changed, List<string> Failed) WriteDwords(
        string subsystem, IReadOnlyList<(string KeyPath, string ValueName, int Value)> values)
    {
        var changed = new List<string>();
        var failed = new List<string>();

        foreach (var (keyPath, valueName, target) in values)
        {
            int? before = ReadInt(keyPath, valueName);
            if (before == target) continue;

            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = baseKey.CreateSubKey(keyPath, true)
                    ?? throw new UnauthorizedAccessException("The key could not be opened.");
                key.SetValue(valueName, target, RegistryValueKind.DWord);

                int? after = ReadInt(keyPath, valueName);

                if (after == target) changed.Add($"{valueName} {before} -> {target}");
                else failed.Add(valueName);
            }
            catch
            {
                failed.Add(valueName);
            }
        }

        return (changed, failed);
    }

    public static void WritePerUserDwords(IReadOnlyList<(string KeyPath, string ValueName, int Value)> values)
    {
        foreach (var (keyPath, valueName, target) in values)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(keyPath, true);
                key?.SetValue(valueName, target, RegistryValueKind.DWord);
            }
            catch { }
        }
    }

    private const string FirewallRulesKey = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";

    /// <summary>Writes the firewall rule strings when missing or stale. Returns a status sentence, or "" when a write failed.</summary>
    public static string WriteFirewallRules(IReadOnlyList<(string ValueName, string Data)> rules)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.CreateSubKey(FirewallRulesKey, true);
            if (key is null) return string.Empty;

            int written = 0;
            foreach (var (valueName, data) in rules)
            {
                if (ReadString(FirewallRulesKey, valueName) == data) continue;
                key.SetValue(valueName, data, RegistryValueKind.String);
                written++;
            }

            bool allPresent = rules.All(r => ReadString(FirewallRulesKey, r.ValueName) == r.Data);
            if (!allPresent) return string.Empty;
            return written > 0
                ? $"Firewall rules written ({written} rule{(written == 1 ? "" : "s")})."
                : "Firewall rules were already in place.";
        }
        catch
        {
            return string.Empty;
        }
    }
}
