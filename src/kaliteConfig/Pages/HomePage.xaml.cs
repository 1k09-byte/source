using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using System;
using kaliteConfig.Services.Navigation;
using kaliteConfig.ViewModels;

namespace kaliteConfig.Pages
{
    public sealed partial class HomePage : Page
    {
        private const string DiscordInviteUrl = "https://discord.gg/faQgV6yayY";
        private const int ShortcutColumns = 4;

        private readonly UpdateViewModel _update = new();
        private bool _checking;
        private bool _updateReady;

        public HomePage()
        {
            InitializeComponent();
            BuildShortcuts();
            string version = Services.UpdateCheckService.CurrentVersion ?? "unknown";
            VersionText.Text = $"kaliteConfig v{version}";
        }

        private void BuildShortcuts()
        {
            var entries = Array.FindAll(MainWindow.DockEntries, e => e.Key != PageKey.Home);
            for (int c = 0; c < ShortcutColumns; c++)
                ShortcutsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int rows = (entries.Length + ShortcutColumns - 1) / ShortcutColumns;
            for (int r = 0; r < rows; r++)
                ShortcutsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                var content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                content.Children.Add(ShortcutIcon(entry));
                content.Children.Add(new TextBlock
                {
                    Text = entry.Label,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });

                var button = new Button
                {
                    Tag = entry,
                    Content = content,
                    Margin = new Thickness(0),
                    Padding = new Thickness(10, 9, 10, 9),
                    CornerRadius = new CornerRadius(10),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                AutomationProperties.SetName(button, entry.Label);
                button.Click += Shortcut_Click;
                Grid.SetColumn(button, i % ShortcutColumns);
                Grid.SetRow(button, i / ShortcutColumns);
                ShortcutsGrid.Children.Add(button);
            }
        }

        private static FrameworkElement ShortcutIcon(MainWindow.DockEntry entry)
        {
            try
            {
                var icon = Services.NavIcons.Create(entry.Glyph, entry.Path, entry.SymbolFont);
                if (icon is PathIcon pathIcon) { pathIcon.Width = 20; pathIcon.Height = 20; }
                else if (icon is FontIcon fontIcon) { fontIcon.FontSize = 20; }
                return icon;
            }
            catch
            {
                return new FontIcon { Glyph = "\uE8A1", FontSize = 20 };
            }
        }

        private void Shortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: MainWindow.DockEntry entry }) return;
            (App.MainWindow as MainWindow)?.GoToPage(NavigationService.KeyForTag(entry.Tag), entry.Tag);
        }

        private async void DiscordButton_Click(object sender, RoutedEventArgs e)
        {
            try { await Windows.System.Launcher.LaunchUriAsync(new Uri(DiscordInviteUrl)); } catch { }
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_checking) return;

            if (_updateReady)
            {
                _update.UpdateNowCommand.Execute(null);
                return;
            }

            _checking = true;
            UpdateButton.IsEnabled = false;
            UpdateStatusText.Text = string.Empty;
            UpdateButtonText.Text = "Checking...";
            try
            {
                await _update.CheckForUpdateAsync();
            }
            catch (Exception)
            {
                UpdateStatusText.Text = "Update check failed";
            }
            finally
            {
                _checking = false;
                UpdateButton.IsEnabled = true;
            }

            if (_update.IsAvailable)
            {
                _updateReady = true;
                UpdateIcon.Glyph = "\uE7B8";
                UpdateButtonText.Text = $"Update to v{_update.LatestVersion}";
                UpdateStatusText.Text = $"v{_update.LatestVersion} is available";
            }
            else
            {
                UpdateButtonText.Text = "Check for updates";
                UpdateStatusText.Text = string.IsNullOrWhiteSpace(_update.StatusText)
                    ? "You're up to date"
                    : _update.StatusText;
            }
        }
    }
}
