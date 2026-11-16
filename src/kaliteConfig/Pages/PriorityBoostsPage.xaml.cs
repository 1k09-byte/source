using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using kaliteConfig.Models;
using kaliteConfig.Controls;
using kaliteConfig.ViewModels;

namespace kaliteConfig.Pages
{
    public sealed partial class PriorityBoostsPage : Page
    {
        public ThreadTunerViewModel ViewModel { get; }

        public PriorityBoostsPage()
        {
            this.InitializeComponent();
            this.ViewModel = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<ViewModels.ThreadTunerViewModel>(App.Services) ?? new ViewModels.ThreadTunerViewModel();
            this.DataContext = ViewModel;

            this.Loaded += Page_Loaded;
            this.Unloaded += Page_Unloaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (MainSelectorBar.SelectedItem == null)
            {
                MainSelectorBar.SelectedItem = TabThreads;
            }

            ViewModel.ResumePolling();
            ViewModel.SyncProfiles();
            RefreshRulesMeta();
            App.Current.ProfileWatcher.RulesChanged += ProfileWatcher_RulesChanged;
        }

        private void ProfileWatcher_RulesChanged(object? sender, EventArgs e)
        {
            RefreshRulesMeta();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            App.Current.ProfileWatcher.RulesChanged -= ProfileWatcher_RulesChanged;
            try { ViewModel.Shutdown(); } catch { }
        }

        private void MainSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            ViewModel.IsThreadTuneTabActive = sender.SelectedItem != TabRules;

            if (sender.SelectedItem == TabRules)
            {
                ThreadsPanel.Visibility = Visibility.Collapsed;
                RulesPanel.Visibility = Visibility.Visible;
                ViewModel.SyncProfiles();
                RefreshRulesMeta();
            }
            else
            {
                RulesPanel.Visibility = Visibility.Collapsed;
                ThreadsPanel.Visibility = Visibility.Visible;
                _ = ViewModel.LoadThreadBoostRowsAsync();
            }
        }

        private void RefreshRulesMeta()
        {
            int n = ViewModel.DisplayedThreadProfiles.Count;
            RulesCountText.Text = n == 1 ? "1 rule" : $"{n} rules";
        }

        private TunerProfile? GetRuleRow(object sender) =>
            (sender as FrameworkElement)?.DataContext as TunerProfile;

        private TunerProfile? SelectedRuleOrRow(object sender) =>
            ViewModel.SelectedProfile ?? GetRuleRow(sender);

        private async Task OpenRuleEditorAsync(TunerProfile target, bool isNew)
        {
            var dlg = new RuleEditorDialog { XamlRoot = this.XamlRoot };
            dlg.LoadFrom(target, isNew);
            if (await dlg.ShowRuleEditorAsync() == ContentDialogResult.Primary || dlg.ShortcutAccepted)
            {
                target.CopyFrom(dlg.Draft);
                bool saved = await App.Current.ProfileWatcher.AddOrUpdate(target);
                ViewModel.SyncProfiles();
                if (!saved)
                {
                    await ShowRulesErrorAsync("Could not save the rule file. The rule is active for this session only.");
                }
                await App.Current.ProfileWatcher.ApplyProfileNowAsync(target);
                ViewModel.SyncProfiles();
                RefreshRulesMeta();
            }
        }

        private async void RuleAdd_Click(object sender, RoutedEventArgs e) =>
            await OpenRuleEditorAsync(new TunerProfile(), isNew: true);

        private async void RuleExport_Click(object sender, RoutedEventArgs e)
        {
            string? message = await RulesTransferUi.ExportAsync();
            if (message is not null) await ShowRulesErrorAsync(message);
        }

        private async void RuleImport_Click(object sender, RoutedEventArgs e)
        {
            string? message = await RulesTransferUi.ImportAsync();
            if (message is null) return;

            ViewModel.RulesFilter = string.Empty;
            ViewModel.SyncProfiles();
            RefreshRulesMeta();
            await ShowRulesErrorAsync(message);
        }

