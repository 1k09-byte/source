using System;
using Microsoft.Win32;

namespace kaliteConfig.Services;

public sealed class SystemResponsivenessService
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string ValueName = "SystemResponsiveness";

    public uint? Read()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) switch
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

    public string? Write(uint value)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
            if (key is null) return "The SystemProfile key could not be opened. Run the app as administrator.";
            key.SetValue(ValueName, (int)value, RegistryValueKind.DWord);
            if (key.GetValue(ValueName) is not int written) return "The value was written but could not be read back.";
            return (uint)written == value ? null : "The value was written but reading it back did not match.";
        }
        catch (Exception ex)
        {
            return "Could not write the value: " + ex.Message;
        }
    }
}
