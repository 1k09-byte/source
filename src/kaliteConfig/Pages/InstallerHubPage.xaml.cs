using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace kaliteConfig.Pages;

public sealed partial class InstallerHubPage : Page
{
    private bool _uninstallTab;

    public InstallerHubPage()
    {
        this.InitializeComponent();
        ContentFrame.Navigate(typeof(InstallerPage));
    }

    private void Tab_Click(object sender, RoutedEventArgs e) => ShowTab(ReferenceEquals(sender, TabUninstall));

    private void ShowTab(bool uninstall)
    {
        ApplyTabVisual(TabInstall, !uninstall);
        ApplyTabVisual(TabUninstall, uninstall);
        if (uninstall == _uninstallTab) return;

        _uninstallTab = uninstall;
        ContentFrame.Navigate(uninstall ? typeof(UninstallerPage) : typeof(InstallerPage));
    }

    private static void ApplyTabVisual(Button tab, bool selected)
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
}