        private Task ShowRulesErrorAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "Rules",
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "Close",
                XamlRoot = this.XamlRoot,
            };
            return dialog.ShowAsync().AsTask();
        }

        private async void RuleEdit_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRuleOrRow(sender) is { } p)
            {
                await OpenRuleEditorAsync(p, isNew: false);
            }
        }

        private async void RuleToggle_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRuleOrRow(sender) is not { } p) return;
            p.Enabled = !p.Enabled;
            p.RefreshSummaries();
            bool saved = await App.Current.ProfileWatcher.AddOrUpdate(p);
            ViewModel.SyncProfiles();
            if (!saved)
            {
                await ShowRulesErrorAsync("Could not save the rule file. The change is active for this session only.");
            }
            if (p.Enabled)
            {
                await App.Current.ProfileWatcher.ApplyProfileNowAsync(p);
            }
            ViewModel.SyncProfiles();
            RefreshRulesMeta();
        }

        private async void RuleDelete_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRuleOrRow(sender) is not { } p) return;
            bool saved = await App.Current.ProfileWatcher.Remove(p);
            ViewModel.SyncProfiles();
            RefreshRulesMeta();
            if (!saved)
            {
                await ShowRulesErrorAsync("Could not save the rule file. The rule will reappear on restart.");
            }
        }

        private async void RuleApplyNow_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRuleOrRow(sender) is not { } p) return;
            await App.Current.ProfileWatcher.ApplyProfileNowAsync(p);
            ViewModel.SyncProfiles();
            RefreshRulesMeta();
        }

        private void RuleRow_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = true;
            if (GetRuleRow(sender) is { } p)
            {
                _ = OpenRuleEditorAsync(p, isNew: false);
            }
        }

        public static bool NotProtected(bool isProtected) => !isProtected;

        private async void ThreadAction_ToggleBoost(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ThreadBoostRow row && !row.IsBusy)
            {
                row.IsBusy = true;
                bool targetState = !row.BoostEnabled;
                try
                {
                    if (targetState)
                        await Services.BoostPreferenceService.Instance.RestoreAsync(row.ProcessName, row.Tid, row.Description, string.Empty);
                    else
                        await Services.BoostPreferenceService.Instance.SuppressAsync(row.ProcessName, row.Tid, row.Description, string.Empty);

                    bool? actual = null;
                    try { actual = await App.Current.ThreadTuning.GetBoostAsync((uint)row.Tid); } catch { }

                    row.IsBoostKnown = actual.HasValue;
                    if (actual.HasValue) row.BoostEnabled = actual.Value;
                }
                catch (Exception ex)
                {
                    _ = new ContentDialog
                    {
                        Title = "Priority Boost",
                        Content = new TextBlock { Text = $"Could not change boost:\n{ex.Message}", TextWrapping = TextWrapping.Wrap },
                        CloseButtonText = "Close",
                        XamlRoot = this.XamlRoot,
                    }.ShowAsync();
                }
                finally
                {
                    row.IsBusy = false;
                }
            }
        }

        private async void ThreadAction_CreateRule(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not ThreadBoostRow row) return;

            string description = row.Description == "(unnamed)" ? string.Empty : row.Description;
            string address = string.Empty;
            try
            {
                var threads = await Services.ThreadQueryService.ListThreadsAsync(row.Pid);
                address = threads.FirstOrDefault(t => t.Tid == row.Tid)?.StartAddress ?? string.Empty;
                if (address == "-") address = string.Empty;
            }
            catch { }

            var draft = new TunerProfile
            {
                Name = row.ProcessName,
                Pattern = row.ProcessName,
                AutoApply = true,
                Enabled = true
            };

            draft.ThreadRules.Add(new TunerThreadRule
            {
                Description = description,
                StartAddress = address,
                MatchAllThreads = description.Length == 0 && address.Length == 0,
                BoostEnabled = false
            });

            _ = OpenRuleEditorAsync(draft, isNew: true);
        }
    }
}
