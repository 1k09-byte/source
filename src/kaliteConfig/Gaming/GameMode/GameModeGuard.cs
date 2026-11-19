using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using kaliteConfig.Native;

namespace kaliteConfig.Gaming.GameMode;

public sealed record GuardVerdict(bool Allowed, string Reason);

public sealed record ProcessFact(
    int Pid,
    int ParentPid,
    string Name,
    string? ImagePath,
    long CreationTicks,
    int SessionId);

public sealed class GameModeGuard
{
    private readonly object _gate = new();
    private Dictionary<int, string[]> _servicePids = new();
    private DateTime _serviceRefreshUtc = DateTime.MinValue;
    private readonly int _selfPid = Environment.ProcessId;
    private readonly string _selfSid;
    private readonly string _selfName;

    public static readonly string[] ImportantServiceNames =
    {
        "WlanSvc", "WwanSvc", "WiFiNetworkManager", "Dot3svc", "BthServ", "BthAvctpSvc", "BTAGService",
        "BluetoothUserService*", "BTDevMgr", "Dhcp", "Dnscache", "NlaSvc", "netprofm", "nsi", "iphlpsvc", "NcaSvc", "Wcmsvc",
        "RemoteAccess", "IKEEXT", "PolicyAgent", "SharedAccess",
        "Audiosrv", "AudioEndpointBuilder", "AudioSrvGroup*",
        "RpcSs", "RpcEptMapper", "DcomLaunch", "Power", "PlugPlay", "EventLog", "ProfSvc", "Winmgmt",
        "LSM", "SamSs", "gpsvc", "Themes", "UserManager", "StateRepository", "W32Time", "TimeBrokerSvc",
        "DeviceAssociationService", "DeviceInstall", "DsmSvc", "ShellHWDetection",
        "TabletInputService", "TextInputManagementService", "camsvc", "FrameServer",
        "WinDefend", "SecurityHealthService", "WdNisSvc", "Sense", "wscsvc", "mpssvc", "BFE",
        "NVDisplay.ContainerLocalSystem", "AMD External Events Utility", "igfxCUIService*",
    };

    public GameModeGuard()
    {
        _selfSid = CurrentSid();
        _selfName = Path.GetFileName(Environment.ProcessPath ?? "kaliteConfig.exe");
    }

    public IReadOnlyCollection<int> SnapshotServicePids()
    {
        lock (_gate)
        {
            if (DateTime.UtcNow - _serviceRefreshUtc < TimeSpan.FromSeconds(30))
                return _servicePids.Keys.ToArray();
        }
        var found = ReadServicePids();
        lock (_gate)
        {
            _servicePids = found;
            _serviceRefreshUtc = DateTime.UtcNow;
            return _servicePids.Keys.ToArray();
        }
    }

    private string[] ServicesFor(int pid)
    {
        lock (_gate)
        {
            if (DateTime.UtcNow - _serviceRefreshUtc < TimeSpan.FromSeconds(30))
                return _servicePids.TryGetValue(pid, out var cached) ? cached : Array.Empty<string>();
        }

        var found = ReadServicePids();
        lock (_gate)
        {
            _servicePids = found;
            _serviceRefreshUtc = DateTime.UtcNow;
            return _servicePids.TryGetValue(pid, out var names) ? names : Array.Empty<string>();
        }
    }

    public bool IsService(int pid) => ServicesFor(pid).Length > 0;

