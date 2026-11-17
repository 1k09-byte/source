using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace kaliteConfig.Services;

public sealed class UpdateCheckService
{
    public const string Owner = "1k09-byte";
    public const string RepoName = "kaliteConfigTOOLkit";
    public const string ReleasesApiUrl = $"https://api.github.com/repos/{Owner}/{RepoName}/releases/latest";
    private const string AssetNamePrefix = "kaliteConfig-Setup-";
    private const string LegacyAssetNamePrefix = "kaliteConfig-Consumer-Setup-";
    private const string MsixAssetNamePrefix = "kaliteConfig-";
    private const string InnoUninstallRegKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{422E40DD-B1A5-4157-907F-690182582441}_is1";
    private const string KaliteOsInstallPathRegKey = @"SOFTWARE\KaliteOS";

    private static readonly HttpClient _http = CreateClient();

    private static int _updateInProgress;

    public string? LastError { get; private set; }

    internal static bool TryBeginUpdate() =>
        System.Threading.Interlocked.CompareExchange(ref _updateInProgress, 1, 0) == 0;

    internal static void EndUpdate() =>
        System.Threading.Interlocked.Exchange(ref _updateInProgress, 0);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("kaliteConfig-Consumer-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public sealed record LatestRelease(string Version, string Notes, string DownloadUrl, long SizeBytes, bool IsMsix);

    internal sealed record SetupAsset(string Name, string DownloadUrl, long SizeBytes, bool IsMsix);

    private static string PendingMarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "kaliteConfig", "update-pending.json");

    internal static void ClearPendingUpdate()
    {
        try { if (File.Exists(PendingMarkerPath)) File.Delete(PendingMarkerPath); }
        catch { }
    }

