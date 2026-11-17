using kaliteConfig.Native;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;

namespace kaliteConfig.Services;

public sealed class KalitePowerPlanService
{
    public enum Vendor { Unknown, Amd, Intel }

    private const string PlanDescription = "0 Dihlay + 999fps";

    private static readonly Guid ProcessorGroup = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid DiskGroup = new("0012ee47-9041-4b5d-9b77-535fba8b1442");
    private static readonly Guid PciExpressGroup = new("501a4d13-42af-4429-9fd1-a8218c268e20");
    private static readonly Guid UsbGroup = new("2a737441-1930-4402-8d77-b2bebba308a3");
    private static readonly Guid InterruptGroup = new("48672f38-7a9a-4bb2-8bf8-3d85be19de4e");
    private static readonly Guid IdleGroup = new("2e601130-5351-4d9d-8e04-252966bad054");
    private static readonly Guid DisplayGroup = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static readonly Guid SleepGroup = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");

    private readonly PowerService _power = new();

    private readonly record struct Write(Guid Subgroup, Guid Setting, uint Ac, uint? Dc);

    public sealed record Outcome(string PlanName, Vendor Vendor, bool CreatedPlan, int Applied, int Failed, IReadOnlyList<string> FailedSettings)
    {
        public string Summary
        {
            get
            {
                string cpu = Vendor == Vendor.Unknown ? "unrecognised CPU" : Vendor.ToString();
                string text = $"Power plan \"{PlanName}\" {(CreatedPlan ? "created" : "updated")} on the {cpu} "
                    + $"({Applied} settings applied";
                if (Failed > 0)
                {
                    string sample = FailedSettings.Count > 0
                        ? ": " + string.Join(", ", FailedSettings.Take(4))
                        : string.Empty;
                    text += $", {Failed} not available on this PC{sample}";
                }
                return text + "), now active.";
            }
        }
    }

    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public Outcome Apply()
    {
        if (!IsElevated())
        {
            throw new UnauthorizedAccessException(
                "Creating or switching a power plan needs administrator rights. " +
                "Close kaliteConfig, run it as administrator, and try again.");
        }

        string cpu = ReadCpuName();
        Vendor vendor = DetectVendor(cpu);
        var topology = TopologyService.Get();
        bool x3d = vendor == Vendor.Amd
            && cpu.Contains("X3D", StringComparison.OrdinalIgnoreCase)
            && topology.PhysicalCores >= 12;
        bool hybrid = topology.IsHybrid;
        string planName = vendor switch
        {
            Vendor.Amd => "KaliteAMD",
            Vendor.Intel => "KaliteINTEL",
            _ => "KalitePlan",
        };


        Guid scheme = FindOrCreatePlan(planName, out bool created);
        int applied = 0, failed = 0;
        var failedSettings = new List<string>();
        foreach (Write write in BuildWrites(vendor, x3d, hybrid))
        {
            var target = scheme;
            var subgroup = write.Subgroup;
            var setting = write.Setting;
            uint result = PowrProf.PowerWriteACValueIndex(IntPtr.Zero, ref target, ref subgroup, ref setting, write.Ac);
            if (write.Dc is uint dc)
                result |= PowrProf.PowerWriteDCValueIndex(IntPtr.Zero, ref target, ref subgroup, ref setting, dc);
            if (result == 0)
            {
                applied++;
            }
            else
            {
                failed++;
                string label = _power.GetSettingFriendlyName(write.Subgroup, write.Setting);
                if (string.IsNullOrWhiteSpace(label)) label = write.Setting.ToString();
                failedSettings.Add($"{label} ({result})");
            }
        }

        _power.SetActiveScheme(scheme);
        return new Outcome(planName, vendor, created, applied, failed, failedSettings);
    }

    private Guid FindOrCreatePlan(string planName, out bool created)
    {
        List<Guid> installed = _power.ListSchemeIds();
        foreach (Guid id in installed)
        {
            if (string.Equals(_power.GetSchemeFriendlyName(id), planName, StringComparison.OrdinalIgnoreCase))
            {
                created = false;
                return id;
            }
        }

        Guid baseScheme = installed.Contains(PowerService.HighPerformanceGuid)
            ? PowerService.HighPerformanceGuid
            : PowerService.BalancedGuid;
        Guid plan = _power.DuplicateScheme(baseScheme);
        _power.WritePlanName(plan, planName);
        _power.WritePlanDescription(plan, PlanDescription);

        string written = _power.GetSchemeFriendlyName(plan);
        if (!string.Equals(written, planName, StringComparison.OrdinalIgnoreCase))
        {
            try { _power.DeleteScheme(plan); } catch { }
            throw new InvalidOperationException(
                $"Windows would not name the new power plan (got \"{written}\"). " +
                "Run kaliteConfig as administrator and try again.");
        }

        created = true;
        return plan;
    }

