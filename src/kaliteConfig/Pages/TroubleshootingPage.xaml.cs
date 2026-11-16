using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;

namespace kaliteConfig.Pages
{
    public sealed partial class TroubleshootingPage : Page
    {
        private readonly Services.RecoveryService _recovery = new();
        private readonly Services.ServiceStartFixService _fixes = new();
        private readonly ObservableCollection<ChangeRow> _changes = new();

        public sealed class ChangeRow
        {
            public string Id { get; init; } = "";
            public string Title { get; init; } = "";
            public string Detail { get; init; } = "";
            public bool CanUndo { get; init; }
        }

        public TroubleshootingPage()
        {
            this.InitializeComponent();
            ChangesList.ItemsSource = _changes;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshSafeState();
            RefreshServiceFixes();
            RefreshChanges();
        }

        private void RefreshServiceFixes()
        {
            try { ObsCaptureState.Text = _fixes.Status(Services.ServiceStartFixService.ObsCapture).Summary; }
            catch { }

            try { HyperVState.Text = _fixes.Status(Services.ServiceStartFixService.HyperV).Summary; }
            catch { }
        }

        private void ApplyObsFix_Click(object sender, RoutedEventArgs e) =>
            ApplyFix(Services.ServiceStartFixService.ObsCapture);

        private void ApplyHyperVFix_Click(object sender, RoutedEventArgs e) =>
            ApplyFix(Services.ServiceStartFixService.HyperV);

        private void ApplyFix(Services.ServiceFix fix)
        {
            Services.ServiceFixResult? result = null;
            try { result = _fixes.Apply(fix); }
            catch (Exception ex) { Show(InfoBarSeverity.Error, "Something went wrong: " + ex.Message); }

            if (result is not null)
                Show(result.Ok ? InfoBarSeverity.Success : InfoBarSeverity.Error, result.Message);

            RefreshServiceFixes();
            RefreshChanges();
        }

        private void RefreshSafeState()
        {
            bool armed = false;
            try { armed = _recovery.IsSafeBootArmed(); } catch { }
            SafeStateText.Text = armed
                ? "Safe mode is currently armed: every restart uses safe mode."
                : "Safe mode is not armed: Windows starts normally.";
        }

        private void Firmware_Click(object sender, RoutedEventArgs e) =>
            Run(() => _recovery.RebootToFirmware());

        private void SafeOnce_Click(object sender, RoutedEventArgs e) =>
            Run(() => _recovery.RebootToSafeModeOnce());

        private void SafeAlways_Click(object sender, RoutedEventArgs e) =>
            Run(() => _recovery.RebootToSafeModeAlways());

        private void NormalStartup_Click(object sender, RoutedEventArgs e) =>
            Run(() => _recovery.RestoreNormalStartup());

        private void Run(Func<Services.RecoveryResult> work)
        {
            Show(InfoBarSeverity.Informational, "Working…");
            Task.Run(work).ContinueWith(t =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (t.IsFaulted || t.Result is null)
                        Show(InfoBarSeverity.Error, "Something went wrong: " + (t.Exception?.GetBaseException().Message ?? "unknown error."));
                    else
                        Show(t.Result.Ok ? InfoBarSeverity.Success : InfoBarSeverity.Error, t.Result.Message);
                    RefreshSafeState();
                });
            });
        }

        private void RefreshChanges_Click(object sender, RoutedEventArgs e) => RefreshChanges();

        private void RefreshChanges()
        {
            _changes.Clear();
            try
            {
                foreach (var record in Services.ChangeLog.ChangeLedger.Recent(60))
                {
                    _changes.Add(new ChangeRow
                    {
                        Id = record.Id,
                        Title = record.Summary,
                        Detail = string.Format(
                            CultureInfo.CurrentCulture,
                            "{0:g} | {1} | HKLM\\{2}\\{3} | {4} -> {5} | {6}",
                            record.AtUtc.ToLocalTime(),
                            record.Subsystem,
                            record.KeyPath,
                            record.ValueName,
                            record.BeforeValue?.ToString(CultureInfo.InvariantCulture) ?? "absent",
                            record.AfterValue?.ToString(CultureInfo.InvariantCulture) ?? "absent",
                            record.Result),
                        CanUndo = record.Result == Services.ChangeLog.ChangeResults.Verified,
                    });
                }
            }
            catch { }

            NoChangesText.Visibility = _changes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ChangesList.Visibility = _changes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string id) return;

            string? error = null;
            try { error = Services.ChangeLog.ChangeLedger.Revert(id); }
            catch (Exception ex) { error = ex.Message; }

            if (error is null) Show(InfoBarSeverity.Success, "Change reverted.");
            else Show(InfoBarSeverity.Warning, error);
            RefreshChanges();
        }

        private void UndoSession_Click(object sender, RoutedEventArgs e)
        {
            var result = Services.ChangeLog.ChangeLedger.RevertSession(Services.ChangeLog.ChangeLedger.CurrentSessionId);

            if (result.Reverted == 0 && result.Skipped == 0)
                Show(InfoBarSeverity.Informational, "This session has not recorded any change that can be undone.");
            else if (result.Skipped == 0)
                Show(InfoBarSeverity.Success, $"Reverted {result.Reverted} change(s) from this session.");
            else
                Show(InfoBarSeverity.Warning,
                    $"Reverted {result.Reverted}, skipped {result.Skipped}: " + string.Join(" ", result.Messages));

            RefreshChanges();
        }

        private void Show(InfoBarSeverity severity, string message)
        {
            StatusBar.Severity = severity;
            StatusBar.Message = message;
            StatusBar.IsOpen = true;
        }
    }
}
