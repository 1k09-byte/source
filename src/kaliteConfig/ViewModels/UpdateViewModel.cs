using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using kaliteConfig.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace kaliteConfig.ViewModels;

public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateCheckService _service = new();
    private CancellationTokenSource? _cts;
    private UpdateCheckService.LatestRelease? _pending;

    [ObservableProperty]
    public partial bool IsAvailable { get; set; }
    [ObservableProperty]
    public partial string LatestVersion { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;
    [ObservableProperty]
    public partial bool IsBusy { get; set; }
    [ObservableProperty]
    public partial double? DownloadPercent { get; set; }
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    public Microsoft.UI.Xaml.Visibility BannerVisibility => IsAvailable
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility ProgressVisibility => IsBusy && DownloadPercent is not null
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string BannerTitle => IsAvailable ? $"kaliteConfig {LatestVersion} available" : string.Empty;

    public bool CanUpdateNow => IsAvailable && !IsBusy;

    [ObservableProperty]
    public partial bool PreferHandOffToInstalledCopy { get; set; }

    [ObservableProperty]
    public partial bool LastAttemptFailed { get; set; }

    [ObservableProperty]
    public partial bool SuppressStartupOffer { get; set; }

    public string? PendingInstallerPath { get; private set; }

    partial void OnPreferHandOffToInstalledCopyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUpdateNow));
        OnPropertyChanged(nameof(PrimaryUpdateActionText));
    }

    partial void OnLastAttemptFailedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanOpenInstaller));
        OnPropertyChanged(nameof(PrimaryUpdateActionText));
    }

    public bool CanOpenInstaller =>
        LastAttemptFailed
        && !string.IsNullOrWhiteSpace(PendingInstallerPath)
        && File.Exists(PendingInstallerPath);

    public string PrimaryUpdateActionText =>
        PreferHandOffToInstalledCopy ? "Open updated copy"
        : CanOpenInstaller ? "Open installer"
        : "Update now";

    partial void OnIsAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(BannerVisibility));
        OnPropertyChanged(nameof(BannerTitle));
        OnPropertyChanged(nameof(CanUpdateNow));
    }
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(ProgressVisibility));
        OnPropertyChanged(nameof(CanUpdateNow));
    }
    partial void OnDownloadPercentChanged(double? value) => OnPropertyChanged(nameof(ProgressVisibility));

    [RelayCommand]
    public async Task CheckForUpdateAsync()
    {
        if (IsBusy) return;
        if (StartupService.IsPackaged)
        {
            StatusText = "Updates arrive automatically with the MSIX package.";
            return;
        }
        try
        {
            var pending = UpdateCheckService.ReadPendingUpdate();
            var running = UpdateCheckService.CurrentVersion;
            var runningPath = Environment.ProcessPath;
            var installedExe = UpdateCheckService.GetInstalledExePath();
            var installedVer = installedExe is not null
                ? UpdateCheckService.TryReadExeVersion(installedExe)
                : null;

            var attempt = UpdateCheckService.ClassifyPendingAttempt(pending, running, installedVer);
            if (attempt == UpdateCheckService.UpdateAttemptState.Applied)
            {
                UpdateCheckService.ClearPendingUpdate();
                UpdateCheckService.ClearAutoRetry();
                pending = null;
                attempt = UpdateCheckService.UpdateAttemptState.None;
            }

            var release = await _service.CheckAsync();
            if (release is null)
            {
                if (!string.IsNullOrWhiteSpace(_service.LastError))
                    StatusText = "Update check failed: " + _service.LastError;
                return;
            }

            _pending = release;
            LatestVersion = release.Version;
            Notes = string.IsNullOrWhiteSpace(release.Notes)
                ? "No release notes provided."
                : release.Notes;

            var sameAttempt = pending is not null
                && string.Equals(pending.Version, release.Version, StringComparison.OrdinalIgnoreCase);

            var failed = attempt == UpdateCheckService.UpdateAttemptState.Failed && sameAttempt;
            bool autoRetried = failed && pending is not null && UpdateCheckService.HasAutoRetried(pending.Version);
            PendingInstallerPath = failed ? pending!.InstallerPath : null;
            LastAttemptFailed = failed;
            SuppressStartupOffer = failed && pending!.Reported;

            if (failed && PendingInstallerPath is not null)
            {
                bool isMsixCache = PendingInstallerPath.EndsWith(".msix", StringComparison.OrdinalIgnoreCase);
                if (!isMsixCache)
                {
                    var cachedVersion = UpdateCheckService.TryReadExeVersion(PendingInstallerPath);
                    var cachedMatches = string.Equals(cachedVersion, pending!.Version, StringComparison.OrdinalIgnoreCase);
                    if (!cachedMatches)
                    {
                        try { File.Delete(PendingInstallerPath); } catch { }
                        PendingInstallerPath = null;
                    }
                }
            }

            PreferHandOffToInstalledCopy =
                attempt == UpdateCheckService.UpdateAttemptState.AppliedElsewhere
                && sameAttempt
                && installedExe is not null;

            if (failed)
            {
                Notes += $"\n\nThe update to {pending!.Version} did not complete - this copy still reports " +
                         $"v{running ?? "the previous version"}.";
                Notes += CanOpenInstaller
                    ? "\nChoose Open installer to run it again with its own window, so any error stays on screen."
                    : "\nChoose Update now to download a fresh copy and try again.";
            }
            else if (PreferHandOffToInstalledCopy && installedExe is not null)
            {
                Notes += $"\n\nThe update is already installed at:\n{installedExe}\n" +
                         $"This session is still running v{running ?? "?"} from:\n{runningPath ?? "?"}\n\n" +
                         "Choose Open updated copy to switch (or pin the Start Menu shortcut).";
            }

            IsAvailable = true;
            OnPropertyChanged(nameof(CanOpenInstaller));
            OnPropertyChanged(nameof(PrimaryUpdateActionText));
            OnPropertyChanged(nameof(BannerTitle));

            if (failed && !autoRetried)
            {
                Notes += CanOpenInstaller
                    ? "\nRetrying once automatically by opening the installer again..."
                    : "\nRetrying once automatically with the same download method...";
                _ = RetryFailedUpdateOnceAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = "Update check failed: " + ex.Message;
        }
    }

    [RelayCommand]
    public void HandOffToInstalledCopy()
    {
        if (IsBusy) return;
        if (!UpdateCheckService.TryLaunchInstalledCopyAndExit())
            StatusText = "Could not open the installed copy. Use the Start Menu kaliteConfig shortcut.";
    }

    [RelayCommand]
    public async Task UpdateNowAsync()
    {
        if (IsBusy || _pending is null) return;
        if (PreferHandOffToInstalledCopy)
        {
            HandOffToInstalledCopy();
            return;
        }
        if (CanOpenInstaller)
        {
            if (_pending.IsMsix && PendingInstallerPath!.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
            {
                OpenInstallerVisibly(PendingInstallerPath!);
                return;
            }
            var cachedVersion = UpdateCheckService.TryReadExeVersion(PendingInstallerPath!);
            if (string.Equals(cachedVersion, _pending.Version, StringComparison.OrdinalIgnoreCase))
            {
                OpenInstallerVisibly(PendingInstallerPath!);
                return;
            }
            try { File.Delete(PendingInstallerPath!); } catch { }
            PendingInstallerPath = null;
            LastAttemptFailed = false;
            OnPropertyChanged(nameof(CanOpenInstaller));
            OnPropertyChanged(nameof(PrimaryUpdateActionText));
        }

        if (!UpdateCheckService.TryBeginUpdate())
        {
            StatusText = "Another update is already running.";
            return;
        }

        IsBusy = true;
        DownloadPercent = null;
        StatusText = "Downloading update…";
        _cts = new CancellationTokenSource();
        string? lastInstallerPath = null;
        try
        {
            var progress = new Progress<double>(p => DownloadPercent = p);
            string installerPath = await _service.DownloadAsync(_pending, progress, _cts.Token);
            lastInstallerPath = installerPath;

            if (_pending.IsMsix)
            {
                StatusText = "Opening MSIX installer…";
                UpdateCheckService.WritePendingUpdate(_pending.Version, installerPath);
                try
                {
                    if (App.MainWindow is MainWindow mainWindow)
                        mainWindow.PrepareForUpdateShutdown();
                }
                catch { }

                var package = Process.Start(new ProcessStartInfo { FileName = installerPath, UseShellExecute = true });
                if (package is null)
                {
                    StatusText = "Windows could not open the MSIX package. Install it from the release page.";
                    return;
                }

                Environment.Exit(0);
                return;
            }

            var downloadedVersion = UpdateCheckService.TryReadExeVersion(installerPath);
            if (!string.Equals(downloadedVersion, _pending.Version, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(installerPath); } catch { }
                StatusText = $"The downloaded installer was built as {downloadedVersion ?? "an unknown version"} instead of {_pending.Version}. Nothing was installed; download the release again.";
                return;
            }

            StatusText = "Launching installer…";
            UpdateCheckService.WritePendingUpdate(_pending.Version, installerPath);
            var installDir = UpdateCheckService.GetRegisteredInstallDirectory();
            var installArgs = UpdateCheckService.BuildSilentInstallerArguments(installDir);
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = installArgs,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            try
            {
                if (App.MainWindow is MainWindow mainWindow)
                    mainWindow.PrepareForUpdateShutdown();
            }
            catch { }

            using (var setup = Process.Start(psi))
            {
                if (setup is null)
                    throw new InvalidOperationException("Windows did not start the installer.");

                if (setup.WaitForExit(2500) && setup.ExitCode != 0)
                {
                    LastAttemptFailed = true;
                    PendingInstallerPath = installerPath;
                    OnPropertyChanged(nameof(CanOpenInstaller));
                    OnPropertyChanged(nameof(PrimaryUpdateActionText));

                    if (TryRetryOnceWithCachedInstaller(installerPath, $"setup exit code {setup.ExitCode}"))
                        return;

                    UpdateCheckService.ClearPendingUpdate();
                    StatusText = $"Setup exited with code {setup.ExitCode}. " +
                                 "The update did not install; run the release installer manually.";
                    return;
                }
            }

            Environment.Exit(0);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Update cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Update failed: {ex.Message}";
            if (TryRetryOnceWithCachedInstaller(lastInstallerPath, "download or launch failure")) return;
        }
        finally
        {
            IsBusy = false;
            DownloadPercent = null;
            _cts?.Dispose();
            _cts = null;
            UpdateCheckService.EndUpdate();
        }
    }

    private bool TryRetryOnceWithCachedInstaller(string? installerPath, string reason)
    {
        if (_pending is null) return false;
        if (UpdateCheckService.HasAutoRetried(_pending.Version)) return false;
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath)) return false;

        bool versionOk = _pending.IsMsix
            || string.Equals(UpdateCheckService.TryReadExeVersion(installerPath), _pending.Version, StringComparison.OrdinalIgnoreCase);
        if (!versionOk) return false;

        UpdateCheckService.MarkAutoRetried(_pending.Version);
        StatusText = "Update failed; retrying once by opening the installer…";
        OpenInstallerVisibly(installerPath);
        return true;
    }

    private async Task RetryFailedUpdateOnceAsync()
    {
        if (_pending is null || IsBusy) return;
        if (UpdateCheckService.HasAutoRetried(_pending.Version)) return;

        if (CanOpenInstaller && PendingInstallerPath is not null)
        {
            UpdateCheckService.MarkAutoRetried(_pending.Version);
            OpenInstallerVisibly(PendingInstallerPath);
            return;
        }

        UpdateCheckService.MarkAutoRetried(_pending.Version);
        await UpdateNowAsync();
    }

    private void OpenInstallerVisibly(string installerPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
            };
            try
            {
                if (App.MainWindow is MainWindow mainWindow)
                    mainWindow.PrepareForUpdateShutdown();
            }
            catch { }

            Process.Start(psi);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            StatusText = $"Could not start the installer: {ex.Message}";
        }
    }
}
