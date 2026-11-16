using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace kaliteConfig.Services;

public static class ExplorerThemeInstaller
{
    public const string ThemeFolderName = "KaliteOS Explorer Theme";
    public const string ThemeZipFileName = ThemeFolderName + ".zip";
    public const string ThemeIniFileName = "theme.ini";
    public const string ResourceFolderName = "Windhawk Resources";

    public static string TargetDirectory =>
        Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", ThemeFolderName);

    public static string? BundledZipPath
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(dir)) return null;

            foreach (var candidate in new[]
            {
                Path.Combine(dir, "Assets", "Windhawk", ThemeZipFileName),
                Path.Combine(dir, "Assets", ThemeZipFileName),
            })
            {
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }

    private static bool IsUpToDate(string zip, string target)
    {
        try
        {
            if (!Directory.Exists(Path.Combine(target, ResourceFolderName))) return false;
            string ini = Path.Combine(target, ThemeIniFileName);
            if (!File.Exists(ini)) return false;
            return File.GetLastWriteTimeUtc(ini) >= File.GetLastWriteTimeUtc(zip);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<string> InstallAsync(IProgress<string>? status = null, CancellationToken ct = default)
    {
        string? zip = BundledZipPath;
        if (zip is null)
            return "Explorer theme skipped: " + ThemeZipFileName + " is missing from the install.";

        string target = TargetDirectory;
        string root = Path.GetPathRoot(target) ?? @"C:\";

        if (IsUpToDate(zip, target))
        {
            status?.Report($"KaliteOS Explorer theme already in place at {target}.");
            return $"KaliteOS Explorer theme already in place at {target}.";
        }

        status?.Report($"Extracting the KaliteOS Explorer theme to {target}...");

        try
        {
            int files = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                Directory.CreateDirectory(target);

                int written = 0;
                using var archive = ZipFile.OpenRead(zip);
                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    string destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    if (!destination.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                        continue;

                    string? folder = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                    entry.ExtractToFile(destination, overwrite: true);
                    written++;
                }
                return written;
            }, ct);

            if (!Directory.Exists(Path.Combine(target, ResourceFolderName)))
                return $"Explorer theme extracted to {target}, but its \"{ResourceFolderName}\" folder is missing from the archive.";

            string iniPath = Path.Combine(target, ThemeIniFileName);
            try { if (File.Exists(iniPath)) File.SetLastWriteTimeUtc(iniPath, DateTime.UtcNow); } catch { }

            string ini = File.Exists(iniPath) ? ThemeIniFileName + " present" : ThemeIniFileName + " missing";
            status?.Report($"KaliteOS Explorer theme installed to {target}.");
            return $"KaliteOS Explorer theme installed to {target} ({files} file(s), {ini}).";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return "Explorer theme not installed: " + ex.Message;
        }
    }
}
