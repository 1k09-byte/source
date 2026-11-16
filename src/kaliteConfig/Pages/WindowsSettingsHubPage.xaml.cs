using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace kaliteConfig.Pages
{
    public sealed partial class WindowsSettingsHubPage : Page
    {
        private readonly Services.WindowsSettingsService _kernel = new();
        private readonly Services.HibernateSettingService _hibernate = new();
        private readonly Services.KernelDmaService _dma = new();
        private bool _loadingKernelToggles;
        private bool _loadingDma;
        private bool _loadingSvcSplit;
        private bool _loadingHibernate;
        private bool _loadingPowerMode;
        private readonly Services.PowerKernelModeService _powerMode = new();

        public WindowsSettingsHubPage()
        {
            this.InitializeComponent();
            this.Loaded += (_, _) => { LoadKernelToggles(); LoadSvcSplit(); LoadDmaToggle(); LoadPowerMode(); };
        }

        private void LoadKernelToggles()
        {
            _loadingKernelToggles = true;
            try
            {
                LoadOneKernelToggle("MmcssStatus", MmcssToggle, MmcssCard);
                LoadOneKernelToggle("Vbs", VbsToggle, VbsCard);
                LoadOneKernelToggle("Hvci", HvciToggle, HvciCard);
                LoadOneKernelToggle("HwSchMode", HagsToggle, HagsCard);
                LoadHibernateToggle();
            }
            finally { _loadingKernelToggles = false; }
        }

        private void LoadHibernateToggle()
        {
            _loadingHibernate = true;
            try
            {
                var status = _hibernate.Read();
                HibernateToggle.IsOn = status.Enabled;
                HibernateCard.Description = status.Error is null
                    ? "Turning it off clears the hibernate flags and keeps fast startup off. Needs administrator rights."
                    : $"Can't read: {status.Error}";
            }
            finally { _loadingHibernate = false; }
        }

        private void LoadDmaToggle()
        {
            _loadingDma = true;
            try
            {
                bool? value = _dma.Read();
                DmaToggle.IsOn = value == true;
                DmaCard.Description = value is null
                    ? "IOMMUFlags is not set. Turning this on writes 1, turning it off writes 0. Needs a restart."
                    : $"IOMMUFlags is {value.Value switch { true => 1, false => 0 }}. Takes effect after a restart.";
            }
            finally { _loadingDma = false; }
        }

        private void Dma_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingDma || sender is not ToggleSwitch toggle) return;
            bool target = toggle.IsOn;
            string? error = _dma.Write(target);
            LoadDmaToggle();
            App.Notify("Kernel DMA Protection", error ?? $"IOMMUFlags set to {(target ? 1 : 0)} and verified. Restart to apply.",
                error is null ? Services.Notifications.AppNoticeSeverity.Success : Services.Notifications.AppNoticeSeverity.Error);
        }

        private void Hibernate_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingHibernate || sender is not ToggleSwitch toggle) return;
            bool target = toggle.IsOn;
            string? error = _hibernate.Write(target);
            LoadHibernateToggle();
            App.Notify(
                "Hibernation",
                error ?? (target
                    ? "Hibernation is on. HibernateEnabled, AllowHibernate and the default flag were set to 1 and verified. Fast startup stays off."
                    : "Hibernation is off. HibernateEnabled, AllowHibernate and the default flag were set to 0 and verified. Fast startup stays off."),
                error is null ? Services.Notifications.AppNoticeSeverity.Success : Services.Notifications.AppNoticeSeverity.Error);
        }

        private void LoadPowerMode()
        {
            _loadingPowerMode = true;
            try
            {
                var mode = _powerMode.Read();
                PowerModeToggle.IsOn = mode == Services.PowerKernelMode.Aggressive;
                PowerModeCard.Description = mode switch
                {
                    Services.PowerKernelMode.Aggressive =>
                        "Aggressive is applied: power-event processing and parked-core requests are off, QoS hints and IPI last-clock-owner handling are on, core parking is forced off. Restart to apply.",
                    Services.PowerKernelMode.Balanced =>
                        "Balanced - Windows defaults are in place. Switching to Aggressive writes the power and kernel flags, switching back removes them again. Needs a restart.",
                    _ => "Custom - some of these values were written by another tool. Pick Balanced or Aggressive to overwrite them.",
                };
            }
            finally { _loadingPowerMode = false; }
        }

        private void PowerMode_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingPowerMode || sender is not ToggleSwitch toggle) return;
            bool aggressive = toggle.IsOn;

            var current = _powerMode.Read();
            if ((current == Services.PowerKernelMode.Aggressive) == aggressive) return;

            string? error = _powerMode.Apply(aggressive
                ? Services.PowerKernelMode.Aggressive
                : Services.PowerKernelMode.Balanced);
            LoadPowerMode();
            App.Notify(
                "CPU and power tuning mode",
                error ?? (aggressive
                    ? "Aggressive mode applied: the power and kernel flags were written and verified. Restart to apply."
                    : "Balanced mode applied: the aggressive flags were removed and verified. Restart to apply."),
                error is null ? Services.Notifications.AppNoticeSeverity.Success : Services.Notifications.AppNoticeSeverity.Error);
        }

        private void LoadOneKernelToggle(string id, ToggleSwitch toggle, CommunityToolkit.WinUI.Controls.SettingsCard card)
        {
            var def = Services.WindowsSettingsService.Find(id);
            if (def == null) return;
            try
            {
                var det = _kernel.Detect(def);
                bool unset = det.State == Services.WindowsSettingsService.TweakState.WindowsDefault;
                toggle.IsOn = det.State == Services.WindowsSettingsService.TweakState.On
                    || (unset && def.DefaultOnWhenAbsent);
                card.Description = string.Empty;
            }
            catch
            {
                card.Description = string.Empty;
            }
        }

        private CommunityToolkit.WinUI.Controls.SettingsCard? CardFor(string id) => id switch
        {
            "MmcssStatus" => MmcssCard,
            "Vbs" => VbsCard,
            "Hvci" => HvciCard,
            "HwSchMode" => HagsCard,
            _ => null,
        };

        private void KernelToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingKernelToggles || sender is not ToggleSwitch toggle || toggle.Tag is not string id) return;
            var def = Services.WindowsSettingsService.Find(id);
            if (def == null) return;
            try
            {
                _kernel.Write(def, toggle.IsOn);
                LoadKernelToggles();
                App.Notify(def.Name, $"Set to {(toggle.IsOn ? "on" : "off")} and verified.", Services.Notifications.AppNoticeSeverity.Success);
            }
            catch (Exception ex)
            {
                LoadKernelToggles();
                if (CardFor(id) is { } card) card.Description += $" Write denied: {ex.Message}";
                App.Notify(def.Name, $"Could not apply the change: {ex.Message}", Services.Notifications.AppNoticeSeverity.Error);
            }
        }

        private void KernelToggle_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string id) return;
            var def = Services.WindowsSettingsService.Find(id);
            if (def == null || !_kernel.HasBackup(def)) return;

            var menu = new MenuFlyout();
            var restore = new MenuFlyoutItem { Text = "Restore original value" };
            restore.Click += (_, _) =>
            {
                try
                {
                    _kernel.ResetToOriginal(def);
                    LoadKernelToggles();
                }
                catch { }
            };
            menu.Items.Add(restore);
            menu.ShowAt(fe, e.GetPosition(fe));
        }

        private void LoadSvcSplit()
        {
            _loadingSvcSplit = true;
            try
            {
                SvcSplitBox.Items.Clear();
                uint? current = null;
                try { current = _kernel.ReadSvcSplit(); }
                catch (Exception ex) { SvcSplitCard.Description = $"Unreadable: {ex.Message}"; return; }

                int selected = -1;
                for (int i = 0; i < Services.WindowsSettingsService.SvcSplitPresets.Length; i++)
                {
                    var (value, label) = Services.WindowsSettingsService.SvcSplitPresets[i];
                    SvcSplitBox.Items.Add(label);
                    if (current == value) selected = i;
                }
                if (current == null)
                {
                    selected = 0;
                    SvcSplitCard.Description = "Not set - Windows default (380000 KB). Takes effect after a restart.";
                }
                else if (selected < 0)
                {
                    string custom = $"Custom: {current} KB";
                    SvcSplitBox.Items.Add(custom);
                    selected = SvcSplitBox.Items.Count - 1;
                    SvcSplitCard.Description = $"Detected: {current} KB. Takes effect after a restart.";
                }
                else
                {
                    SvcSplitCard.Description = $"Detected: {current} KB. Takes effect after a restart.";
                }
                SvcSplitBox.SelectedIndex = selected;
            }
            finally { _loadingSvcSplit = false; }
        }

        private void SvcSplitBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingSvcSplit || SvcSplitBox.SelectedIndex < 0) return;
            if (SvcSplitBox.SelectedIndex >= Services.WindowsSettingsService.SvcSplitPresets.Length) return;
            var (value, _) = Services.WindowsSettingsService.SvcSplitPresets[SvcSplitBox.SelectedIndex];
            try
            {
                if (value == 380000u)
                {
                    _kernel.ResetSvcSplit();
                }
                else
                {
                    _kernel.WriteSvcSplit(value);
                }
                LoadSvcSplit();
                App.Notify("Service host splitting", "Saved. It applies after a restart.", Services.Notifications.AppNoticeSeverity.Success);
            }
            catch (Exception ex)
            {
                LoadSvcSplit();
                SvcSplitCard.Description += $" Write denied: {ex.Message}";
                App.Notify("Service host splitting", $"Could not apply the change: {ex.Message}", Services.Notifications.AppNoticeSeverity.Error);
            }
        }

        private void SvcSplitReset_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _kernel.ResetSvcSplit();
            }
            catch { }
            LoadSvcSplit();
        }
    }
}
