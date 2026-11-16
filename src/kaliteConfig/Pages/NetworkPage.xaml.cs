using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using kaliteConfig.Services;
using kaliteConfig.ViewModels;

namespace kaliteConfig.Pages;

public sealed partial class NetworkPage : Page
{
    public NetworkViewModel ViewModel => (NetworkViewModel)DataContext;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _debounce;

    public NetworkPage()
    {
        InitializeComponent();
        Loaded += NetworkPage_Loaded;
        Unloaded += NetworkPage_Unloaded;
        ViewModel.BannerRequested += OnBanner;
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += OnNetChanged;
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetChanged;
    }

    public static Brush DotBrush(bool isUp) => new SolidColorBrush(isUp ? Colors.LimeGreen : Colors.Gray);

    private async void NetworkPage_Loaded(object sender, RoutedEventArgs e)
    {
        try { await ViewModel.RefreshAsync().ConfigureAwait(true); }
        catch (Exception ex) { OnBanner("Load failed", ex.Message); }
    }

    private void NetworkPage_Unloaded(object sender, RoutedEventArgs e)
    {
        try { _debounce?.Stop(); } catch { }
        ViewModel.BannerRequested -= OnBanner;
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= OnNetChanged;
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetChanged;
    }

    private void OnBanner(string title, string content)
    {
        NetInfoBar.Title = title;
        NetInfoBar.Message = content;
        bool failed = title.Contains("fail", StringComparison.OrdinalIgnoreCase);
        NetInfoBar.Severity = failed ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        NetInfoBar.IsOpen = true;
        UndoInfoButton.Visibility = Visibility.Collapsed;
        kaliteConfig.App.Notify("Network: " + title, content,
            failed ? Services.Notifications.AppNoticeSeverity.Error : Services.Notifications.AppNoticeSeverity.Success);
    }

    private void OnNetChanged(object? sender, EventArgs e)
    {
        try
        {
            var q = DispatcherQueue;
            if (q is null) return;
            _debounce ??= q.CreateTimer();
            _debounce.Stop();
            try { _debounce.Tick -= DebounceTick; } catch { }
            _debounce.Tick += DebounceTick;
            _debounce.Interval = TimeSpan.FromSeconds(1.5);
            _debounce.Start();
        }
        catch { }
    }

    private async void DebounceTick(Microsoft.UI.Dispatching.DispatcherQueueTimer timer, object args)
    {
        try
        {
            timer.Stop();
            if (Visibility != Visibility.Visible) return;
            try { await ViewModel.RefreshAsync().ConfigureAwait(true); }
            catch (Exception ex) { OnBanner("Live refresh failed", ex.Message); }
        }
        catch { }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        try { await ViewModel.RefreshAsync().ConfigureAwait(true); }
        catch (Exception ex) { OnBanner("Refresh failed", ex.Message); }
    }

    private async void AdapterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AdapterList.SelectedItem is NetAdapterInfo a)
        {
            try { await ViewModel.SelectAdapterAsync(a).ConfigureAwait(true); }
            catch (Exception ex) { OnBanner("Select failed", ex.Message); }
        }
    }

    private void SensitiveValue_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NetLabelValue row)
            row.ToggleReveal();
    }

    private void CopyRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NetLabelValue row)
        {
            try
            {
                var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dp.SetText($"{row.Label}: {row.Value}");
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            }
            catch (Exception ex) { OnBanner("Copy failed", ex.Message); }
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedAdapter is null) return;
        if (ViewModel.IsRiskyTarget && !ViewModel.ConfirmRiskyEdit)
        {
            OnBanner("Confirm first", "Virtual, VPN or disabled adapters need the explicit confirmation checkbox before anything writes.");
            return;
        }
        if (!ViewModel.ConfirmRiskyEdit && ViewModel.Adapters.Count(a => a.IsUp && (a.Kind == NetAdapterKind.Ethernet || a.Kind == NetAdapterKind.WiFi)) <= 1
            && ViewModel.SelectedAdapter.IsUp)
        {
            OnBanner("Confirm first", "This looks like the only adapter with a link. Tick the confirmation checkbox to apply anyway - the keep-countdown still protects you.");
            return;
        }
        try { await ViewModel.ApplyAsync().ConfigureAwait(true); }
        catch (Exception ex) { OnBanner("Apply failed", ex.Message); }
    }

    private void Keep_Click(object sender, RoutedEventArgs e) => ViewModel.KeepChanges();

    private async void RevertNow_Click(object sender, RoutedEventArgs e)
    {
        try { await ViewModel.RevertAsync().ConfigureAwait(true); }
        catch (Exception ex) { OnBanner("Revert failed", ex.Message); }
    }

    private async void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        try { await ViewModel.ResetDefaultsAsync().ConfigureAwait(true); }
        catch (Exception ex) { OnBanner("Reset failed", ex.Message); }
    }

    private async void Rollback_Click(object sender, RoutedEventArgs e)
    {
        try { await ViewModel.RollbackSnapshotAsync(SnapshotPicker.SelectedValue as string).ConfigureAwait(true); }
        catch (Exception ex) { OnBanner("Rollback failed", ex.Message); }
    }

    private void UndoInfoBar_Click(object sender, RoutedEventArgs e) { }

    private void DeviceManager_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("mmc.exe", "devmgmt.msc")
            {
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex) { OnBanner("Could not open Device Manager", ex.Message); }
    }

    private void ControlPanelProps_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? guid = ViewModel.SelectedAdapter?.Guid;
            var psi = new System.Diagnostics.ProcessStartInfo("control.exe", "ncpa.cpl")
            {
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(psi);

            string name = string.IsNullOrWhiteSpace(ViewModel.SelectedAdapter?.Name)
                ? "your adapter" : ViewModel.SelectedAdapter!.Name;
            OnBanner("Network Connections opened",
                string.IsNullOrWhiteSpace(guid)
                    ? "Double-click the adapter, then Properties -> Advanced for its driver settings."
                    : $"Select \"{name}\", then Properties -> Advanced for its driver settings.");
        }
        catch (Exception ex) { OnBanner("Could not open Control Panel", ex.Message); }
    }

    private void CopyDriver_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(string.Join("\n", ViewModel.DriverRows.Select(r => $"{r.Label}: {r.Value}")));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            OnBanner("Copied", "Driver info copied as text.");
        }
        catch (Exception ex) { OnBanner("Copy failed", ex.Message); }
    }

    private void NetTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (NetTabs.SelectedItem is TabViewItem { Visibility: not Visibility.Visible } hidden)
                NetTabs.SelectedIndex = 0;
        }
        catch { }
    }

    private void SysChangedOnly_Changed(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplySysFilter(SysChangedOnlyToggle.IsChecked == true);
    }

    private async void SysReRead_Click(object sender, RoutedEventArgs e)
    {
        try { await ViewModel.ReReadSysAsync().ConfigureAwait(true); }
        catch (Exception ex) { OnBanner("Re-read failed", ex.Message); }
    }

    private void ShowAdvancedToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (AdvancedTab is null || OffloadsTab is null) return;

        Visibility v = ShowAdvancedToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AdvancedTab.Visibility = v;
        OffloadsTab.Visibility = v;
    }
}
