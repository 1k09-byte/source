using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using kaliteConfig.Services.ChangeLog;

namespace kaliteConfig.Services;

public sealed record StartTarget(string ServiceName, int Start);

public sealed record ServiceFix(
    string Id,
    string Title,
    string Description,
    bool NeedsRestart,
    StartTarget[] Targets);

public sealed record ServiceFixStatus(bool Applied, string Summary);

public sealed record ServiceFixResult(bool Ok, string Message);

public sealed class ServiceStartFixService
{
    public const string DefaultServicesRoot = @"SYSTEM\CurrentControlSet\Services";
    private const string Subsystem = "service-fixes";

    private readonly RegistryHive _hive;
    private readonly string _servicesRoot;
    private readonly bool _journal;

    public ServiceStartFixService()
        : this(RegistryHive.LocalMachine, DefaultServicesRoot, journal: true)
    {
    }

    public ServiceStartFixService(RegistryHive hive, string servicesRoot, bool journal)
    {
        _hive = hive;
        _servicesRoot = servicesRoot;
        _journal = journal;
    }

    public static readonly ServiceFix ObsCapture = new(
        "obs-capture",
        "OBS capture",
        "Sets the Windows capture service back to Manual, which restores game and window capture in OBS when the service was set to Disabled.",
        false,
        new[] { new StartTarget("CaptureService", 3) });

    public static readonly ServiceFix HyperV = new(
        "hyperv",
        "Hyper-V and WSL",
        "Restores the Hyper-V device services that a tuning tool disabled: the virtualization driver, the virtual drive enumerator, the composite and USB bus enumerators, the remote desktop bus and the WSL service. Needed before Hyper-V, WSL2, Windows Sandbox or Android emulators can start.",
        true,
        new[]
        {
            new StartTarget("vid", 3),
            new StartTarget("vdrvroot", 0),
            new StartTarget("umbus", 3),
            new StartTarget("CompositeBus", 3),
            new StartTarget("rdpbus", 3),
            new StartTarget("WslService", 3),
        });

    public static IReadOnlyList<ServiceFix> All { get; } = new[] { ObsCapture, HyperV };

    public ServiceFixStatus Status(ServiceFix fix)
    {
        var installed = fix.Targets.Where(t => Read(t.ServiceName) is not null).ToArray();
        var missing = fix.Targets.Where(t => Read(t.ServiceName) is null).Select(t => t.ServiceName).ToArray();

        string current = string.Join(", ", installed.Select(t => $"{t.ServiceName} {Read(t.ServiceName)}"));
        if (current.Length == 0) current = "no matching service on this PC";

        bool applied = installed.Length > 0 && installed.All(t => Read(t.ServiceName) == t.Start);

        string summary = applied
            ? "Already set: " + current
            : "Current: " + current;

        if (missing.Length > 0)
            summary += " (not installed: " + string.Join(", ", missing) + ")";

        return new ServiceFixStatus(applied, summary);
    }

    public ServiceFixResult Apply(ServiceFix fix)
    {
        var changed = new List<string>();
        var skipped = new List<string>();
        var failed = new List<string>();

        foreach (var target in fix.Targets)
        {
            int? before = Read(target.ServiceName);
            if (before is null)
            {
                skipped.Add(target.ServiceName);
                continue;
            }

            if (before == target.Start) continue;

            if (!Write(target.ServiceName, target.Start))
            {
                failed.Add(target.ServiceName);
                Record(target.ServiceName, before, null, ChangeResults.Failed, "Access denied.");
                continue;
            }

            int? after = Read(target.ServiceName);
            Record(target.ServiceName, before, after,
                after == target.Start ? ChangeResults.Verified : ChangeResults.Failed, null);

            if (after == target.Start) changed.Add($"{target.ServiceName} {before} -> {target.Start}");
            else failed.Add(target.ServiceName);
        }

        if (failed.Count > 0)
            return new ServiceFixResult(false,
                "Could not write " + string.Join(", ", failed) + ". Run kaliteConfig as administrator.");

        var message = new StringBuilder();
        message.Append(changed.Count > 0
            ? fix.Title + ": " + string.Join(", ", changed) + "."
            : fix.Title + ": nothing to change, the values are already right.");
        if (skipped.Count > 0)
            message.Append(" Skipped " + string.Join(", ", skipped) + " because they are not installed on this PC.");
        if (fix.NeedsRestart && changed.Count > 0)
            message.Append(" Restart Windows to apply it.");

        return new ServiceFixResult(true, message.ToString());
    }

    private void Record(string serviceName, int? before, int? after, string result, string? detail)
    {
        if (!_journal) return;

        ChangeLedger.RecordRegistryChange(
            Subsystem,
            $"{serviceName} Start {before} -> {after?.ToString() ?? "?"}",
            $@"{_servicesRoot}\{serviceName}",
            "Start",
            before,
            after,
            result,
            detail);
    }

    private int? Read(string serviceName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey($@"{_servicesRoot}\{serviceName}", false);
            return key?.GetValue("Start") switch
            {
                int i => i,
                long l => (int)l,
                _ => null,
            };
        }
        catch { return null; }
    }

    private bool Write(string serviceName, int value)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey($@"{_servicesRoot}\{serviceName}", true);
            if (key is null) return false;
            key.SetValue("Start", value, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }
}