    private static string RetryMarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "kaliteConfig", "update-retry.json");

    internal sealed record RetryAttempt(string Version, DateTimeOffset AttemptedAt);

    internal static bool HasAutoRetried(string version)
    {
        try
        {
            if (!File.Exists(RetryMarkerPath)) return false;
            var marker = JsonSerializer.Deserialize<RetryAttempt>(File.ReadAllText(RetryMarkerPath));
            return marker is not null
                && string.Equals(marker.Version.Trim(), version.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    internal static void MarkAutoRetried(string version)
    {
        try
        {
            var dir = Path.GetDirectoryName(RetryMarkerPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(RetryMarkerPath,
                JsonSerializer.Serialize(new RetryAttempt(version.Trim(), DateTimeOffset.Now)));
        }
        catch { }
    }

    internal static void ClearAutoRetry()
    {
        try { if (File.Exists(RetryMarkerPath)) File.Delete(RetryMarkerPath); }
        catch { }
    }

    internal static string? GetRegisteredInstallDirectory()
    {
        try
        {
            using var kalite = Registry.LocalMachine.OpenSubKey(KaliteOsInstallPathRegKey, writable: false);
            if (kalite?.GetValue("InstallPath") is string fromKalite && !string.IsNullOrWhiteSpace(fromKalite))
            {
                var trimmed = fromKalite.Trim().TrimEnd('\\', '/');
                if (Directory.Exists(trimmed)) return trimmed;
            }
        }
        catch { }

        try
        {
            using var uninstall = Registry.LocalMachine.OpenSubKey(InnoUninstallRegKey, writable: false);
            if (uninstall?.GetValue("InstallLocation") is string loc && !string.IsNullOrWhiteSpace(loc))
            {
                var trimmed = loc.Trim().TrimEnd('\\', '/');
                if (Directory.Exists(trimmed)) return trimmed;
            }
        }
        catch { }

        return null;
    }

    internal static string? GetInstalledExePath()
    {
        var dir = GetRegisteredInstallDirectory();
        if (dir is null) return null;
        var exe = Path.Combine(dir, "kaliteConfig.exe");
        return File.Exists(exe) ? exe : null;
    }

    internal static bool PathsEqual(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(a.Trim()),
                Path.GetFullPath(b.Trim()),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    internal static string? TryReadExeVersion(string exePath)
    {
        try
        {
            var fvi = FileVersionInfo.GetVersionInfo(exePath);
            return $"{fvi.FileMajorPart}.{fvi.FileMinorPart}.{fvi.FileBuildPart}.{fvi.FilePrivatePart}";
        }
        catch { return null; }
    }

    internal static bool ShouldHandOffToInstalledCopy(
        string? pendingVersion,
        string? runningVersion,
        string? installedExeVersion,
        string? runningExePath,
        string? installedExePath)
    {
        if (string.IsNullOrWhiteSpace(pendingVersion)
            || string.IsNullOrWhiteSpace(runningVersion)
            || string.IsNullOrWhiteSpace(installedExeVersion)
            || string.IsNullOrWhiteSpace(runningExePath)
            || string.IsNullOrWhiteSpace(installedExePath))
        {
            return false;
        }

        if (PathsEqual(runningExePath, installedExePath))
            return false;

        if (!IsNewer(pendingVersion, runningVersion))
            return false;

        if (string.Equals(pendingVersion, installedExeVersion, StringComparison.OrdinalIgnoreCase))
            return true;

        return !IsNewer(pendingVersion, installedExeVersion)
               && IsNewer(installedExeVersion, runningVersion);
    }

    internal static bool TryLaunchInstalledCopyAndExit()
    {
        var installedExe = GetInstalledExePath();
        if (installedExe is null) return false;

        var runningPath = Environment.ProcessPath;
        if (PathsEqual(runningPath, installedExe))
            return false;

        try
        {
            bool isAdmin;
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }

            var psi = new ProcessStartInfo
            {
                FileName = installedExe,
                UseShellExecute = true,
            };
            if (!isAdmin) psi.Verb = "runas";

            Process.Start(psi);
            ClearPendingUpdate();
            Environment.Exit(0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryLaunchInstalledCopyAndExitIfStaleSession()
    {
        var pending = ReadPendingUpdateVersion();
        var running = CurrentVersion;
        var installedExe = GetInstalledExePath();
        if (pending is null || running is null || installedExe is null) return false;

        var installedVer = TryReadExeVersion(installedExe);
        if (!ShouldHandOffToInstalledCopy(
                pending, running, installedVer, Environment.ProcessPath, installedExe))
        {
            return false;
        }

        return TryLaunchInstalledCopyAndExit();
    }

    internal static string BuildSilentInstallerArguments(string? installDirectory)
    {
        var args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS";
        if (!string.IsNullOrWhiteSpace(installDirectory))
        {
            var dir = installDirectory.Trim().TrimEnd('\\');
            args += $" /DIR=\"{dir}\"";
        }
        return args;
    }

    internal static void WritePendingUpdate(string version)
    {
        try
        {
            var dir = Path.GetDirectoryName(PendingMarkerPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(PendingMarkerPath,
                JsonSerializer.Serialize(new { version, at = DateTime.Now }));
        }
        catch { }
    }

    internal static string? ReadPendingUpdateVersion() => ReadPendingUpdate()?.Version;

    internal sealed record PendingUpdate(
        string Version,
        string? InstallerPath,
        bool Reported);

    internal static string SetupDownloadPath(string version, bool isMsix = false) => isMsix
        ? Path.Combine(Path.GetTempPath(), $"{MsixAssetNamePrefix}{version.Trim()}.msix")
        : Path.Combine(Path.GetTempPath(), $"{AssetNamePrefix}{version.Trim()}.exe");

    internal enum UpdateAttemptState
    {
        None,
        Applied,
        AppliedElsewhere,
        Failed,
    }

    internal static UpdateAttemptState ClassifyPendingAttempt(
        PendingUpdate? pending,
        string? runningVersion,
        string? installedExeVersion)
    {
        if (pending is null || string.IsNullOrWhiteSpace(pending.Version))
            return UpdateAttemptState.None;

        if (!string.IsNullOrWhiteSpace(runningVersion) && !IsNewer(pending.Version, runningVersion))
            return UpdateAttemptState.Applied;

        if (!string.IsNullOrWhiteSpace(installedExeVersion)
            && !IsNewer(pending.Version, installedExeVersion))
            return UpdateAttemptState.AppliedElsewhere;

        return UpdateAttemptState.Failed;
    }

    internal static void WritePendingUpdate(
        string version,
        string? installerPath = null,
        bool reported = false)
    {
        try
        {
            var dir = Path.GetDirectoryName(PendingMarkerPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(PendingMarkerPath,
                JsonSerializer.Serialize(new
                {
                    version,
                    at = DateTime.Now,
                    installer = installerPath,
                    reported,
                }));
        }
        catch { }
    }

    internal static PendingUpdate? ReadPendingUpdate()
    {
        try
        {
            if (!File.Exists(PendingMarkerPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(PendingMarkerPath));
            var root = doc.RootElement;
            var version = StringProperty(root, "version");
            if (string.IsNullOrWhiteSpace(version)) return null;
            return new PendingUpdate(
                version.Trim(),
                StringProperty(root, "installer"),
                root.TryGetProperty("reported", out var reported)
                    && reported.ValueKind == JsonValueKind.True);
        }
        catch { return null; }
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static void MarkPendingUpdateReported()
    {
        var pending = ReadPendingUpdate();
        if (pending is null) return;
        WritePendingUpdate(pending.Version, pending.InstallerPath, reported: true);
    }

    public static string? CurrentVersion
    {
        get
        {
            try
            {
                var v = Assembly.GetEntryAssembly()?.GetName().Version;
                return v is null ? null : $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            }
            catch { return null; }
        }
    }

    public static bool IsNewer(string? remoteTag, string? currentVersion)
    {
        if (string.IsNullOrWhiteSpace(remoteTag) || string.IsNullOrWhiteSpace(currentVersion))
            return false;

        string remote = remoteTag.TrimStart('v', 'V');
        int[] r = remote.Split('.').Select(s => int.TryParse(s, out int n) ? n : -1).ToArray();
        int[] c = currentVersion.Split('.').Select(s => int.TryParse(s, out int n) ? n : -1).ToArray();
        if (r.Any(n => n < 0) || c.Any(n => n < 0) || r.Length == 0 || c.Length == 0)
            return false;

        int len = Math.Max(r.Length, c.Length);
        for (int i = 0; i < len; i++)
        {
            int rv = i < r.Length ? r[i] : 0;
            int cv = i < c.Length ? c[i] : 0;
            if (rv != cv) return rv > cv;
        }
        return false;
    }

    public async Task<LatestRelease?> CheckAsync(CancellationToken ct = default)
    {
        LastError = null;
        string? last = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var release = await CheckOnceAsync(ct);
            if (release is not null)
            {
                LastError = null;
                return release;
            }

            if (LastError is null) return null;

            last = LastError;
            LastError = null;
            if (ct.IsCancellationRequested) break;
            if (attempt < 3) await Task.Delay(TimeSpan.FromMilliseconds(700 * attempt), ct);
        }

        LastError = last;
        return null;
    }

    private async Task<LatestRelease?> CheckOnceAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(ReleasesApiUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                LastError = response.StatusCode == System.Net.HttpStatusCode.Forbidden
                    ? "GitHub rate limit reached (HTTP 403). Try again in a few minutes."
                    : $"GitHub returned HTTP {(int)response.StatusCode}.";
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? "" : "";
            string notes = root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String
                ? (b.GetString() ?? "") : "";

            if (!IsNewer(tag, CurrentVersion))
            {
                return null;
            }

            SetupAsset? picked = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                picked = PickSetupAsset(assets, tag);

            if (picked is null)
            {
                return null;
            }

            return new LatestRelease(tag.TrimStart('v', 'V'), notes, picked.DownloadUrl, picked.SizeBytes, picked.IsMsix);
        }
        catch (Exception ex)
        {
            LastError = "Could not reach GitHub: " + ex.Message;
            return null;
        }
    }

    internal static SetupAsset? PickSetupAsset(JsonElement assetsArray, string tag)
    {
        string version = tag.TrimStart('v', 'V').Trim();
        if (version.Length == 0) return null;

        var exeMatching = new System.Collections.Generic.List<SetupAsset>();
        var msixMatching = new System.Collections.Generic.List<SetupAsset>();
        foreach (var asset in assetsArray.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString() ?? "" : "";
            if (!name.Contains(version, StringComparison.OrdinalIgnoreCase))
                continue;

            string? url = asset.TryGetProperty("browser_download_url", out var u) && u.ValueKind == JsonValueKind.String
                ? u.GetString() : null;
            if (string.IsNullOrEmpty(url)) continue;
            long size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out long sz) ? sz : 0;

            bool isExe = (name.StartsWith(AssetNamePrefix, StringComparison.OrdinalIgnoreCase)
                 || name.StartsWith(LegacyAssetNamePrefix, StringComparison.OrdinalIgnoreCase))
                && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            bool isMsix = name.StartsWith(MsixAssetNamePrefix, StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase);
            if (isExe) exeMatching.Add(new SetupAsset(name, url!, size, false));
            else if (isMsix) msixMatching.Add(new SetupAsset(name, url!, size, true));
        }

        if (exeMatching.Count > 0)
            return exeMatching.FirstOrDefault(a => a.Name.StartsWith(AssetNamePrefix, StringComparison.OrdinalIgnoreCase))
                ?? exeMatching[0];
        if (msixMatching.Count > 0)
            return msixMatching[0];
        return null;
    }

    public async Task<string> DownloadAsync(LatestRelease release, IProgress<double>? progress, CancellationToken ct = default)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                return await DownloadOnceAsync(release, progress, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                last = ex;
                if (attempt < 3) await Task.Delay(TimeSpan.FromMilliseconds(800 * attempt), ct);
            }
        }

        throw last ?? new InvalidOperationException("The update could not be downloaded.");
    }

    private async Task<string> DownloadOnceAsync(LatestRelease release, IProgress<double>? progress, CancellationToken ct)
    {
        string tempPath = SetupDownloadPath(release.Version, release.IsMsix);
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("kaliteConfig-Consumer-Updater/1.0");

        using var response = await client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long total = release.SizeBytes > 0
            ? release.SizeBytes
            : response.Content.Headers.ContentLength ?? -1;

        await using var content = await response.Content.ReadAsStreamAsync(ct);
        await using var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);

        var buffer = new byte[1 << 16];
        long read = 0;
        var lastReport = DateTime.UtcNow;
        int n;
        while ((n = await content.ReadAsync(buffer, ct)) != 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (progress is not null && total > 0 && (DateTime.UtcNow - lastReport).TotalMilliseconds > 100)
            {
                progress.Report(read * 100.0 / total);
                lastReport = DateTime.UtcNow;
            }
        }
        if (release.SizeBytes > 0 && read != release.SizeBytes)
        {
            try { File.Delete(tempPath); } catch { }
            throw new IOException($"The download stopped at {read} of {release.SizeBytes} bytes.");
        }

        progress?.Report(100);
        return tempPath;
    }

}
