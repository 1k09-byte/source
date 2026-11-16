using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using kaliteConfig.Models;
using System;
using System.Linq;

namespace kaliteConfig.Pages
{
    public sealed partial class UninstallerPage : Page
    {
        public UninstallerPage()
        {
            this.InitializeComponent();
            FilterBar.SelectedItem = FilterBar.Items[0] as SelectorBarItem;
            ViewModel.IsUninstallerTabActive = true;
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ViewModel.ShowSystem)) UpdateShowSystemButton();
            };
            UpdateShowSystemButton();
            this.Loaded += async (s, e) =>
            {
                if (ViewModel.Apps.Count == 0 && !ViewModel.IsLoading)
                {
                    await ViewModel.LoadAppsCommand.ExecuteAsync(null);
                }
            };
        }

        private void FilterBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            int index = sender.Items.IndexOf(sender.SelectedItem);
            if (index >= 0 && ViewModel.FilterIndex != index) ViewModel.FilterIndex = index;
        }

        private void ShowSystemButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ShowSystem = !ViewModel.ShowSystem;
            UpdateShowSystemButton();
        }

        private void UpdateShowSystemButton()
        {
            bool on = ViewModel.ShowSystem;
            ShowSystemButton.Content = on ? "On" : "Off";
            ShowSystemButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                on ? Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            ShowSystemButton.Foreground = on
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }

        private void AppList_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            int n = AppList.SelectedItems.Count;
            SelectionText.Text = n == 0 ? string.Empty : $"{n} selected";
            AppDetailsPane.IsPaneOpen = n > 0;
            foreach (var removed in e.RemovedItems.OfType<UninstallerItem>())
                removed.IsSelected = false;
            foreach (var added in e.AddedItems.OfType<UninstallerItem>())
                added.IsSelected = true;
        }

        private UninstallerItem? _pressedApp;
        private bool _appPressWasSelected;

        private void AppRow_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _pressedApp = (sender as FrameworkElement)?.DataContext as UninstallerItem;
            _appPressWasSelected = _pressedApp != null && ReferenceEquals(_pressedApp, AppList.SelectedItem);
        }

        private void AppRow_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_appPressWasSelected || _pressedApp is null || !ReferenceEquals(_pressedApp, AppList.SelectedItem)) return;
            _appPressWasSelected = false;
            DispatcherQueue.TryEnqueue(() =>
            {
                AppList.SelectedItem = null;
                ViewModel.SelectedApp = null;
            });
        }

        private void DetailsClose_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AppList.SelectedItem = null;
                ViewModel.SelectedApp = null;
            });
        }

        private async void DetailsUninstall_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var targets = ViewModel.Apps.Where(a => a.IsSelected).ToList();
            if (ViewModel.SelectedApp != null && targets.Count == 0) targets.Add(ViewModel.SelectedApp);
            if (targets.Count == 0) return;
            var lines = string.Join("\n", targets.Take(8).Select(a => $"- {a.Name}"));
            if (targets.Count > 8) lines += $"\n- and {targets.Count - 8} more";
            var dialog = new ContentDialog
            {
                Title = $"Uninstall {targets.Count} application{(targets.Count == 1 ? "" : "s")}?",
                Content = new TextBlock { Text = lines + "\n\nRuns each registered uninstaller, then cleans leftovers.", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
                PrimaryButtonText = "Uninstall",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            await ViewModel.UninstallSelectedCommand.ExecuteAsync(null);
        }

        private async void DetailsForceRemove_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var name = ViewModel.SelectedApp?.Name ?? "selected applications";
            var dialog = new ContentDialog
            {
                Title = "Force-remove residue?",
                Content = new TextBlock { Text = $"Scans for leftover files, folders and registry keys of {name} and deletes what it finds. The app itself is not uninstalled by this.", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
                PrimaryButtonText = "Force remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            await ViewModel.ForceRemoveCommand.ExecuteAsync(null);
        }
    }
}
