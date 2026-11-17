using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kaliteConfig.Models;
using kaliteConfig.ViewModels;

namespace kaliteConfig.Pages
{
    public sealed partial class AffinityPage : Page
    {
        public AffinityViewModel ViewModel { get; }

        private readonly Services.SmallProcessorMaskService _spm = new();
        private readonly List<(int Bit, CheckBox Check)> _spmChecks = new();
        private bool _spmLoading;
        private bool _spmOverriding;

        public AffinityPage()
        {
            ViewModel = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<AffinityViewModel>(App.Services) ?? new AffinityViewModel();
            InitializeComponent();
            try { ReservedCpuSetsFrame.Navigate(typeof(ReservedCpuSetsPage)); } catch { }
            BuildSpmCores();
            LoadSpmState();
            Loaded += AffinityPage_Loaded;
        }

        private async void AffinityPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.IsRefreshing)
            {
                await ViewModel.RefreshDevicesCommand.ExecuteAsync(null);
            }
        }

        // ---------- Scheduling / Small Processor Mask ----------

        private void BuildSpmCores()
        {
            SpmCoresHost.Children.Clear();
            _spmChecks.Clear();

            int count = Environment.ProcessorCount;

            // Group logical processors by physical core: primary thread on top, SMT siblings below.
            var groups = new List<List<int>>();
            var assigned = new bool[Math.Max(count, 1)];
            foreach (ulong coreMask in Services.SmallProcessorMaskService.GetCoreMasks())
            {
                var group = new List<int>();
                for (int bit = 0; bit < Math.Min(count, 64); bit++)
                {
                    if (((coreMask >> bit) & 1UL) == 0) continue;
                    group.Add(bit);
                    assigned[bit] = true;
                }
                if (group.Count > 0) groups.Add(group);
            }
            for (int i = 0; i < count; i++) // fallback for anything the topology query missed
            {
                if (!assigned[i]) groups.Add(new List<int> { i });
            }

            const int ColumnsPerRow = 8;
            var rows = new List<List<List<int>>>();
            foreach (var group in groups)
            {
                int row = rows.Count == 0 || rows[^1].Count == ColumnsPerRow
                    ? rows.Count
                    : rows.Count - 1;
                if (row == rows.Count) rows.Add(new List<List<int>>());
                rows[row].Add(group);
            }

            foreach (var row in rows)
            {
                var grid = new Grid { ColumnSpacing = 8, RowSpacing = 2 };
                for (int c = 0; c < row.Count; c++)
                    grid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(1, GridUnitType.Star),
                        MaxWidth = 140,
                    });
                int maxThreads = row.Max(g => g.Count);
                for (int r = 0; r < maxThreads; r++)
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                for (int c = 0; c < row.Count; c++)
                {
                    var group = row[c];
                    for (int r = 0; r < group.Count; r++)
                    {
                        int lp = group[r];
                        bool sibling = r > 0; // first thread of a core is the primary, the rest are its HT siblings
                        var check = new CheckBox { Content = sibling ? $"CPU {lp} (HT)" : $"CPU {lp}" };
                        check.Checked += (_, _) => OnSpmCheckChanged();
                        check.Unchecked += (_, _) => OnSpmCheckChanged();
                        Grid.SetColumn(check, c);
                        Grid.SetRow(check, r);
                        grid.Children.Add(check);
                        _spmChecks.Add((lp, check));
                    }
                }

                SpmCoresHost.Children.Add(grid);
            }
        }

        private void LoadSpmState()
        {
            _spmLoading = true;
            try
            {
                ulong? saved = _spm.Read();
                SetSpmMode(saved.HasValue);
                ApplySpmMaskToChecks(saved ?? 0);
                UpdateSpmMaskText();
            }
            finally { _spmLoading = false; }
        }

        private void SetSpmMode(bool overriding)
        {
            _spmOverriding = overriding;
            ApplySpmSegmentVisual(SpmDefaultButton, !overriding);
            ApplySpmSegmentVisual(SpmOverrideButton, overriding);
            foreach (var (_, check) in _spmChecks) check.IsEnabled = overriding;
        }

        private void ApplySpmMaskToChecks(ulong mask)
        {
            _spmLoading = true;
            try
            {
                foreach (var (bit, check) in _spmChecks)
                    check.IsChecked = (mask & (1UL << bit)) != 0;
            }
            finally { _spmLoading = false; }
        }

        private ulong CurrentSpmMask()
        {
            ulong mask = 0;
            foreach (var (bit, check) in _spmChecks)
                if (check.IsChecked == true) mask |= 1UL << bit;
            return mask;
        }

        private void UpdateSpmMaskText()
        {
            ulong? saved = _spm.Read();
            SpmMaskText.Text = saved is null
                ? "Windows default (no SmallProcessorMask set)"
                : $"Mask: {saved.Value} (0x{saved.Value:X8})";
        }

        private void SpmMode_Click(object sender, RoutedEventArgs e)
        {
            if (_spmLoading) return;
            bool overriding = ReferenceEquals(sender, SpmOverrideButton);
            if (overriding == _spmOverriding) return;

            SetSpmMode(overriding);

            if (overriding)
            {
                // Start from whatever is in the registry (nothing is written until a core is ticked).
                ApplySpmMaskToChecks(_spm.Read() ?? 0);
                UpdateSpmMaskText();
                return;
            }

            ApplySpmMaskToChecks(0);
            string? error = _spm.Write(0);
            UpdateSpmMaskText();
            kaliteConfig.App.Notify("Scheduling",
                error ?? "SmallProcessorMask removed. Windows default scheduling is back in place. Restart to apply.",
                error is null ? Services.Notifications.AppNoticeSeverity.Success : Services.Notifications.AppNoticeSeverity.Error);
        }

        private void OnSpmCheckChanged()
        {
            if (_spmLoading || !_spmOverriding) return;

            ulong mask = CurrentSpmMask();
            string? error = _spm.Write(mask);
            UpdateSpmMaskText();
            kaliteConfig.App.Notify("Scheduling",
                error ?? (mask == 0
                    ? "SmallProcessorMask removed. Windows default scheduling is back in place. Restart to apply."
                    : $"SmallProcessorMask set to {mask} (0x{mask:X8}) and verified. Restart to apply."),
                error is null ? Services.Notifications.AppNoticeSeverity.Success : Services.Notifications.AppNoticeSeverity.Error);
        }

        private static void ApplySpmSegmentVisual(Button tab, bool selected)
        {
            tab.Background = selected
                ? ThemeBrush("ToggleSegmentFillBrush", Microsoft.UI.Colors.White)
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            tab.Foreground = selected
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
                : ThemeBrush("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray);
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

        private async void DeviceSummary_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not AffinityDeviceItem device) return;

            if (device.IsExpanded)
            {
                device.IsExpanded = false;
                return;
            }

            CollapseExpanded();

            try
            {
                await ViewModel.LoadDeviceDetailsAsync(device);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Device not available", ex.Message);
                return;
            }

            device.IsExpanded = true;
        }

        private void CollapseExpanded()
        {
            foreach (var device in ViewModel.GraphicsDevices
                .Concat(ViewModel.NetworkDevices)
                .Concat(ViewModel.UsbDevices)
                .Concat(ViewModel.AudioDevices))
            {
                if (device.IsExpanded) device.IsExpanded = false;
            }
        }

        private async void DevicePanel_ApplyRequested(object? sender, EventArgs e) => await ApplyDeviceAsync();

        private void DevicePanel_CancelRequested(object? sender, EventArgs e)
        {
            ViewModel.DiscardDialogChanges();
            CollapseExpanded();
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            await new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "Close",
                XamlRoot = XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
            }.ShowAsync();
        }

        private async Task ApplyDeviceAsync()
        {
            if (ViewModel.SelectedDevice is null)
            {
                CollapseExpanded();
                return;
            }

            if (!ViewModel.IsDevicePresent(ViewModel.SelectedDevice))
            {
                string goneName = ViewModel.SelectedDevice.Name;
                CollapseExpanded();
                await ViewModel.RefreshDevicesCommand.ExecuteAsync(null);
                await ShowMessageAsync("Device not present",
                    $"{goneName} is no longer present, so nothing was written. The device list has been rescanned - this is expected after a GPU restart or driver install.");
                return;
            }

            int previousChanges = ViewModel.TrackedChanges.Count;
            ViewModel.ApplyDeviceChanges(ViewModel.SelectedDevice);
            CollapseExpanded();

            if (ViewModel.TrackedChanges.Count > previousChanges)
            {
                await ViewModel.RestartDeviceAfterApplyAsync(ViewModel.SelectedDevice);
                kaliteConfig.App.Notify("Interrupts & affinity", ViewModel.StatusText,
                    ViewModel.StatusText.Contains("fail", StringComparison.OrdinalIgnoreCase)
                        ? Services.Notifications.AppNoticeSeverity.Error
                        : Services.Notifications.AppNoticeSeverity.Success);
            }
            else
            {
                kaliteConfig.App.Notify("Interrupts & affinity", "Nothing changed on that device.",
                    Services.Notifications.AppNoticeSeverity.Info);
            }
        }


        private sealed class ChangeListRow
        {
            public ChangeListRow(string device, string detail)
            {
                Device = device;
                Detail = detail;
            }

            public string Device { get; }
            public string Detail { get; }
        }

        private async void ViewChanges_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = $"Tracked Changes ({ViewModel.TrackedChanges.Count})",
                CloseButtonText = "Close",
                XamlRoot = XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
            };
            if (ViewModel.TrackedChanges.Count == 0)
            {
                dialog.Content = new TextBlock
                {
                    Text = "No changes have been made yet.",
                    TextWrapping = TextWrapping.Wrap
                };
            }
            else
            {
                var rows = new System.Collections.Generic.List<ChangeListRow>();
                foreach (var c in ViewModel.TrackedChanges)
                {
                    string oldVal = FormatValue(c.PropertyName, c.OldValue);
                    string newVal = FormatValue(c.PropertyName, c.NewValue);
                    rows.Add(new ChangeListRow(c.DeviceName, $"{c.PropertyName}: {oldVal} → {newVal}"));
                }
                dialog.Content = new ListView
                {
                    ItemsSource = rows,
                    ItemTemplate = (DataTemplate)Resources["ChangeRowTemplate"],
                    ItemContainerStyle = (Style)Resources["AffinityRowContainerStyle"],
                    SelectionMode = ListViewSelectionMode.None
                };
            }
            await dialog.ShowAsync();
        }

        private static string FormatValue(string propertyName, object? value)
        {
            if (value is null) return "-";
            return propertyName switch
            {
                "MsiEnabled" => (bool)value ? "On" : "Off",
                "MessageNumberLimit" => value is int i ? (i == 0 ? "Auto" : i.ToString()) : "-",
                "DevicePolicy" => Services.AffinityService.DevicePolicyShort((int?)value),
                "DevicePriority" => Services.AffinityService.DevicePriorityName((int?)value),
                "AffinityMask" => Services.AffinityService.AffinityMaskText((ulong?)value),
                _ => value.ToString() ?? "-"
            };
        }

        public static Visibility BoolToVis(bool isVisible)
        {
            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public static string ExpanderGlyph(bool expanded)
        {
            return expanded ? "\uE70D" : "\uE76C";
        }

        public static BitmapImage? VendorIcon(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string? path = null;
            if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                path = "ms-appx:///Assets/nvidia-logo.png";
            else if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                path = "ms-appx:///Assets/amd-logo.png";
            else if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                path = "ms-appx:///Assets/intel-logo.png";
            return path is null ? null : new BitmapImage(new Uri(path));
        }

        public static Visibility NonEmptyToVisibility(int count)
        {
            return count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        public static bool Not(bool val) => !val;

        public static string CountText(int count) => count.ToString();
    }
}