    private static List<Write> BuildWrites(Vendor vendor, bool x3d, bool hybrid)
    {
        var writes = new List<Write>();
        void Add(Guid subgroup, string setting, uint ac, uint? dc = null)
            => writes.Add(new Write(subgroup, new Guid(setting), ac, dc));

        bool secondaryCpu = vendor == Vendor.Amd || hybrid;
        uint secondaryMin = vendor == Vendor.Amd ? 5u : 0u;
        uint heteroPolicy = hybrid ? 4u : 0u;
        uint heteroScheduling = hybrid ? 2u : 0u;

        Add(ProcessorGroup, "893dee8e-2bef-41e0-89c6-b55d0929964c", 5);
        Add(ProcessorGroup, "bc5038f7-23e0-4960-96da-33abaf5935ec", 100);
        Add(ProcessorGroup, "75b0ae3f-bce0-45a7-8c89-c9611c25e100", 0);
        Add(ProcessorGroup, "be337238-0d82-4146-a960-4f3749d470c7", 2);
        Add(ProcessorGroup, "45bcc044-d885-43e2-8605-ee0ec6e96b59", 100);
        Add(ProcessorGroup, "3b04d4fd-1cc7-4f23-ab1c-d1337819c4bb", 0);
        Add(ProcessorGroup, "5d76a2ca-e8c0-402f-a133-2158492d58ad", 0);
        Add(ProcessorGroup, "8baa4a8a-14c6-4451-8e8b-14bdbd197537", 1);
        Add(ProcessorGroup, "36687f9e-e3a5-4dbf-b1dc-15eb381c6863", 0);
        Add(ProcessorGroup, "cfeda3d0-7697-4566-a922-a9086cd49dfa", 30000);
        Add(ProcessorGroup, "4d2b0152-7d5c-498b-88e2-34345392a2c5", x3d ? 100u : 5000u);
        Add(ProcessorGroup, "4b92d758-5a24-4851-a470-815d78aee119", 100);
        Add(ProcessorGroup, "7b224883-b3cc-4d79-819f-8374152cbe7c", 100);
        Add(ProcessorGroup, "0cc5b647-c1df-4637-891a-dec35c318583", x3d ? 0u : 100u);
        Add(ProcessorGroup, "ea062031-0e34-4ff1-9b6d-eb1059334028", 100);
        Add(ProcessorGroup, "c7be0679-2817-4d69-9d02-519a537ed0c6", 2);
        Add(ProcessorGroup, "2ddd5a84-5a71-437e-912a-db0b8c788732", 1);
        Add(ProcessorGroup, "71021b41-c749-4d21-be74-a00f335d582b", 1);
        Add(ProcessorGroup, "dfd10d17-d5eb-45dd-877a-9a34ddd15c82", 1);
        Add(ProcessorGroup, "447235c7-6a8d-4cc0-8e24-9eaf70b96e2b", 2);

        if (secondaryCpu)
        {
            Add(ProcessorGroup, "893dee8e-2bef-41e0-89c6-b55d0929964d", secondaryMin);
            Add(ProcessorGroup, "bc5038f7-23e0-4960-96da-33abaf5935ed", 100);
            Add(ProcessorGroup, "75b0ae3f-bce0-45a7-8c89-c9611c25e101", 0);
            Add(ProcessorGroup, "36687f9e-e3a5-4dbf-b1dc-15eb381c6864", 0);
            Add(ProcessorGroup, "0cc5b647-c1df-4637-891a-dec35c318584", x3d ? 0u : 100u);
            Add(ProcessorGroup, "ea062031-0e34-4ff1-9b6d-eb1059334029", 100);
            Add(ProcessorGroup, "447235c7-6a8d-4cc0-8e24-9eaf70b96e2c", 2);
        }

        if (vendor == Vendor.Amd || hybrid)
        {
            Add(ProcessorGroup, "7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5", heteroPolicy);
            Add(ProcessorGroup, "93b8b6dc-0698-4d1c-9ee4-0644e900c85d", heteroScheduling);
            Add(ProcessorGroup, "bae08b81-2d5e-4688-ad6a-13243356654b", heteroScheduling);
        }

        if (!x3d)
        {
            Add(ProcessorGroup, "616cdaa5-695e-4545-97ad-97dc2d1bdd88", 100);
            Add(ProcessorGroup, "616cdaa5-695e-4545-97ad-97dc2d1bdd89", 100);
        }
        Add(ProcessorGroup, "619b7505-003b-4e82-b7a6-4dd29c300971", 100);
        Add(ProcessorGroup, "619b7505-003b-4e82-b7a6-4dd29c300972", 100);
        Add(ProcessorGroup, "1facfc65-a930-4bc5-9f38-504ec097bbc0", 100);
        Add(ProcessorGroup, "94d3a615-a899-4ac5-ae2b-e4d8f634367f", 1);

        Add(InterruptGroup, "2bfc24f9-5ea2-4801-8213-3dbae01aa39d", 3);
        Add(PciExpressGroup, "ee12f906-d277-404b-b6da-e5fa1a576df5", 0);
        Add(UsbGroup, "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0);
        Add(UsbGroup, "d4e98f31-5ffe-4ce1-be31-1b38b384c009", 0);
        Add(UsbGroup, "0853a681-27c8-4100-a2fd-82013e970683", 0);
        Add(DiskGroup, "fc7372b6-ab2d-43ee-8797-15e9841f2cca", 0);
        Add(DiskGroup, "d639518a-e56d-4345-8af2-b9f32fb26109", 0);
        Add(DiskGroup, "fc95af4d-40e7-4b6d-835a-56d131dbc80e", 0);
        Add(DiskGroup, "d3d55efd-c1ff-424e-9dc3-441be7833010", 0);
        Add(DiskGroup, "dbc9e238-6de9-49e3-92cd-8c2b4946b472", 0);
        Add(DiskGroup, "0b2d69d7-a2a1-449c-9680-f91c70521c60", 0);
        Add(DiskGroup, "dab60367-53fe-4fbc-825e-521d069d2456", 0);
        Add(DiskGroup, "80e3c60e-bb94-4ad8-bbe0-0d3195efc663", 0);
        Add(DiskGroup, "51dea550-bb38-4bc4-991b-eacf37be5ec8", 100);
        Add(DiskGroup, "6738e2c4-e8a5-4a42-b16a-e040e769756e", 0);
        Add(IdleGroup, "d502f7ee-1dc7-4efd-a55d-f04b6f5c0545", 0);

        Add(DisplayGroup, "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e", 0);
        Add(DisplayGroup, "17aaa29b-8b43-4b94-aafe-35f64daaf1ee", 0);
        Add(DisplayGroup, "90959d22-d6a1-49b9-af93-bce885ad335b", 0);
        Add(DisplayGroup, "fbd9aa66-9553-4097-ba44-ed6e9d65eab8", 0);
        Add(SleepGroup, "29f6c1db-86da-48c5-9fdb-f2b67b1f44da", 0);
        Add(SleepGroup, "9d7815a6-7ee4-497e-8888-515a05f02364", 0);
        Add(SleepGroup, "bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d", 0);

        return writes;
    }

    private static Vendor DetectVendor(string cpu)
    {
        foreach (string token in new[] { "AMD", "Ryzen", "Athlon", "EPYC", "Threadripper" })
            if (cpu.Contains(token, StringComparison.OrdinalIgnoreCase)) return Vendor.Amd;
        foreach (string token in new[] { "Intel", "Xeon", "Pentium", "Celeron" })
            if (cpu.Contains(token, StringComparison.OrdinalIgnoreCase)) return Vendor.Intel;
        if (cpu.Contains("Core", StringComparison.OrdinalIgnoreCase)) return Vendor.Intel;
        return Vendor.Unknown;
    }

    private static string ReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", false);
            if (key?.GetValue("ProcessorNameString") is string name && name.Trim().Length > 0)
                return name.Trim();
        }
        catch { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", false);
            if (key?.GetValue("Identifier") is string identifier && identifier.Trim().Length > 0)
                return identifier.Trim();
        }
        catch { }

        try
        {
            string? identifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
            if (!string.IsNullOrWhiteSpace(identifier)) return identifier.Trim();
        }
        catch { }

        return string.Empty;
    }
}
