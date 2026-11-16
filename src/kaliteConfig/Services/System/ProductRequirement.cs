using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace kaliteConfig.Services;

internal static class ProductRequirement
{
    private const string EditionValue = "KaliteOS11";
    private const string OrganizationValue = "KaliteOS";

    private const string VersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string OemKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation";

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;
    private const uint MB_SETFOREGROUND = 0x00010000;
    private const uint MB_TOPMOST = 0x00040000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    internal static bool IsSatisfied() =>
        Matches(Read(VersionKey, "EditionSubVersion"), EditionValue)
        && Matches(Read(VersionKey, "RegisteredOrganization"), OrganizationValue)
        && Matches(Read(OemKey, "Manufacturer"), OrganizationValue)
        && Matches(Read(OemKey, "Model"), EditionValue);

    internal static void RefuseToRun()
    {
        string message =
            "You are not on KaliteOS." + Environment.NewLine + Environment.NewLine +
            "kaliteConfig only runs on KaliteOS, so it will not open here.";

        try { MessageBoxW(IntPtr.Zero, message, "kaliteConfig", MB_OK | MB_ICONERROR | MB_SETFOREGROUND | MB_TOPMOST); }
        catch { }
    }

    private static bool Matches(string value, string expected) =>
        string.Equals((value ?? string.Empty).Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static string Read(string subKey, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName)?.ToString()?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