    public static bool IsImportantService(string serviceName)
    {
        if (string.IsNullOrEmpty(serviceName)) return false;
        foreach (var pattern in ImportantServiceNames)
        {
            if (pattern.EndsWith('*'))
            {
                if (serviceName.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (serviceName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static readonly string[] ConnectivityServiceNames =
    {
        "WlanSvc", "WwanSvc", "WiFiNetworkManager", "Dot3svc", "BthServ", "BthAvctpSvc", "BTAGService",
        "BluetoothUserService*", "BTDevMgr", "Dhcp", "Dnscache", "NlaSvc", "netprofm", "nsi", "iphlpsvc", "NcaSvc", "Wcmsvc", "WcmSvc",
        "RasMan", "RemoteAccess", "IKEEXT", "PolicyAgent", "SharedAccess", "SstpSvc", "vpnagent*",
    };

    public static bool IsConnectivityService(string serviceName)
    {
        if (string.IsNullOrEmpty(serviceName)) return false;
        foreach (var pattern in ConnectivityServiceNames)
        {
            if (pattern.EndsWith('*'))
            {
                if (serviceName.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (serviceName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsBoostExempt(string imageName)
    {
        if (string.IsNullOrEmpty(imageName)) return false;
        if (imageName.Equals("dwm.exe", StringComparison.OrdinalIgnoreCase)) return true;
        return IsConnectivityImage(imageName);
    }

    private static readonly string[] ConnectivityImageTokens =
    {
        "wlan", "wwan", "wifi", "wi-fi", "wireless", "bluetooth", "vpn", "btag", "btdev", "ndisuio",
    };

    public static bool IsConnectivityImage(string imageName)
    {
        if (string.IsNullOrEmpty(imageName)) return false;
        foreach (var name in BackgroundCatalog.ConnectivityFamily)
            if (imageName.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;

        string stem = imageName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? imageName[..^4] : imageName;
        if (stem.StartsWith("bth", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var token in ConnectivityImageTokens)
            if (stem.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public GuardVerdict Evaluate(ProcessFact fact, IReadOnlySet<int> gamePids, IReadOnlySet<string> gamePaths, GameModeSettings settings, IReadOnlySet<string>? lockedRules = null)
        => Decide(fact, gamePids, gamePaths, settings, lockedRules);

    public GuardVerdict EvaluateBoost(ProcessFact fact, IReadOnlySet<int> gamePids, IReadOnlySet<string> gamePaths, GameModeSettings settings, IReadOnlySet<string> lockedRules)
    {
        if (fact.Pid <= 4) return new GuardVerdict(false, "System process");
        if (fact.Pid == _selfPid) return new GuardVerdict(false, "kaliteConfig itself");
        if (string.Equals(fact.Name, _selfName, StringComparison.OrdinalIgnoreCase))
            return new GuardVerdict(false, "kaliteConfig helper");

        var image = fact.Name ?? string.Empty;
        if (image.Equals("System", StringComparison.OrdinalIgnoreCase)
            || image.Equals("Registry", StringComparison.OrdinalIgnoreCase)
            || image.Equals("Idle", StringComparison.OrdinalIgnoreCase)
            || image.Equals("Memory Compression", StringComparison.OrdinalIgnoreCase))
            return new GuardVerdict(false, "System process");

        if (IsIgnored(settings, image, fact.ImagePath))
            return new GuardVerdict(false, "On your ignored apps list");

        if (gamePids.Contains(fact.Pid))
            return new GuardVerdict(false, "The game, its launcher or its children");
        if (!string.IsNullOrEmpty(fact.ImagePath) && gamePaths.Contains(fact.ImagePath))
            return new GuardVerdict(false, "The game executable");

        if (IsBoostExempt(image))
            return new GuardVerdict(false, "dwm or an internet, Bluetooth or VPN process, left alone");

        foreach (var service in ServicesFor(fact.Pid))
            if (IsConnectivityService(service))
                return new GuardVerdict(false, $"Hosts {service}, an internet or Bluetooth service");

        if (lockedRules.Count > 0 && MatchesLockedRule(lockedRules, image))
            return new GuardVerdict(false, "Locked by one of your thread rules");

        return new GuardVerdict(true, "Not the game, dwm or a connectivity service");
    }

    private GuardVerdict Decide(ProcessFact fact, IReadOnlySet<int> gamePids, IReadOnlySet<string> gamePaths, GameModeSettings settings, IReadOnlySet<string>? lockedRules = null)
    {
        if (fact.Pid <= 4) return new GuardVerdict(false, "System process");
        if (fact.Pid == _selfPid) return new GuardVerdict(false, "kaliteConfig itself");
        if (string.Equals(fact.Name, _selfName, StringComparison.OrdinalIgnoreCase))
            return new GuardVerdict(false, "kaliteConfig helper");
        var services = ServicesFor(fact.Pid);
        bool isService = services.Length > 0;

        if (fact.SessionId == 0 && !isService) return new GuardVerdict(false, "Session 0 process");

        var image = fact.Name;
        if (image.Equals("System", StringComparison.OrdinalIgnoreCase)
            || image.Equals("Registry", StringComparison.OrdinalIgnoreCase)
            || image.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            return new GuardVerdict(false, "System process");

        if (IsIgnored(settings, image, fact.ImagePath))
            return new GuardVerdict(false, "On your ignored apps list");

        if (IsAntiCheatName(image))
        {
            var antiCheatDetail = Inspect(NativeMethods.Handles.OpenProcess(
                NativeMethods.ProcessAccess.QueryLimitedInformation, false, (uint)fact.Pid), allowSystem: true);
            if (antiCheatDetail != null) return new GuardVerdict(false, antiCheatDetail);
            return new GuardVerdict(true, "Anti-cheat, useless background");
        }

        if (gamePids.Contains(fact.Pid))
            return new GuardVerdict(false, "The game, its launcher or its children");
        if (!string.IsNullOrEmpty(fact.ImagePath) && gamePaths.Contains(fact.ImagePath))
            return new GuardVerdict(false, "The game executable");

        foreach (var shell in BackgroundCatalog.ShellFamily)
            if (image.Equals(shell, StringComparison.OrdinalIgnoreCase))
                return new GuardVerdict(false, "Shell, compositor, audio or servicing process");

        if (IsConnectivityImage(image))
            return new GuardVerdict(false, "Wi-Fi, Bluetooth, network or VPN process, left alone");

        if (lockedRules is { Count: > 0 } && MatchesLockedRule(lockedRules, image))
            return new GuardVerdict(false, "Locked by one of your thread rules");

        if (isService)
        {
            foreach (var service in services)
                if (IsImportantService(service))
                    return new GuardVerdict(false, $"Hosts {service}, an important service");

            var serviceDetail = Inspect(NativeMethods.Handles.OpenProcess(
                NativeMethods.ProcessAccess.QueryLimitedInformation, false, (uint)fact.Pid), allowSystem: true);
            if (serviceDetail != null) return new GuardVerdict(false, serviceDetail);
            return new GuardVerdict(true, "Background service, not important");
        }

        if (!string.IsNullOrEmpty(fact.ImagePath))
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windows)
                && fact.ImagePath.StartsWith(windows, StringComparison.OrdinalIgnoreCase)
                && !fact.ImagePath.StartsWith(Path.Combine(windows, "SystemApps"), StringComparison.OrdinalIgnoreCase))
                return new GuardVerdict(false, "Windows component, not a service");
        }

        var detail = Inspect(NativeMethods.Handles.OpenProcess(
            NativeMethods.ProcessAccess.QueryLimitedInformation, false, (uint)fact.Pid),
            allowSystem: false);
        if (detail != null) return new GuardVerdict(false, detail);

        return new GuardVerdict(true, "Background app");
    }

    internal static bool MatchesLockedRule(IReadOnlySet<string> lockedRules, string imageName)
    {
        var stem = imageName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? imageName[..^4] : imageName;
        foreach (var rule in lockedRules)
        {
            foreach (var part in rule.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (MatchesName(part, imageName)) return true;
                if (!part.Contains('.') && part.Equals(stem, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    private string? Inspect(SafeProcessHandle handle, bool allowSystem)
    {
        if (handle.IsInvalid) return "Cannot query the process";
        try
        {
            IntPtr raw = handle.DangerousGetHandle();
            var protection = new NativeMethods.GameMode.ProtectionLevelInfo();
            if (!NativeMethods.GameMode.GetProcessInformation(raw, NativeMethods.GameMode.ProcessProtectionLevelInfo, ref protection, Marshal.SizeOf<NativeMethods.GameMode.ProtectionLevelInfo>()))
                return "Protection state unknown";
            if (protection.ProtectionLevel != 0 && protection.ProtectionLevel != 0xFFFFFFFE)
                return "Protected process (PPL)";

            if (NativeMethods.GameMode.IsProcessCritical(raw, out bool critical) && critical)
                return "Critical system process";

            var sid = ProcessSid(raw);
            if (sid == null) return "Cannot read the process token";
            if (sid == "S-1-5-18" || sid == "S-1-5-19" || sid == "S-1-5-20")
            {
                if (allowSystem) return null;
                return "Runs as SYSTEM, LOCAL SERVICE or NETWORK SERVICE";
            }
            if (!string.Equals(sid, _selfSid, StringComparison.OrdinalIgnoreCase))
                return "Belongs to another user";
            return null;
        }
        catch
        {
            return "Cannot query the process";
        }
        finally
        {
            handle.Dispose();
        }
    }

    private static string? ProcessSid(IntPtr process)
    {
        if (!NativeMethods.GameMode.OpenProcessToken(process, 0x0008, out IntPtr token)) return null;
        try
        {
            NativeMethods.GameMode.GetTokenInformation(token, 1, IntPtr.Zero, 0, out int needed);
            if (needed <= 0) return null;
            IntPtr buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!NativeMethods.GameMode.GetTokenInformation(token, 1, buffer, needed, out _)) return null;
                IntPtr sid = Marshal.ReadIntPtr(buffer);
                if (sid == IntPtr.Zero) return null;
                if (!NativeMethods.GameMode.ConvertSidToStringSid(sid, out IntPtr text)) return null;
                try { return Marshal.PtrToStringUni(text); }
                finally { NativeMethods.GameMode.LocalFree(text); }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally
        {
            NativeMethods.Handles.CloseHandle(token);
        }
    }

    private static string CurrentSid()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static bool IsUnsafeGameTarget(ProcessFact fact)
    {
        if (fact.Pid <= 4 || fact.SessionId == 0) return true;
        if (string.Equals(fact.Name, "System", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fact.Name, "Registry", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fact.Name, "Idle", StringComparison.OrdinalIgnoreCase))
            return true;
        return IsAntiCheatName(fact.Name);
    }

    public static HashSet<int> ExpandGameDescendants(IReadOnlyDictionary<int, int> parents, int gamePid)
    {
        var set = new HashSet<int> { gamePid };
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var pair in parents)
            {
                if (set.Contains(pair.Key) || !set.Contains(pair.Value)) continue;
                set.Add(pair.Key);
                changed = true;
            }
        }
        return set;
    }

    /// <summary>
    /// True when the process is on the user's ignored apps (Never touch) list.
    /// Entries containing a directory separator are matched against the full
    /// image path; any other entry is matched as an image name or wildcard.
    /// </summary>
    public static bool IsIgnored(GameModeSettings settings, string? imageName, string? imagePath)
    {
        foreach (var entry in settings.NeverTouch)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            if (IsPathEntry(entry))
            {
                if (!string.IsNullOrEmpty(imagePath)
                    && string.Equals(entry, imagePath, StringComparison.OrdinalIgnoreCase))
                    return true;
                continue;
            }
            if (!string.IsNullOrEmpty(imageName) && MatchesName(entry, imageName)) return true;
        }
        return false;
    }

    private static bool IsPathEntry(string entry)
        => entry.Contains('\\') || entry.Contains('/') || Path.IsPathRooted(entry);

    public static bool MatchesName(string pattern, string imageName)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var trimmed = pattern.Trim();
        if (trimmed.Contains('*') || trimmed.Contains('?'))
        {
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(trimmed)
                .Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(imageName, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        if (imageName.Equals(trimmed, StringComparison.OrdinalIgnoreCase)) return true;

        return !trimmed.Contains('.') && imageName.Equals(trimmed + ".exe", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAntiCheatName(string image)
    {
        foreach (var name in BackgroundCatalog.AntiCheatNames)
            if (image.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var token in new[] { "anticheat", "battleye", "easyanticheat", "faceit", "gameguard", "xigncode", "punkbuster", "vgc.exe", "vgk.sys", "bedaisy" })
            if (image.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static IReadOnlySet<int> ExpandGameTree(IReadOnlyList<ProcessFact> facts, int gamePid)
    {
        var set = new HashSet<int> { gamePid };
        var parents = facts.ToDictionary(f => f.Pid, f => f.ParentPid);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var fact in facts)
            {
                if (set.Contains(fact.Pid)) continue;
                if (parents.TryGetValue(fact.Pid, out int parent) && set.Contains(parent))
                {
                    set.Add(fact.Pid);
                    changed = true;
                }
            }
        }
        int current = gamePid;
        for (int i = 0; i < 32 && parents.TryGetValue(current, out int parent) && parent > 4; i++)
        {
            set.Add(parent);
            current = parent;
        }
        return set;
    }

    private static Dictionary<int, string[]> ReadServicePids()
    {
        var found = new Dictionary<int, List<string>>();
        IntPtr manager = NativeMethods.GameMode.OpenSCManager(null, null, NativeMethods.GameMode.ScManagerEnumerateService);
        if (manager == IntPtr.Zero) return new Dictionary<int, string[]>();
        try
        {
            int resume = 0;
            int size = 64 * 1024;
            while (true)
            {
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (!NativeMethods.GameMode.EnumServicesStatusEx(
                            manager, NativeMethods.GameMode.ScStatusProcessInfo,
                            NativeMethods.GameMode.ServiceWin32, NativeMethods.GameMode.ServiceStateAll,
                            buffer, size, out int needed, out int returned, ref resume, null))
                    {
                        if (needed > size)
                        {
                            size = needed;
                            continue;
                        }
                        break;
                    }

                    int structSize = Marshal.SizeOf<NativeMethods.GameMode.EnumServiceStatusProcess>();
                    for (int i = 0; i < returned; i++)
                    {
                        var item = Marshal.PtrToStructure<NativeMethods.GameMode.EnumServiceStatusProcess>(buffer + i * structSize);
                        if (item.Status.ProcessId <= 4) continue;
                        int pid = (int)item.Status.ProcessId;
                        if (!found.TryGetValue(pid, out var names)) found[pid] = names = new List<string>();
                        var serviceName = Marshal.PtrToStringUni(item.ServiceName);
                        if (!string.IsNullOrEmpty(serviceName)) names.Add(serviceName);
                    }
                    if (resume == 0) break;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        catch
        {
        }
        finally
        {
            NativeMethods.GameMode.CloseServiceHandle(manager);
        }
        return found.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }
}
