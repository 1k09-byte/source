using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace kaliteConfig.Input;

public interface IVendorSoftwareLauncher
{
    bool CanOpen(string name);
    bool TryOpen(string name);
}

public sealed class VendorSoftwareLauncher : IVendorSoftwareLauncher
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public bool CanOpen(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return Find(name) is not null;
    }

    public bool TryOpen(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string? path = Find(name);
        if (path is null) return false;
        try
        {
            var start = new ProcessStartInfo(path) { UseShellExecute = true };
            Process.Start(start);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string? Find(string name)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(name, out string? cached)) return cached;
            string? found = FindRunning(name) ?? FindShortcut(name) ?? FindKnownPath(name);
            _cache[name] = found;
            return found;
        }
    }

    private static string? FindRunning(string name)
    {
        foreach (string processName in ProcessNames(name))
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        string? path = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
                    }
                    catch { }
                    finally { try { process.Dispose(); } catch { } }
                }
            }
            catch { }
        }
        return null;
    }

    private static string? FindShortcut(string name)
    {
        string token = ShortcutToken(name);
        if (token.Length == 0) return null;
        string product = name.Replace(token, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        var loose = new List<string>();
        foreach (string root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        })
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
            try
            {
                foreach (string file in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    string leaf = Path.GetFileNameWithoutExtension(file);
                    if (leaf.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (product.Length > 0 && leaf.IndexOf(product, StringComparison.OrdinalIgnoreCase) >= 0) return file;
                    loose.Add(file);
                }
            }
            catch { }
        }
        return loose.Count > 0 ? loose[0] : null;
    }

    private static string? FindKnownPath(string name)
    {
        foreach (string candidate in KnownPaths(name))
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static IEnumerable<string> KnownPaths(string name)
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        switch (name)
        {
            case "Logitech G Hub":
                yield return Path.Combine(programFiles, "LGHUB", "lghub.exe");
                break;
            case "Razer Synapse":
                yield return Path.Combine(programFiles, "Razer", "Synapse3", "WPFUI", "Razer Synapse 3.exe");
                yield return Path.Combine(programFiles, "Razer", "Synapse3", "WPFUI", "RazerSynapse3.exe");
                yield return Path.Combine(programFiles, "Razer", "RazerCentral", "RazerCentral.exe");
                break;
            case "SteelSeries GG":
                yield return Path.Combine(programFiles, "SteelSeries", "GG", "SteelSeriesGG.exe");
                break;
            case "Corsair iCUE":
                yield return Path.Combine(programFiles, "Corsair", "CORSAIR iCUE 5 Software", "iCUE.exe");
                yield return Path.Combine(programFiles, "Corsair", "CORSAIR iCUE 4 Software", "iCUE.exe");
                break;
            case "ROCCAT Swarm":
                yield return Path.Combine(programFilesX86, "ROCCAT", "ROCCAT Swarm", "ROCCAT_Swarm.exe");
                break;
            case "Glorious Core":
                yield return Path.Combine(programFiles, "Glorious", "Glorious Core", "GloriousCore.exe");
                break;
            case "ASUS Armoury Crate":
                yield return Path.Combine(programFiles, "ASUS", "Armoury Crate", "ArmouryCrate.exe");
                break;
        }
    }

    private static IEnumerable<string> ProcessNames(string name)
    {
        switch (name)
        {
            case "Logitech G Hub":
                yield return "lghub";
                yield return "lghub_agent";
                break;
            case "Razer Synapse":
                yield return "Razer Synapse";
                yield return "RazerSynapse3";
                yield return "RazerCentral";
                break;
            case "SteelSeries GG":
                yield return "SteelSeriesGG";
                break;
            case "Corsair iCUE":
                yield return "iCUE";
                break;
            case "ROCCAT Swarm":
                yield return "ROCCAT_Swarm";
                break;
            case "HyperX NGENUITY":
                yield return "NGENUITY";
                break;
            case "Glorious Core":
                yield return "GloriousCore";
                break;
            case "ASUS Armoury Crate":
                yield return "ArmouryCrate";
                yield return "ArmouryCrate.UserSessionHelper";
                break;
            case "MSI Center":
                yield return "MSI Center";
                break;
        }
    }

    private static string ShortcutToken(string name) => name switch
    {
        "Logitech G Hub" => "Logitech",
        "Razer Synapse" => "Razer",
        "SteelSeries GG" => "SteelSeries",
        "Corsair iCUE" => "Corsair",
        "ROCCAT Swarm" => "ROCCAT",
        "HyperX NGENUITY" => "HyperX",
        "Glorious Core" => "Glorious",
        "ASUS Armoury Crate" => "Armoury",
        "MSI Center" => "MSI Center",
        _ => string.Empty,
    };
}
