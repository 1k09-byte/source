using kaliteConfig.Gaming.GameMode;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Threading.Tasks;

namespace kaliteConfig.Controls;

public sealed partial class GameModeDialog : ContentDialog
{
    private readonly GameModeViewModel _game;

    public GameModeDialog(GameModeViewModel game)
    {
        _game = game;
        InitializeComponent();
        kaliteConfig.Services.Theming.ThemeHelper.ApplyTheme(this);
        DataContext = game;
    }

    public Task ShowOverAsync(XamlRoot root)
    {
        XamlRoot = root;
        return ShowAsync().AsTask();
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => _game.Preview();

    private void Resume_Click(object sender, RoutedEventArgs e) => _game.ResumeAll();

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_game.GamePathInput))
        {
            _game.AddGamePath();
            return;
        }

        var window = App.MainWindow;
        if (window is null) return;
        string? file = Services.Win32FilePicker.PickOpenFile(
            WinRT.Interop.WindowNative.GetWindowHandle(window),
            new[]
            {
                ("Applications (*.exe)", "*.exe"),
                ("Shortcuts (*.lnk)", "*.lnk"),
                ("All files (*.*)", "*.*"),
            },
            "Choose a game executable");
        if (string.IsNullOrWhiteSpace(file)) return;
        _game.GamePathInput = file;
        _game.AddListEntry();
    }

    private void AddFocused_Click(object sender, RoutedEventArgs e) => _game.AddFocusedApp();

    private void AddPopularGames_Click(object sender, RoutedEventArgs e) => _game.AddPopularGames();

    private void AddIgnored_Click(object sender, RoutedEventArgs e) => _game.AddIgnoredApp();

    private void RemoveIgnored_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameModeListRow row }) _game.RemoveIgnoredApp(row);
    }

    private void IgnoredBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        _game.AddIgnoredApp();
    }

    private void BrowseIgnored_Click(object sender, RoutedEventArgs e)
    {
        var window = App.MainWindow;
        if (window is null) return;
        string? file = Services.Win32FilePicker.PickOpenFile(
            WinRT.Interop.WindowNative.GetWindowHandle(window),
            new[]
            {
                ("Applications (*.exe)", "*.exe"),
                ("All files (*.*)", "*.*"),
            },
            "Choose an executable to ignore");
        if (string.IsNullOrWhiteSpace(file)) return;
        _game.IgnoredAppInput = file;
        _game.AddIgnoredApp();
    }

    private void PathBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        _game.AddListEntry();
    }

    private void ListFeedback_Close(InfoBar sender, object args) => _game.ClearListFeedback();

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameModeListRow row }) _game.RemoveListItem(row);
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e) => _game.CopyLog();

    private void ExportLog_Click(object sender, RoutedEventArgs e) => _game.ExportLog();

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _game.ClearLog();

    private void Enabled_Toggled(object sender, RoutedEventArgs e)
    {
        bool want = (sender as ToggleSwitch)?.IsOn ?? false;
        if (_game.Enabled != want) _game.Enabled = want;
    }

    private void SuspendWake_Toggled(object sender, RoutedEventArgs e)
    {
        bool want = (sender as ToggleSwitch)?.IsOn ?? false;
        if (_game.SuspendBackgroundWake != want) _game.SuspendBackgroundWake = want;
    }

    private void SuspendInput_Toggled(object sender, RoutedEventArgs e)
    {
        bool want = (sender as ToggleSwitch)?.IsOn ?? false;
        if (_game.SuspendBackgroundInput != want) _game.SuspendBackgroundInput = want;
    }

    private void BackgroundMode_Toggled(object sender, RoutedEventArgs e)
    {
        bool want = (sender as ToggleSwitch)?.IsOn ?? false;
        if (_game.BackgroundMode != want) _game.BackgroundMode = want;
    }

    private void Debounce_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (double.IsNaN(args.NewValue) || _game.DebounceMs == args.NewValue) return;
        _game.DebounceMs = args.NewValue;
    }
}
