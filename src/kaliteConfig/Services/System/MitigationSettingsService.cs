using System;
using Microsoft.Win32;

namespace kaliteConfig.Services;

/// <summary>
/// Spectre &amp; Meltdown CPU mitigation switch: FeatureSettingsOverride 0 with
/// FeatureSettingsOverrideMask 3 restores the mitigations, FeatureSettingsOverride 1
/// with the same mask disables them. Absent values mean the Windows default (on).
/// </summary>
public sealed class SpectreMeltdownService
{
    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    private const string OverrideName = "FeatureSettingsOverride";
    private const string MaskName = "FeatureSettingsOverrideMask";

    /// <summary>true = mitigations on (override 0), false = mitigations off (override 1), null = not set (Windows default: on).</summary>
    public bool? Read()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
            if (key?.GetValue(OverrideName) is not int overrideValue) return null;
            if (key.GetValue(MaskName) is not int) return null;
            return overrideValue switch
            {
                0 => true,
                1 => false,
                _ => (bool?)null,
            };
        }
        catch
        {
            return null;
        }
    }

    public string? Write(bool enable)
    {
        int overrideValue = enable ? 0 : 1;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
            if (key is null) return "The Memory Management key could not be opened. Run the app as administrator.";
            key.SetValue(OverrideName, overrideValue, RegistryValueKind.DWord);
            key.SetValue(MaskName, 3, RegistryValueKind.DWord);
            if (key.GetValue(OverrideName) is not int written) return "The value was written but could not be read back.";
            return written == overrideValue ? null : "The value was written but reading it back did not match.";
        }
        catch (Exception ex)
        {
            return "Could not write the value: " + ex.Message;
        }
    }
}

/// <summary>
/// Control Flow Guard switch via the kernel MitigationOptions value: 00010000… enables
/// CFG, 00020000… disables it. Absent value means the Windows default (CFG on).
/// </summary>
public sealed class ControlFlowGuardService
{
    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
    private const string ValueName = "MitigationOptions";

    private static readonly byte[] EnabledValue = Convert.FromHexString("00010000000000000000000000000000");
    private static readonly byte[] DisabledValue = Convert.FromHexString("00020000000000000000000000000000");

    /// <summary>true = CFG on (…01), false = CFG off (…02), null = not set or custom (Windows default: on).</summary>
    public bool? Read()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
            if (key?.GetValue(ValueName) is not byte[] value || value.Length != EnabledValue.Length) return null;
            if (value.AsSpan().SequenceEqual(EnabledValue.AsSpan())) return true;
            if (value.AsSpan().SequenceEqual(DisabledValue.AsSpan())) return false;
            return null;
        }
        catch
        {
            return null;
        }
    }

    public string? Write(bool enable)
    {
        byte[] data = (byte[])(enable ? EnabledValue.Clone() : DisabledValue.Clone());
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
            if (key is null) return "The kernel key could not be opened. Run the app as administrator.";
            key.SetValue(ValueName, data, RegistryValueKind.Binary);
            if (key.GetValue(ValueName) is not byte[] written) return "The value was written but could not be read back.";
            return written.AsSpan().SequenceEqual(data.AsSpan()) ? null : "The value was written but reading it back did not match.";
        }
        catch (Exception ex)
        {
            return "Could not write the value: " + ex.Message;
        }
    }
}
