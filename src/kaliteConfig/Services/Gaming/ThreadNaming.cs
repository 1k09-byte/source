using System;
using System.IO;

namespace kaliteConfig.Services;

public static class ThreadNaming
{
    private static readonly (string Module, string Label)[] KnownModuleThreads =
    {
        ("windows.Gaming.Input.dll", "GameInput input thread"),
        ("gameinput.dll", "GameInput service thread"),
    };

    public const string Unnamed = "(unnamed)";

    public static string? NameFromStartAddress(string? startAddress)
    {
        string text = (startAddress ?? string.Empty).Trim();
        int cut = text.IndexOf('+');
        string module = cut > 0 ? text[..cut] : text;
        if (module.Length == 0) return null;

        string file = module;
        try { file = Path.GetFileName(module); } catch { }

        foreach (var (known, label) in KnownModuleThreads)
        {
            if (string.Equals(file, known, StringComparison.OrdinalIgnoreCase))
                return $"{label} ({known})";
        }
        return null;
    }

    public static string Describe(string? windowsName, string? startAddress)
    {
        string text = (windowsName ?? string.Empty).Trim();
        if (text.Length > 0 && !string.Equals(text, Unnamed, StringComparison.OrdinalIgnoreCase))
            return text;
        return NameFromStartAddress(startAddress) ?? Unnamed;
    }
}
