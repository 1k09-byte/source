using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace kaliteConfig.Pages;

public sealed partial class GraphicsHubPage : Page
{
    private bool _overclockTab = true; // start flipped so the constructor's ShowTab(false) navigates

    public GraphicsHubPage()
    {
        this.InitializeComponent();
        ShowTab(false);
    }

    private void Tab_Click(object sender, RoutedEventArgs e) => ShowTab(ReferenceEquals(sender, TabOverclock));

    private void ShowTab(bool overclock)
    {
        ApplyTabVisual(TabDrivers, !overclock);
        ApplyTabVisual(TabOverclock, overclock);
        if (overclock == _overclockTab) return;

        _overclockTab = overclock;
        ContentFrame.Navigate(overclock ? typeof(OverclockPage) : typeof(GpuDriversPage));
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
