using System;
using System.Linq;
using Microsoft.Win32;

namespace kaliteConfig.Services;

public enum PowerKernelMode
{
    Balanced,
    Aggressive,
    Custom,
}

public sealed class PowerKernelModeService
{
    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Power";
    private const string KernelKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";

    private readonly record struct Flag(string Key, string Name, uint Value);

    private static readonly Flag[] AggressiveFlags =
    {
        new(PowerKey, "EventProcessorEnabled", 0),
        new(PowerKey, "AlwaysComputeQosHints", 1),
        new(PowerKey, "IpiLastClockOwnerDisable", 1),
        new(PowerKey, "AllowSystemRequiredPowerRequests", 0),
        new(PowerKey, "DisableDisplayBurstOnPowerSourceChange", 1),
        new(PowerKey, "PoFxSystemIrpWaitForReportDevicePowered", 0),
        new(PowerKey, "AllowAudioToEnableExecutionRequiredPowerRequests", 0),
        new(KernelKey, "ForceParkingRequested", 0),
    };

    private static readonly Flag[] AggressiveOnlyFlags = AggressiveFlags[1..];

    public PowerKernelMode Read()
    {
        if (AggressiveFlags.All(f => ReadFlag(f) == f.Value)) return PowerKernelMode.Aggressive;
        if (AggressiveOnlyFlags.All(f => ReadFlag(f) is null)) return PowerKernelMode.Balanced;
        return PowerKernelMode.Custom;
    }

    public string? Apply(PowerKernelMode mode)
    {
        if (mode == PowerKernelMode.Custom) return "Pick Balanced or Aggressive.";

        bool aggressive = mode == PowerKernelMode.Aggressive;
        var flags = aggressive ? AggressiveFlags : AggressiveOnlyFlags;

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            foreach (var flag in flags)
            {
                using var key = baseKey.CreateSubKey(flag.Key, true);
                if (key is null)
                    return $"HKLM\\{flag.Key} could not be opened. Run the app as administrator.";

                if (aggressive)
                    key.SetValue(flag.Name, (int)flag.Value, RegistryValueKind.DWord);
                else
                    key.DeleteValue(flag.Name, throwOnMissingValue: false);
            }
        }
        catch (UnauthorizedAccessException)
        {
            return "Administrator rights are required to change these power and kernel flags. " +
                   "Restart kaliteConfig as administrator and try again.";
        }
        catch (Exception ex)
        {
            return "Could not write the values: " + ex.Message;
        }

        bool verified = aggressive
            ? AggressiveFlags.All(f => ReadFlag(f) == f.Value)
            : AggressiveOnlyFlags.All(f => ReadFlag(f) is null);
        return verified ? null : "The values were written but reading them back did not match.";
    }

    private static uint? ReadFlag(Flag flag)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(flag.Key, false);
            return key?.GetValue(flag.Name) switch
            {
                int v => (uint)v,
                long v => (uint)v,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }
}
