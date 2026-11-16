using System;
using Microsoft.Win32;

namespace kaliteConfig.Services;

public sealed class KernelDmaService
{
    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string ValueName = "IOMMUFlags";

    public bool? Read()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
            if (key?.GetValue(ValueName) is not int value) return null;
            return value != 0;
        }
        catch
        {
            return null;
        }
    }

    public string? Write(bool enabled)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
            if (key is null) return "The GraphicsDrivers key could not be opened. Run the app as administrator.";
            key.SetValue(ValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
            if (key.GetValue(ValueName) is not int written) return "The value was written but could not be read back.";
            bool actual = written != 0;
            return actual == enabled ? null : "The value was written but reading it back did not match.";
        }
        catch (Exception ex)
        {
            return "Could not write the value: " + ex.Message;
        }
    }
}
