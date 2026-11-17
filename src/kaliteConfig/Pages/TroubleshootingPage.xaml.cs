using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace kaliteConfig.Pages
{
    public sealed partial class TroubleshootingPage : Page
    {
        private readonly Services.RecoveryService _recovery = new();
        private readonly Services.FaceitUpdatesFixService _faceit = new();
        private readonly Services.RemoteDesktopFixService _remoteDesktop = new();
        private readonly Services.PrinterFixService _printer = new();

        public TroubleshootingPage()
        {
            this.InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshFixStates();
        }

        private void RefreshFixStates()
        {
            SetFix(FaceitFixButton, SafeStatus(() => _faceit.Status()));
            SetFix(RemoteDesktopFixButton, SafeStatus(() => _remoteDesktop.Status()));
            SetFix(PrinterFixButton, SafeStatus(() => _printer.Status()));
        }

        private static Services.ServiceFixStatus SafeStatus(Func<Services.ServiceFixStatus> read)
        {
            try { return read(); }
            catch { return new Services.ServiceFixStatus(false, string.Empty); }
        }

        private static void SetFix(Button button, Services.ServiceFixStatus status)
        {
            bool applied = status.Applied;
            button.Background = ThemeBrush(
                applied ? "ToggleSegmentIdleBrush" : "ToggleSegmentFillBrush",
                applied ? Microsoft.UI.Colors.Gray : Microsoft.UI.Colors.DarkBlue);
            button.Foreground = applied
                ? ThemeBrush("TextFillColorTertiaryBrush", Microsoft.UI.Colors.Gray)
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        }

        private static Microsoft.UI.Xaml.Media.Brush ThemeBrush(string key, Windows.UI.Color fallback)
        {
            try
            {
                if (Application.Current.Resources.TryGetValue(key, out object? value)
                    && value is Microsoft.UI.Xaml.Media.Brush brush)
                    return brush;
            }
            catch { }
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(fallback);
        }

        private void ApplyFaceitFix_Click(object sender, RoutedEventArgs e) =>
            ApplyFixWork(() => _faceit.Apply());

        private void ApplyRemoteDesktopFix_Click(object sender, RoutedEventArgs e) =>
            ApplyFixWork(() => _remoteDesktop.Apply());

        private void ApplyPrinterFix_Click(object sender, RoutedEventArgs e) =>
            ApplyFixWork(() => _printer.Apply());

        private void ApplyFixWork(Func<Services.ServiceFixResult> work)
        {
            Show(InfoBarSeverity.Informational, "Working…");
            Task.Run(work).ContinueWith(t =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (t.IsFaulted)
                        Show(InfoBarSeverity.Error, "Something went wrong: " + (t.Exception?.GetBaseException().Message ?? "unknown error."));
                    else
                        Show(t.Result.Ok ? InfoBarSeverity.Success : InfoBarSeverity.Error, t.Result.Message);
                    RefreshFixStates();
                });
            });
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
                });
            });
        }

        private void Show(InfoBarSeverity severity, string message)
        {
            StatusBar.Severity = severity;
            StatusBar.Message = message;
            StatusBar.IsOpen = true;
        }
    }
}
