using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using kaliteConfig.Models;
using kaliteConfig.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace kaliteConfig.Controls;

public sealed class LiveThreadChoice
{
    public int Pid { get; init; }
    public int Tid { get; init; }
    public string Description { get; init; } = string.Empty;
    public string StartAddress { get; init; } = string.Empty;

    public bool IsNamed =>
        !string.IsNullOrWhiteSpace(Description) && Description != "(unnamed)";

    public string TidText => $"tid {Tid}";

    public string Label => IsNamed
        ? $"{Description}  ·  pid {Pid} · tid {Tid}"
        : $"(unnamed) start {StartAddress}  ·  pid {Pid} · tid {Tid}";

    public override string ToString() => Label;
}

public sealed partial class RuleEditorDialog : ContentDialog, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    public TunerProfile Draft { get; } = new();

    private bool _priorityEnabled;
    public bool PriorityEnabled
    {
        get => _priorityEnabled;
        set { if (Set(ref _priorityEnabled, value)) UpdateSummary(); }
    }

    private string _priorityName = "Normal";
    public string PriorityName
    {
        get => _priorityName;
        set => Set(ref _priorityName, value);
    }

    private bool _boostEnabled;
    public bool BoostEnabled
    {
        get => _boostEnabled;
        set { if (Set(ref _boostEnabled, value)) UpdateSummary(); }
    }

    private string _boostName = "Enabled";
    public string BoostName
    {
        get => _boostName;
        set => Set(ref _boostName, value);
    }

    private bool _efficiencyEnabled;
    public bool EfficiencyEnabled
    {
        get => _efficiencyEnabled;
        set { if (Set(ref _efficiencyEnabled, value)) UpdateSummary(); }
    }

    private string _efficiencyName = "Disabled";
    public string EfficiencyName
    {
        get => _efficiencyName;
        set => Set(ref _efficiencyName, value);
    }

    private bool _affinityEnabled;
    public bool AffinityEnabled
    {
        get => _affinityEnabled;
        set { if (Set(ref _affinityEnabled, value)) UpdateSummary(); }
    }

    private bool _cpuSetsEnabled;
    public bool CpuSetsEnabled
    {
        get => _cpuSetsEnabled;
        set { if (Set(ref _cpuSetsEnabled, value)) UpdateSummary(); }
    }

    public List<string> ProcessPriorityNames { get; } = new() { "Low (Idle)", "BelowNormal", "Normal", "AboveNormal", "High", "Realtime" };
    public List<string> ToggleNames { get; } = new() { "Enabled", "Disabled" };

    private ulong _pendingAffinityMask;
    private List<ulong> _pendingCpuSetIds = new();
    private int _cpuCount;

    private TunerThreadRule? _selectedRule;
    private bool _loadingRuleEditor = true;
    // Process-setting combos fire SelectionChanged while LoadFrom pushes the
    // saved values into the bindings. This flag is cleared once the dialog is
    // actually open, so a user picking a priority (without touching a thread
    // rule first) enables that process setting instead of being ignored.
    private bool _suppressProcessComboEvents = true;
    private const int LiveThreadLimit = 400;
    private const int MaxTargetProcesses = 12;
    private int _liveThreadLoadToken;
    private int _liveTargetToken;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _liveThreadDebounce;
    private int _ruleIdealCpu = -1;
    private const int RuleCpuBoxColumns = 8;

    private TaskCompletionSource<ContentDialogResult>? _sessionResult;

    public RuleEditorDialog()
    {
        this.InitializeComponent();
        kaliteConfig.Services.Theming.ThemeHelper.ApplyTheme(this);
        try
        {
            _cpuCount = App.Current.ProcessTuning.LogicalProcessorCount();
        }
        catch
        {
            _cpuCount = Environment.ProcessorCount;
        }
        ulong all = _cpuCount >= 64 ? ulong.MaxValue : ((1UL << _cpuCount) - 1UL);
        _pendingAffinityMask = all;
        Draft.ThreadRules.CollectionChanged += (_, _) =>
        {
            UpdateSummary();
            RefreshThreadRulesView();
        };
        UpdateSummary();
        Opened += (_, _) =>
        {
            // Bindings have pushed the saved rule values into the combos by now;
            // from this point on a combo change means the user chose that setting.
            _suppressProcessComboEvents = false;
            ApplyCompactCommandButtons();
        };
    }

    public Task<ContentDialogResult> ShowRuleEditorAsync()
    {
        if (_sessionResult != null)
            return _sessionResult.Task;

        _sessionResult = new TaskCompletionSource<ContentDialogResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _ = ShowSessionAsync();
        return _sessionResult.Task;
    }

    private async Task ShowSessionAsync()
    {
        ContentDialogResult result;
        try
        {
            result = await ShowAsync();
        }
        catch
        {
            result = ContentDialogResult.None;
        }
        _sessionResult?.TrySetResult(result);
        _sessionResult = null;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        ApplyCompactCommandButtons();
    }

    private void ApplyCompactCommandButtons()
    {
        if (GetTemplateChild("CommandSpace") is not Grid commandSpace)
            return;

        commandSpace.HorizontalAlignment = HorizontalAlignment.Right;
        commandSpace.Margin = new Thickness(0, 0, 24, 0);
        if (commandSpace.ColumnDefinitions.Count >= 5)
        {
            commandSpace.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Auto);
            commandSpace.ColumnDefinitions[4].Width = new GridLength(1, GridUnitType.Auto);
        }
        if (GetTemplateChild("PrimaryButton") is Button primary)
        {
            primary.Width = 140;
            primary.Margin = new Thickness(0, 0, 8, 0);
        }
        if (GetTemplateChild("CloseButton") is Button close)
        {
            close.Width = 140;
            close.Margin = new Thickness(0);
        }
    }

    public void LoadFrom(TunerProfile source, bool isNew)
    {
        Title = isNew ? "New rule" : "Edit rule";
        _suppressProcessComboEvents = true;
        Draft.CopyFrom(source);
        PriorityEnabled = Draft.PriorityClass.HasValue;
        PriorityName = Draft.PriorityClass.HasValue ? PriorityClassToName(Draft.PriorityClass.Value) : "Normal";
        BoostEnabled = Draft.BoostEnabled.HasValue;
        BoostName = Draft.BoostEnabled.HasValue && !Draft.BoostEnabled.Value ? "Disabled" : "Enabled";
        EfficiencyEnabled = Draft.EfficiencyMode.HasValue;
        EfficiencyName = Draft.EfficiencyMode.HasValue && Draft.EfficiencyMode.Value ? "Enabled" : "Disabled";
        AffinityEnabled = Draft.AffinityMask.HasValue;
        CpuSetsEnabled = Draft.CpuSetIds != null && Draft.CpuSetIds.Count > 0;
        _pendingAffinityMask = Draft.AffinityMask ?? AllMask();
        _pendingCpuSetIds = Draft.CpuSetIds == null ? new List<ulong>() : new List<ulong>(Draft.CpuSetIds);
        UpdateAffinitySummary();
        UpdateCpuSetsSummary();
        ShowTab("Settings");
        HideError();
        UpdateSummary();
        _selectedRule = null;
        _ruleIdealCpu = -1;
        RefreshThreadRulesView();
        ScheduleLiveThreadReload();
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            ShowTab(tag);
        }
    }

    private void ShowTab(string tag)
    {
        bool settings = tag != "Threads";
        SettingsPanel.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        ThreadsPanel.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        TabSettings.FontWeight = settings ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
        TabThreads.FontWeight = settings ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.Bold;
        if (!settings)
        {
            RefreshThreadRulesView();
            _ = LoadLiveThreadsAsync();
        }
        else
        {
            ScheduleLiveThreadReload();
        }
    }

    private void ScheduleLiveThreadReload()
    {
        if (ThreadsPanel is null) return;
        if (_liveThreadDebounce is null)
        {
            _liveThreadDebounce = DispatcherQueue.CreateTimer();
            _liveThreadDebounce.Interval = System.TimeSpan.FromMilliseconds(350);
            _liveThreadDebounce.IsRepeating = false;
            _liveThreadDebounce.Tick += (_, _) => _ = RefreshLiveTargetAsync();
        }
        _liveThreadDebounce.Stop();
        _liveThreadDebounce.Start();
    }

    private async Task RefreshLiveTargetAsync()
    {
        if (ThreadsPanel is not null && ThreadsPanel.Visibility == Visibility.Visible)
        {
            await LoadLiveThreadsAsync();
            return;
        }
        await LoadLiveTargetStatusAsync();
    }

    private async Task LoadLiveTargetStatusAsync()
    {
        int token = ++_liveTargetToken;
        string pattern = (TargetBox?.Text ?? string.Empty).Trim();
        if (!TryGetTargetProcessName(pattern, out _))
        {
            ApplyTargetStatus(null, 0);
            return;
        }

        var (pids, threads) = await Task.Run(() =>
        {
            var found = FindTargetPids(pattern);
            int threadCount = 0;
            foreach (int pid in found.Take(MaxTargetProcesses))
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    threadCount += proc.Threads.Count;
                }
                catch
                {
                }
            }
            return (found, threadCount);
        });

        if (token != _liveTargetToken) return;
        ApplyTargetStatus(pids, threads);
    }

    private void ApplyTargetStatus(IReadOnlyList<int>? pids, int threadCount)
    {
        if (LiveTargetStatusText is null) return;

        bool threadsVisible = ThreadsPanel is not null && ThreadsPanel.Visibility == Visibility.Visible;
        if (pids is null)
        {
            LiveTargetStatusText.Text = "Tab picks the top match · Enter picks the highlighted one";
            LiveTargetStatusText.Foreground = ResourceBrush("TextFillColorSecondaryBrush");
            ShowLiveThreadsButton.Visibility = Visibility.Collapsed;
            return;
        }

        if (pids.Count == 0)
        {
            LiveTargetStatusText.Text = "That process is not running right now - the rule is saved and applies when it starts";
            LiveTargetStatusText.Foreground = ResourceBrush("TextFillColorSecondaryBrush");
            ShowLiveThreadsButton.Visibility = Visibility.Collapsed;
            return;
        }

        string instances = pids.Count == 1 ? $"pid {pids[0]}" : $"{pids.Count} instances";
        LiveTargetStatusText.Text = threadCount > 0
            ? $"Running now · {instances} · {threadCount} thread{(threadCount == 1 ? string.Empty : "s")}" +
              (threadsVisible ? string.Empty : " - open Thread rules to pick one")
            : $"Running now · {instances} - its threads could not be read";
        LiveTargetStatusText.Foreground = ResourceBrush("SystemFillColorSuccessBrush");
        ShowLiveThreadsButton.Visibility = threadCount > 0 && !threadsVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static Microsoft.UI.Xaml.Media.Brush ResourceBrush(string key)
    {
        try
        {
            return (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[key];
        }
        catch
        {
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
        }
    }

    private void ShowLiveThreads_Click(object sender, RoutedEventArgs e) => ShowTab("Threads");

    private static readonly List<string> NoSuggestions = new();
    private static List<string>? _processNameCache;
    private static DateTime _processNameCacheAtUtc;
    private int _suggestionToken;

    private void TargetBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ScheduleLiveThreadReload();
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _ = RefreshSuggestionsAsync(sender);
    }

    private async Task RefreshSuggestionsAsync(AutoSuggestBox box)
    {
        int token = ++_suggestionToken;
        string query = LastTargetToken(box.Text);
        if (!HasSearchableText(query))
        {
            box.ItemsSource = NoSuggestions;
            return;
        }

        List<string> names;
        try
        {
            names = await GetProcessNamesAsync();
        }
        catch
        {
            return;
        }
        if (token != _suggestionToken || !ReferenceEquals(box, TargetBox)) return;
        box.ItemsSource = RankSuggestions(names, query);
    }

    private static bool HasSearchableText(string query)
    {
        foreach (char c in query)
        {
            if (!char.IsWhiteSpace(c) && c != '*' && c != '?') return true;
        }
        return false;
    }

    private static string LastTargetToken(string? text)
    {
        string s = text ?? string.Empty;
        int cut = s.LastIndexOfAny(new[] { ',', ';' });
        if (cut >= 0) s = s[(cut + 1)..];
        return s.Trim();
    }

    private static string WithTargetToken(string? text, string pick)
    {
        string s = text ?? string.Empty;
        int cut = s.LastIndexOfAny(new[] { ',', ';' });
        string prefix = cut >= 0 ? s[..(cut + 1)].TrimEnd() + " " : string.Empty;
        return (prefix + pick).Trim();
    }

    private static async Task<List<string>> GetProcessNamesAsync()
    {
        var cached = _processNameCache;
        if (cached is not null && DateTime.UtcNow - _processNameCacheAtUtc < TimeSpan.FromSeconds(2))
            return cached;

        var names = await Task.Run(() =>
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (!string.IsNullOrEmpty(process.ProcessName)) set.Add(process.ProcessName);
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
            return set.ToList();
        });

        _processNameCache = names;
        _processNameCacheAtUtc = DateTime.UtcNow;
        return names;
    }

    private static List<string> RankSuggestions(List<string> names, string query)
    {
        bool wildcard = query.IndexOfAny(new[] { '*', '?' }) >= 0;
        var exact = new List<string>();
        var prefix = new List<string>();
        var rest = new List<string>();

        foreach (var name in names)
        {
            bool hit = wildcard
                ? WildcardMatch(name, query)
                : name.Contains(query, StringComparison.OrdinalIgnoreCase);
            if (!hit) continue;

            if (name.Equals(query, StringComparison.OrdinalIgnoreCase)) exact.Add(name);
            else if (!wildcard && name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) prefix.Add(name);
            else rest.Add(name);
        }

        var ranked = exact.Concat(prefix).Concat(rest).Take(12).ToList();
        return ranked.Count > 0 ? ranked : NoSuggestions;
    }

    private void TargetBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string s)
        {
            sender.Text = WithTargetToken(sender.Text, s);
            if (ThreadsPanel.Visibility == Visibility.Visible)
            {
                _liveThreadDebounce?.Stop();
                _ = LoadLiveThreadsAsync();
            }
        }
    }

    private void TargetBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Tab) return;
        if (TargetBox.ItemsSource is not System.Collections.IEnumerable items) return;

        string? pick = items.Cast<object>().OfType<string>().FirstOrDefault();
        if (string.IsNullOrEmpty(pick)) return;

        e.Handled = true;
        TargetBox.Text = WithTargetToken(TargetBox.Text, pick);
        TargetBox.IsSuggestionListOpen = false;
    }

    private void ThreadRulesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectRule(ThreadRulesList.SelectedItem as TunerThreadRule);
    }

    private void SelectRule(TunerThreadRule? rule)
    {
        _selectedRule = rule;
        _ruleIdealCpu = -1;
        RuleEditorStatus(null);
        bool has = rule != null;
        RuleNoSelectionText.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        RuleEditorPanel.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (rule == null) return;
        _loadingRuleEditor = true;
        try
        {
            RuleEditorTitle.Text = rule.TargetText;
            RuleEditorSub.Text = string.IsNullOrWhiteSpace(rule.StartAddress)
                ? (rule.MatchAllThreads ? "Matches all threads" : "Matches any thread")
                : rule.StartAddress;
            RulePriorityCurrent.Text = "Current: " + rule.PriorityText;
            SelectComboByTag(RulePriorityCombo, rule.Priority ?? int.MinValue);
            SelectMemoryCombo(RuleMemoryCombo, rule.MemoryPriority);
            RuleMemoryCurrent.Text = rule.MemoryPriority is uint memLevel
                ? "Current: " + DescribeMemoryPriority(memLevel)
                : "Current: system default";
            SelectMemoryCombo(RuleIoPriorityCombo, rule.IoPriority);
            RuleIoPriorityCurrent.Text = rule.IoPriority is uint ioLevel
                ? "Current: " + DescribeIoPriority(ioLevel)
                : "Current: system default";
            RuleEcoToggle.IsOn = rule.EfficiencyMode ?? false;
            RuleAffinityCurrent.Text = "Current: " + DescribeRuleAffinity(rule.AffinityMask);
            BuildRuleAffinityBoxes(rule.AffinityMask ?? AllMask());
            _ruleIdealCpu = -1;
            if (rule.IdealGroup.HasValue && rule.IdealIndex.HasValue && rule.IdealGroup.Value == 0)
            {
                _ruleIdealCpu = rule.IdealIndex.Value;
                RuleIdealCurrent.Text = $"Current: group 0 CPU {_ruleIdealCpu}";
            }
            else if (rule.IdealGroup.HasValue || rule.IdealIndex.HasValue)
            {
                RuleIdealCurrent.Text = $"Current: group {rule.IdealGroup?.ToString() ?? "?"} CPU {rule.IdealIndex?.ToString() ?? "?"} - only processor group 0 is tuneable here, pick a CPU below.";
            }
            else
            {
                RuleIdealCurrent.Text = "Current: Windows chooses (no preference) - pick a CPU below.";
            }
            BuildRuleIdealBoxes();
            foreach (var box in RuleIdealCpuGrid.Children.OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>())
                box.IsChecked = _ruleIdealCpu >= 0 && box.Tag is int t && t == _ruleIdealCpu;
        }
        finally { _loadingRuleEditor = false; }
    }

    private void RefreshThreadRulesView()
    {
        if (!ReferenceEquals(ThreadRulesList.ItemsSource, Draft.ThreadRules))
            ThreadRulesList.ItemsSource = Draft.ThreadRules;
        int n = Draft.ThreadRules.Count;
        ThreadRulesHeaderText.Text = n == 0
            ? "No thread rules yet - pick a live thread above and add a rule for it"
            : n == 1 ? "1 thread rule - select one to tune it" : $"{n} thread rules - select one to tune it";
        if (_selectedRule != null && !Draft.ThreadRules.Contains(_selectedRule))
        {
            ThreadRulesList.SelectedItem = null;
            SelectRule(null);
        }
        else if (_selectedRule != null)
        {
            var keep = _selectedRule;
            SelectRule(null);
            ThreadRulesList.SelectedItem = keep;
            SelectRule(keep);
        }
        if (_selectedRule == null && Draft.ThreadRules.Count > 0)
        {
            var first = Draft.ThreadRules[0];
            ThreadRulesList.SelectedItem = first;
            if (_selectedRule == null) SelectRule(first);
        }
    }

    private void BuildRuleAffinityBoxes(ulong mask)
    {
        RuleAffinityCpuGrid.Children.Clear();
        RuleAffinityCpuGrid.ColumnDefinitions.Clear();
        RuleAffinityCpuGrid.RowDefinitions.Clear();
        int count = Math.Min(_cpuCount, 64);
        for (int c = 0; c < RuleCpuBoxColumns; c++)
            RuleAffinityCpuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int r = 0; r < (count + RuleCpuBoxColumns - 1) / RuleCpuBoxColumns; r++)
            RuleAffinityCpuGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < count; i++)
        {
            var box = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton
            {
                Content = i.ToString(),
                Width = 44,
                IsChecked = (mask & (1UL << i)) != 0,
                Tag = i,
            };
            Grid.SetColumn(box, i % RuleCpuBoxColumns);
            Grid.SetRow(box, i / RuleCpuBoxColumns);
            RuleAffinityCpuGrid.Children.Add(box);
        }
    }

    private void BuildRuleIdealBoxes()
    {
        RuleIdealCpuGrid.Children.Clear();
        RuleIdealCpuGrid.ColumnDefinitions.Clear();
        RuleIdealCpuGrid.RowDefinitions.Clear();
        int count = Math.Min(_cpuCount, 64);
        for (int c = 0; c < RuleCpuBoxColumns; c++)
            RuleIdealCpuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int r = 0; r < (count + RuleCpuBoxColumns - 1) / RuleCpuBoxColumns; r++)
            RuleIdealCpuGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < count; i++)
        {
            int cpu = i;
            var box = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton
            {
                Content = cpu.ToString(),
                Width = 44,
                Tag = cpu,
            };
            box.Click += (_, _) =>
            {
                _ruleIdealCpu = cpu;
                foreach (var other in RuleIdealCpuGrid.Children.OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>())
                    other.IsChecked = ReferenceEquals(other, box);
            };
            Grid.SetColumn(box, cpu % RuleCpuBoxColumns);
            Grid.SetRow(box, cpu / RuleCpuBoxColumns);
            RuleIdealCpuGrid.Children.Add(box);
        }
    }

    private ulong ReadRuleAffinityMask()
    {
        ulong mask = 0;
        foreach (var box in RuleAffinityCpuGrid.Children.OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>())
        {
            if (box.IsChecked == true && box.Tag is int cpu && cpu < 64)
                mask |= 1UL << cpu;
        }
        return mask;
    }

    private static void SelectComboByTag(ComboBox box, int level)
    {
        box.SelectedItem = null;
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string s && int.TryParse(s, out int v) && v == level)
            {
                box.SelectedItem = item;
                return;
            }
        }
    }

    private static int? ComboLevel(ComboBox box)
    {
        if (box.SelectedItem is ComboBoxItem item && item.Tag is string s && int.TryParse(s, out int v))
            return v;
        return null;
    }

    private static void SelectMemoryCombo(ComboBox box, uint? level)
    {
        box.SelectedItem = null;
        string wanted = level.HasValue ? level.Value.ToString() : "default";
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag && tag == wanted)
            {
                box.SelectedItem = item;
                return;
            }
        }
    }

    private static string DescribeMemoryPriority(uint level) => level switch
    {
        1 => "Very low (1)",
        2 => "Low (2)",
        3 => "Medium (3)",
        4 => "Below normal (4)",
        5 => "Normal (5)",
        _ => $"Level {level}",
    };

    private void RuleMemoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingRuleEditor || _selectedRule == null) return;
        string? tag = (RuleMemoryCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        uint? level = tag is not null && uint.TryParse(tag, out uint parsed) && parsed is >= 1 and <= 5
            ? parsed
            : null;
        _selectedRule.MemoryPriority = level;
        RuleMemoryCurrent.Text = level is uint m
            ? "Current: " + DescribeMemoryPriority(m)
            : "Current: system default";
        Draft.RefreshSummaries();
        UpdateSummary();
        RuleEditorStatus(null);
    }

    private static string DescribeIoPriority(uint level) => level switch
    {
        0 => "Very low",
        1 => "Low",
        2 => "Normal",
        3 => "High",
        _ => $"Level {level}",
    };

    private void RuleIoPriorityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingRuleEditor || _selectedRule == null) return;
        string? tag = (RuleIoPriorityCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        uint? level = tag is not null && uint.TryParse(tag, out uint parsed) && parsed <= 3
            ? parsed
            : null;
        _selectedRule.IoPriority = level;
        RuleIoPriorityCurrent.Text = level is uint io
            ? "Current: " + DescribeIoPriority(io)
            : "Current: system default";
        Draft.RefreshSummaries();
        UpdateSummary();
        RuleEditorStatus(null);
    }

    private void ProcessRowCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProcessComboEvents) return;
        if (ReferenceEquals(sender, ProcessPriorityCombo)) PriorityEnabled = true;
        else if (ReferenceEquals(sender, ProcessBoostCombo)) BoostEnabled = true;
        else if (ReferenceEquals(sender, ProcessEfficiencyCombo)) EfficiencyEnabled = true;
    }

    private string DescribeRuleAffinity(ulong? mask)
    {
        if (!mask.HasValue) return "All logical processors";
        ulong m = mask.Value;
        if (m == AllMask()) return "All logical processors";
        var cpus = new List<string>();
        for (int i = 0; i < _cpuCount && i < 64; i++)
            if ((m & (1UL << i)) != 0) cpus.Add(i.ToString());
        return cpus.Count == 0 ? "None" : $"Group 0: CPU {string.Join(", ", cpus)} (0x{m:X})";
    }

    private void RulePriorityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingRuleEditor || _selectedRule == null) return;
        var level = ComboLevel(RulePriorityCombo);
        if (level == null) return;
        _selectedRule.Priority = level.Value;
        RulePriorityCurrent.Text = "Current: " + _selectedRule.PriorityText;
        Draft.RefreshSummaries();
        UpdateSummary();
        RuleEditorStatus(null);
    }

    private void RuleEcoToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingRuleEditor || _selectedRule == null) return;
        _selectedRule.EfficiencyMode = RuleEcoToggle.IsOn;
        Draft.RefreshSummaries();
        UpdateSummary();
        RuleEditorStatus(null);
    }

    private void RuleAffinityApply_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRule == null) return;
        ulong mask = ReadRuleAffinityMask();
        if (mask == 0)
        {
            RuleEditorStatus("Affinity: tick at least one CPU.", true);
            return;
        }
        if (mask == AllMask())
        {
            _selectedRule.AffinityMask = null;
            _selectedRule.AffinityGroup = null;
        }
        else
        {
            _selectedRule.AffinityGroup = 0;
            _selectedRule.AffinityMask = mask;
        }
        RuleAffinityCurrent.Text = "Current: " + DescribeRuleAffinity(_selectedRule.AffinityMask);
        Draft.RefreshSummaries();
        UpdateSummary();
        RuleEditorStatus(null);
    }

    private void RuleIdealApply_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRule == null) return;
        if (_ruleIdealCpu < 0)
        {
            RuleEditorStatus("Ideal processor: pick one CPU box first.", true);
            return;
        }
        _selectedRule.IdealGroup = 0;
        _selectedRule.IdealIndex = (byte)_ruleIdealCpu;
        RuleIdealCurrent.Text = $"Current: group 0 CPU {_ruleIdealCpu}";
        Draft.RefreshSummaries();
        UpdateSummary();
        RuleEditorStatus(null);
    }

    private void ThreadRule_DeleteFlyout_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TunerThreadRule rule)
        {
            Draft.ThreadRules.Remove(rule);
            Draft.RefreshSummaries();
            UpdateSummary();
        }
    }

    private void AddAllThreadsRule_Click(object sender, RoutedEventArgs e)
    {
        var existing = Draft.ThreadRules.FirstOrDefault(r => r.MatchAllThreads);
        if (existing != null)
        {
            ThreadRulesList.SelectedItem = existing;
            SelectRule(existing);
            RuleEditorStatus("This process already has a rule that matches every thread.", true);
            return;
        }

        var rule = new TunerThreadRule { Description = string.Empty, MatchAllThreads = true };
        Draft.ThreadRules.Add(rule);
        Draft.RefreshSummaries();
        UpdateSummary();
        ThreadRulesList.SelectedItem = rule;
        SelectRule(rule);
        RuleEditorStatus("Added - it matches every thread of this process. Pick a priority on the right, or tick boost in the rule row.");
    }

    private void ThreadRule_DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var rule = _selectedRule
            ?? (sender as FrameworkElement)?.DataContext as TunerThreadRule
            ?? ThreadRulesList.SelectedItem as TunerThreadRule;
        if (rule == null)
        {
            RuleEditorStatus("Select a thread rule first.", true);
            return;
        }
        Draft.ThreadRules.Remove(rule);
        Draft.RefreshSummaries();
        UpdateSummary();
    }

    private void LiveThreadsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AddPickedThreadButton.IsEnabled = LiveThreadsList.SelectedItem is LiveThreadChoice;
    }

    private void LiveThreadsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (LiveThreadsList.SelectedItem is LiveThreadChoice)
            AddPickedThread_Click(sender, e);
    }

    private async void LiveThreadRefresh_Click(object sender, RoutedEventArgs e)
        => await LoadLiveThreadsAsync();

    private void AddPickedThread_Click(object sender, RoutedEventArgs e)
    {
        if (LiveThreadsList.SelectedItem is not LiveThreadChoice pick)
        {
            RuleEditorStatus("Pick a live thread first.", true);
            return;
        }

        var already = Draft.ThreadRules.FirstOrDefault(r => SameThreadTarget(r, pick));
        if (already != null)
        {
            ThreadRulesList.SelectedItem = already;
            SelectRule(already);
            RuleEditorStatus($"\"{pick.Label}\" is already a rule for this process.", true);
            return;
        }

        var rule = pick.IsNamed
            ? new TunerThreadRule { Description = pick.Description, StartAddress = string.Empty, MatchAllThreads = false }
            : new TunerThreadRule { Description = string.Empty, StartAddress = pick.StartAddress, MatchAllThreads = false };
        Draft.ThreadRules.Add(rule);
        Draft.RefreshSummaries();
        UpdateSummary();
        ThreadRulesList.SelectedItem = rule;
        SelectRule(rule);
        RuleEditorStatus($"Added {pick.Label} - pick a priority or toggle boost/Eco on the right.");
    }

    private static bool SameThreadTarget(TunerThreadRule rule, LiveThreadChoice pick)
    {
        if (rule.MatchAllThreads) return false;
        if (!string.IsNullOrWhiteSpace(rule.StartAddress))
            return string.Equals(rule.StartAddress.Trim(), pick.StartAddress.Trim(), StringComparison.OrdinalIgnoreCase);
        return pick.IsNamed
            && string.Equals((rule.Description ?? string.Empty).Trim(), pick.Description.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadLiveThreadsAsync()
    {
        int token = ++_liveThreadLoadToken;
        LiveThreadsList.IsEnabled = false;
        LiveThreadRefreshButton.IsEnabled = false;
        AddPickedThreadButton.IsEnabled = false;
        try
        {
            string pattern = (TargetBox?.Text ?? Draft.Pattern ?? string.Empty).Trim();
            bool hasTarget = TryGetTargetProcessName(pattern, out _);
            var pids = FindTargetPids(pattern);
            var items = new List<LiveThreadChoice>();
            int totalThreads = 0;
            foreach (int pid in pids.Take(MaxTargetProcesses))
            {
                if (token != _liveThreadLoadToken) return;
                try
                {
                    var details = await ThreadQueryService.ListThreadsDetailedAsync(pid);
                    totalThreads += details.Count;
                    foreach (var d in details)
                    {
                        items.Add(new LiveThreadChoice
                        {
                            Pid = pid,
                            Tid = d.Tid,
                            Description = d.Description ?? string.Empty,
                            StartAddress = d.StartAddress ?? string.Empty,
                        });
                    }
                }
                catch
                {
                }
                if (items.Count >= LiveThreadLimit) break;
            }
            if (token != _liveThreadLoadToken) return;

            var ordered = items
                .OrderByDescending(i => i.IsNamed)
                .ThenBy(i => i.Description, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(i => i.Pid)
                .ThenBy(i => i.Tid)
                .Take(LiveThreadLimit)
                .ToList();
            LiveThreadsList.ItemsSource = ordered;
            ApplyTargetStatus(pids, totalThreads);
            LiveThreadsHeaderText.Text = ordered.Count > 0
                ? $"Live threads now ({ordered.Count}) - pick one to add a rule"
                : !hasTarget
                    ? "Type the target process above to list its threads"
                    : pids.Count == 0
                        ? "That process is not running right now"
                        : "That process has no threads to show";
        }
        finally
        {
            if (token == _liveThreadLoadToken)
            {
                LiveThreadsList.IsEnabled = true;
                LiveThreadRefreshButton.IsEnabled = true;
                AddPickedThreadButton.IsEnabled = LiveThreadsList.SelectedItem is LiveThreadChoice;
            }
        }
    }

    private static bool TryGetTargetProcessName(string pattern, out string name)
    {
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(pattern)) return false;

        string first = pattern.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? pattern;
        name = first.Trim();
        int slash = name.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0) name = name[(slash + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4].Trim();
        if (name.Length == 0 || name.All(c => c == '*'))
        {
            name = string.Empty;
            return false;
        }
        return true;
    }

    private static bool WildcardMatch(string text, string pattern)
    {
        int t = 0, p = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length
                && (pattern[p] == '?'
                    || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t])))
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = t;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++mark;
            }
            else
            {
                return false;
            }
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    private static List<int> FindTargetPids(string pattern)
    {
        var pids = new List<int>();
        if (string.IsNullOrWhiteSpace(pattern)) return pids;

        string first = pattern.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? pattern;
        if (!TryGetTargetProcessName(first, out string name)) return pids;
        bool wildcard = name.Contains('*');

        try
        {
            if (!wildcard)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { pids.Add(p.Id); } catch { } finally { p.Dispose(); }
                }
                return pids;
            }

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (WildcardMatch(p.ProcessName, name))
                        pids.Add(p.Id);
                }
                catch
                {
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
        }
        return pids;
    }

    private void RuleEditorStatus(string? message)
    {
        RuleEditorStatus(message, false);
    }

    private void RuleEditorStatus(string? message, bool warn)
    {
        try
        {
            bool show = !string.IsNullOrEmpty(message);
            ThreadRulesStatusText.Text = message ?? string.Empty;
            ThreadRulesStatusText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show)
            {
                ThreadRulesStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                    warn ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush"];
            }
        }
        catch { }

        if (string.IsNullOrEmpty(message))
        {
            RuleEditorStatusBox.Visibility = Visibility.Collapsed;
            RuleEditorStatusBox.Text = string.Empty;
        }
        else
        {
            RuleEditorStatusBox.Text = message;
            RuleEditorStatusBox.Visibility = Visibility.Visible;
        }
    }

    private ulong AllMask() => _cpuCount >= 64 ? ulong.MaxValue : ((1UL << _cpuCount) - 1UL);

    private void AffinityChoose_Click(object sender, RoutedEventArgs e)
    {
        _ = CpuPickerFlyouts.ShowAffinityPickerAsync(
            AffinityChooseButton,
            _cpuCount,
            _pendingAffinityMask,
            mask =>
            {
                _pendingAffinityMask = mask;
                AffinityEnabled = true;
                UpdateAffinitySummary();
                UpdateSummary();
            });
    }

    private void CpuSetsChoose_Click(object sender, RoutedEventArgs e)
    {
        _ = CpuPickerFlyouts.ShowCpuSetsPickerAsync(
            CpuSetsChooseButton,
            _pendingCpuSetIds,
            (ids, total) =>
            {
                _pendingCpuSetIds = ids;
                CpuSetsEnabled = true;
                UpdateCpuSetsSummary(total);
                UpdateSummary();
            });
    }

    private void UpdateAffinitySummary()
    {
        if (_pendingAffinityMask == AllMask())
        {
            DraftAffinitySummaryText.Text = "All logical processors";
        }
        else
        {
            int n = 0;
            for (int i = 0; i < _cpuCount && i < 64; i++)
            {
                if ((_pendingAffinityMask & (1UL << i)) != 0) n++;
            }
            DraftAffinitySummaryText.Text = $"{n} of {_cpuCount} · 0x{_pendingAffinityMask:X}";
        }
    }

    private void UpdateCpuSetsSummary(int total = -1)
    {
        if (_pendingCpuSetIds.Count == 0 || (total >= 0 && _pendingCpuSetIds.Count >= total))
        {
            CpuSetsSummaryText.Text = "All logical processors";
        }
        else
        {
            CpuSetsSummaryText.Text = $"{_pendingCpuSetIds.Count} selected";
        }
    }

    private int EnabledActionCount =>
        (PriorityEnabled ? 1 : 0) +
        (BoostEnabled ? 1 : 0) +
        (EfficiencyEnabled ? 1 : 0) +
        (AffinityEnabled ? 1 : 0) +
        (CpuSetsEnabled ? 1 : 0);

    private void UpdateSummary()
    {
        int a = EnabledActionCount;
        int t = Draft.ThreadRules.Count;
        SummaryText.Text = $"{a} process setting{(a == 1 ? "" : "s")} · {t} thread rule{(t == 1 ? "" : "s")}";
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;
    }

    private static uint PriorityNameToValue(string name) => name switch
    {
        "Low (Idle)" => 0x40,
        "Low" => 0x40,
        "Idle" => 0x40,

        "BelowNormal" => 0x4000,
        "Normal" => 0x20,
        "AboveNormal" => 0x8000,
        "High" => 0x80,
        "Realtime" => 0x100,
        _ => 0x20,
    };

    private static string PriorityClassToName(uint value) => value switch
    {
        0x40 => "Low (Idle)",
        0x4000 => "BelowNormal",
        0x20 => "Normal",
        0x8000 => "AboveNormal",
        0x80 => "High",
        0x100 => "Realtime",
        _ => "Normal",
    };

    public bool ShortcutAccepted { get; private set; }

    private void Dialog_PrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!TryCommit())
        {
            args.Cancel = true;
        }
    }

    private static string NormalizeTarget(string value)
    {
        string v = (value ?? string.Empty).Trim().Trim('"');
        int slash = v.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0) v = v[(slash + 1)..];
        if (v.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) v = v[..^4];
        return v.Trim();
    }

    private static string NormalizePattern(string target)
    {
        var tokens = target
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeTarget)
            .Where(t => t.Length > 0)
            .ToList();
        return tokens.Count > 0 ? string.Join(", ", tokens) : target.Trim();
    }

    private static string DeriveRuleName(string target)
    {
        string first = target
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeTarget)
            .FirstOrDefault(t => t.Length > 0) ?? string.Empty;
        first = first.Trim('*').Trim();
        return first.Length > 0 ? first : NormalizeTarget(target);
    }

    private bool TryCommit()
    {
        HideError();
        string target = (TargetBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(target))
        {
            ShowError("Enter a target process name or pattern.");
            return false;
        }
        if (EnabledActionCount == 0 && !Draft.GamingModeAuto && Draft.ThreadRules.Count == 0)
        {
            ShowError("Enable at least one process setting or add one thread rule.");
            return false;
        }

        Draft.Pattern = NormalizePattern(target);
        Draft.Name = DeriveRuleName(target);
        Draft.PriorityClass = PriorityEnabled ? PriorityNameToValue(PriorityName) : null;
        Draft.BoostEnabled = BoostEnabled ? BoostName == "Enabled" : null;
        Draft.EfficiencyMode = EfficiencyEnabled ? EfficiencyName == "Enabled" : null;
        Draft.AffinityMask = AffinityEnabled ? _pendingAffinityMask : null;
        Draft.CpuSetIds = CpuSetsEnabled && _pendingCpuSetIds.Count > 0 ? new List<ulong>(_pendingCpuSetIds) : null;
        Draft.RefreshSummaries();
        return true;
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter
            && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            if (TryCommit())
            {
                ShortcutAccepted = true;
                Hide();
            }
            e.Handled = true;
        }
    }
}
