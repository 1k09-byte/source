using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace kaliteConfig.Gaming.GameMode;

public sealed partial class GameModeViewModel : ObservableObject
{
    private readonly GameModeEngine _engine;
    private readonly DispatcherQueue _dispatcher;
    private bool _updating;

    public GameModeViewModel(GameModeEngine engine)
    {
        _engine = engine;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _engine.Changed += OnEngineChanged;
        _updating = true;
        StatusText = string.Empty;
        StatusSeverity = InfoBarSeverity.Informational;
        GamePathInput = string.Empty;
        AddKindIndex = 0;
        JournalText = string.Empty;
        EnableButtonText = "Enable Game Mode";
        PreviewSummary = string.Empty;
        ListFeedback = string.Empty;
        ListFeedbackSeverity = InfoBarSeverity.Informational;
        DebounceMs = 750;
        Refresh();
    }

    public ObservableCollection<GameModeAffectedRow> Affected { get; } = new();
    public ObservableCollection<GameModeLogRow> LogRows { get; } = new();
    public ObservableCollection<GameModeListRow> ListItems { get; } = new();

    public bool HasGames => _engine.Games.Count > 0;
    public IReadOnlyList<string> LogFilters { get; } = new[] { "All results", "Applied", "Skipped or refused", "Downgraded", "Could not restore" };

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity StatusSeverity { get; set; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial string EnableButtonText { get; set; }

    [ObservableProperty]
    public partial double DebounceMs { get; set; }

    [ObservableProperty]
    public partial bool SuspendBackgroundWake { get; set; }

    [ObservableProperty]
    public partial bool SuspendBackgroundInput { get; set; }

    [ObservableProperty]
    public partial bool BackgroundMode { get; set; }

    [ObservableProperty]
    public partial int LogFilterIndex { get; set; }

    [ObservableProperty]
    public partial string GamePathInput { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddButtonText))]
    [NotifyPropertyChangedFor(nameof(InputPlaceholder))]
    public partial int AddKindIndex { get; set; }

    public string AddButtonText => AddKindIndex == 1 ? "Add to Never touch" : "Add game";

    public string InputPlaceholder => AddKindIndex == 1
        ? "Image name or pattern"
        : "Full path, or an image name such as notepad.exe";

    [ObservableProperty]
    public partial string JournalText { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial string PreviewSummary { get; set; }

    [ObservableProperty]
    public partial string ListFeedback { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity ListFeedbackSeverity { get; set; }

    public bool HasListFeedback => !string.IsNullOrEmpty(ListFeedback);

    public string EngineState => _engine.State.ToString();

    partial void OnEnabledChanged(bool value)
    {
        if (_updating) return;
        if (_engine.Settings.Enabled == value) return;
        _engine.SetEnabled(value);
    }

    partial void OnDebounceMsChanged(double value)
    {
        if (_updating) return;
        _engine.SetDebounce((int)value);
    }

    partial void OnSuspendBackgroundWakeChanged(bool value)
    {
        if (_updating) return;
        if (_engine.Settings.SuspendBackgroundWake == value) return;
        _engine.SetSuspendBackgroundWake(value);
    }

    partial void OnSuspendBackgroundInputChanged(bool value)
    {
        if (_updating) return;
        if (_engine.Settings.SuspendBackgroundInput == value) return;
        _engine.SetSuspendBackgroundInput(value);
    }

    partial void OnBackgroundModeChanged(bool value)
    {
        if (_updating) return;
        if (_engine.Settings.BackgroundMode == value) return;
        _engine.SetBackgroundMode(value);
    }

    partial void OnLogFilterIndexChanged(int value) => ApplyLogFilter();

    private void OnEngineChanged()
    {
        if (_dispatcher.HasThreadAccess) Refresh();
        else _dispatcher.TryEnqueue(Refresh);
    }

    public void Refresh()
    {
        _updating = true;
        try
        {
            StatusText = _engine.StatusText;
            IsActive = _engine.State == GameModeState.Active;
            StatusSeverity = _engine.State switch
            {
                GameModeState.Active => InfoBarSeverity.Success,
                GameModeState.Pending => InfoBarSeverity.Warning,
                GameModeState.Releasing => InfoBarSeverity.Warning,
                _ => InfoBarSeverity.Informational,
            };
            Enabled = _engine.Settings.Enabled;
            EnableButtonText = Enabled ? "Disable Game Mode" : "Enable Game Mode";
            DebounceMs = _engine.Settings.ClampedDebounce;
            SuspendBackgroundWake = _engine.Settings.SuspendBackgroundWake;
            SuspendBackgroundInput = _engine.Settings.SuspendBackgroundInput;
            BackgroundMode = _engine.Settings.BackgroundMode;
            JournalText = _engine.PendingJournalCount == 0
                ? "Nothing is held by Game Mode."
                : $"{_engine.PendingJournalCount} journal entr(ies) held; restore happens on release.";
            Replace(Affected, _engine.Affected);
            RefreshListItems();
            ApplyLogFilter();
            OnPropertyChanged(nameof(EngineState));
        }
        finally
        {
            _updating = false;
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    private void RefreshListItems()
    {
        ListItems.Clear();
        foreach (var game in _engine.Games)
        {
            ListItems.Add(new GameModeListRow
            {
                Name = game.Name,
                Detail = game.Path,
                Game = game,
            });
        }

        foreach (var pattern in _engine.NeverTouch)
        {
            ListItems.Add(new GameModeListRow
            {
                Name = pattern,
                Detail = "Excluded from every Game Mode change",
                Kind = "Never touch",
                IsNeverTouch = true,
                Pattern = pattern,
            });
        }

        OnPropertyChanged(nameof(HasGames));
    }

    private void ApplyLogFilter()
    {
        var rows = _engine.LogRows.AsEnumerable();
        rows = LogFilterIndex switch
        {
            1 => rows.Where(r => r.Result.Contains("Applied", StringComparison.OrdinalIgnoreCase)),
            2 => rows.Where(r => r.Result.Contains("Refused", StringComparison.OrdinalIgnoreCase) || r.Result.Contains("Skipped", StringComparison.OrdinalIgnoreCase)),
            3 => rows.Where(r => r.Result.Contains("Downgraded", StringComparison.OrdinalIgnoreCase)),
            4 => rows.Where(r => r.Result.Contains("Could not restore", StringComparison.OrdinalIgnoreCase)),
            _ => rows,
        };
        Replace(LogRows, rows);
    }

    public void ToggleEnabled()
    {
        _engine.SetEnabled(!_engine.Settings.Enabled);
        Refresh();
    }

    public void ResumeAll()
    {
        _engine.ResumeEverything();
        Refresh();
    }

    public void SaveGames()
    {
        _engine.SaveSettings();
        Refresh();
    }

    public void Preview()
    {
        var rows = _engine.Preview();
        _updating = true;
        try
        {
            Replace(Affected, rows);
            ApplyLogFilter();
            StatusText = rows.Count == 0
                ? "Preview: nothing to plan. Add a game to the list first."
                : $"Preview only: {rows.Count} background process(es) would be planned; nothing was changed.";
        }
        finally
        {
            _updating = false;
        }
        PreviewSummary = StatusText;
    }

    public void SetFeedback(string text)
    {
        PreviewSummary = text;
    }

    public void AddGamePath()
    {
        var result = _engine.AddGamePath(GamePathInput);
        if (result.Success) GamePathInput = string.Empty;
        ShowListFeedback(result);
        Refresh();
    }

    public void AddFocusedApp()
    {
        ShowListFeedback(_engine.AddFocusedApp());
        Refresh();
    }

    public string RestorePopularGamesText => $"Restore popular games ({GameModeGamePresets.Popular.Length})";

    public void AddPopularGames()
    {
        ShowListFeedback(_engine.AddPopularGames());
        Refresh();
    }

    public void AddListEntry()
    {
        if (AddKindIndex == 1)
        {
            var result = _engine.AddNeverTouch(GamePathInput);
            if (result.Success) GamePathInput = string.Empty;
            ShowListFeedback(result);
            Refresh();
            return;
        }

        AddGamePath();
    }

    public void ClearListFeedback() => ListFeedback = string.Empty;

    private void ShowListFeedback(GameModeEditResult result)
    {
        ListFeedbackSeverity = result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        ListFeedback = result.Message;
        global::kaliteConfig.App.Notify("Game Mode", result.Message,
            result.Success
                ? global::kaliteConfig.Services.Notifications.AppNoticeSeverity.Success
                : global::kaliteConfig.Services.Notifications.AppNoticeSeverity.Warning);
    }

    partial void OnListFeedbackChanged(string value) => OnPropertyChanged(nameof(HasListFeedback));

    public void RemoveListItem(GameModeListRow row)
    {
        if (row.IsNeverTouch && row.Pattern is not null) _engine.RemoveNeverTouch(row.Pattern);
        else if (row.Game is not null) _engine.RemoveGame(row.Game.Name, row.Game.Path);
        Refresh();
    }

    public void ClearLog()
    {
        _engine.ClearLog();
        Refresh();
    }

    public void CopyLog()
    {
        var builder = new StringBuilder();
        foreach (var row in LogRows)
            builder.AppendLine($"{row.Time}\t{row.Process}\t{row.Action}\t{row.Result}\t{row.Message}");
        try
        {
            var package = new DataPackage();
            package.SetText(builder.ToString());
            Clipboard.SetContent(package);
            PreviewSummary = $"Copied {LogRows.Count} log row(s) to the clipboard.";
        }
        catch (Exception ex)
        {
            PreviewSummary = "Could not copy the log: " + ex.Message;
        }
    }

    public void ExportLog()
    {
        try
        {
            var path = System.IO.Path.Combine(GameModeSettingsStore.RootDirectory, $"game-mode-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            System.IO.Directory.CreateDirectory(GameModeSettingsStore.RootDirectory);
            var builder = new StringBuilder();
            foreach (var row in _engine.LogRows)
                builder.AppendLine($"{row.Time}\t{row.Process}\t{row.Action}\t{row.Result}\t{row.Message}");
            System.IO.File.WriteAllText(path, builder.ToString());
            PreviewSummary = "Exported the log to " + path;
        }
        catch (Exception ex)
        {
            PreviewSummary = "Could not export the log: " + ex.Message;
        }
    }
}
