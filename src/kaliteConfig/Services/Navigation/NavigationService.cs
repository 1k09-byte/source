using System;
using Microsoft.UI.Xaml.Controls;

namespace kaliteConfig.Services.Navigation;

public enum PageKey
{
    Apps, Drivers, Startup, Graphics, Gaming, PriorityBoosts, ThreadTuner, Snip,
    Network, WindowsSettings, BiosManager, PowerPlans, Troubleshooting, Settings, Input, Home
}

public interface INavigationService
{
    void Attach(Frame frame);
    bool Navigate(PageKey key);
    PageKey? Current { get; }
}

public sealed class NavigationService : INavigationService
{
    private Frame? _frame;
    public PageKey? Current { get; private set; }

    private static Type TypeFor(PageKey key) => key switch
    {
        PageKey.Apps => typeof(Pages.InstallerHubPage),
        PageKey.Drivers => typeof(Pages.DriversPage),
        PageKey.Startup => typeof(Pages.StartupPage),
        PageKey.Graphics => typeof(Pages.GraphicsHubPage),
        PageKey.Gaming => typeof(Pages.AffinityPage),
        PageKey.PriorityBoosts => typeof(Pages.PriorityBoostsPage),
        PageKey.ThreadTuner => typeof(Pages.ThreadTunerPage),
        PageKey.Snip => typeof(Pages.SnipPage),
        PageKey.Network => typeof(Pages.NetworkPage),
        PageKey.Input => typeof(Pages.InputPage),
        PageKey.WindowsSettings => typeof(Pages.WindowsSettingsHubPage),
        PageKey.BiosManager => typeof(Pages.BiosManagerPage),
        PageKey.PowerPlans => typeof(Pages.PowerPlansPage),
        PageKey.Troubleshooting => typeof(Pages.TroubleshootingPage),
        PageKey.Home => typeof(Pages.HomePage),
        _ => typeof(Pages.SettingsPage),
    };

    public static PageKey KeyForTag(string tag) => tag switch
    {
        "AppsPage" => PageKey.Apps,
        "DriversPage" => PageKey.Drivers,
        "StartupPage" => PageKey.Startup,
        "GraphicsPage" => PageKey.Graphics,
        "GamingPage" => PageKey.Gaming,
        "PriorityBoostsPage" => PageKey.PriorityBoosts,
        "ThreadTunerPage" => PageKey.ThreadTuner,
        "SnipPage" => PageKey.Snip,
        "NetworkPage" => PageKey.Network,
        "InputPage" => PageKey.Input,
        "WindowsSettingsPage" => PageKey.WindowsSettings,
        "BiosManagerPage" => PageKey.BiosManager,
        "PowerPlansPage" => PageKey.PowerPlans,
        "TroubleshootingPage" => PageKey.Troubleshooting,
        "HomePage" => PageKey.Home,
        _ => PageKey.Settings,
    };

    public void Attach(Frame frame) => _frame = frame;

    public bool Navigate(PageKey key)
    {
        if (_frame is null) return false;
        Current = key;
        Type type = TypeFor(key);
        return _frame.Navigate(type);
    }
}
