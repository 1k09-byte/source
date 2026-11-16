using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using kaliteConfig.Models;
using kaliteConfig.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace kaliteConfig.ViewModels;

public sealed partial class PowerPlansViewModel : ObservableObject
{
    private readonly PowerService _service = new();

    public ObservableCollection<PowerScheme> Schemes { get; } = new();

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value) => ScheduleFilter();

    partial void OnSelectedSchemeChanged(PowerScheme? value) => ApplySettingFilter();

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _filterTimer;

    private void ScheduleFilter()
    {
        try
        {
            if (_uiDispatcher is null) { ApplySettingFilter(); return; }

            _filterTimer?.Stop();
            if (_filterTimer is null)
            {
                _filterTimer = _uiDispatcher.CreateTimer();
                _filterTimer.Interval = TimeSpan.FromMilliseconds(250);
                _filterTimer.IsRepeating = false;
                _filterTimer.Tick += (_, _) => { _filterTimer.Stop(); ApplySettingFilter(); };
            }
            _filterTimer.Start();
        }
        catch
        {
            ApplySettingFilter();
        }
    }

    public void ApplySettingFilter()
    {
        try
        {
            string q = (SearchText ?? string.Empty).Trim();
            PowerScheme? scheme = SelectedScheme;

            scheme?.FilteredSubgroups.Clear();
            if (scheme is not null)
            {
                foreach (PowerSubgroup group in scheme.Subgroups)
                {
                    bool groupMatches = q.Length == 0
                        || group.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || (group.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);

                    group.FilteredSettings.Clear();
                    foreach (PowerSetting setting in group.Settings)
                    {
                        bool match = groupMatches
                            || setting.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                            || (setting.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
                        if (match) group.FilteredSettings.Add(setting);
                    }

                    if (q.Length == 0 || group.FilteredSettings.Count > 0)
                    {
                        scheme.FilteredSubgroups.Add(group);
                        if (q.Length > 0 && !group.IsExpanded) group.IsExpanded = true;
                    }
                }
            }

            OnPropertyChanged(nameof(HasNoSettingMatches));
        }
        catch
        {
        }
    }

    public bool HasNoSettingMatches =>
        !string.IsNullOrWhiteSpace(SearchText)
        && SelectedScheme is not null
        && SelectedScheme.Subgroups.Count > 0
        && SelectedScheme.FilteredSubgroups.Count == 0;

    [ObservableProperty]
    public partial PowerScheme? SelectedScheme { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }
    [ObservableProperty]
    public partial string LoadingStatus { get; set; } = "Loading…";

    public ObservableCollection<KernelTweakItem> KernelTweaks { get; } = new();
    private readonly WindowsSettingsService _kernelService = new();
    private bool _suppressKernelWrite;

    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _uiDispatcher;

    private void ReportStatus(string status)
    {
        if (_uiDispatcher != null)
        {
            _uiDispatcher.TryEnqueue(() => LoadingStatus = status);
        }
        else
        {
            LoadingStatus = status;
        }
    }

    public PowerPlansViewModel()
    {
        try { _uiDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(); }
        catch { _uiDispatcher = null; }
    }

    partial void OnSelectedSchemeChanged(PowerScheme? oldValue, PowerScheme? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= Scheme_PropertyChanged;
            foreach (var sg in oldValue.Subgroups)
                foreach (var st in sg.Settings)
                    st.PropertyChanged -= Setting_PropertyChanged;
        }
        if (newValue != null)
        {
            newValue.PropertyChanged -= Scheme_PropertyChanged;
            newValue.PropertyChanged += Scheme_PropertyChanged;
            foreach (var sg in newValue.Subgroups)
                foreach (var st in sg.Settings)
                {
                    st.PropertyChanged -= Setting_PropertyChanged;
                    st.PropertyChanged += Setting_PropertyChanged;
                }
        }
    }

    private void Scheme_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is PowerScheme scheme)
        {
            if (e.PropertyName == nameof(PowerScheme.Name))
                _service.WritePlanName(scheme.Id, scheme.Name);
            else if (e.PropertyName == nameof(PowerScheme.Description))
                _service.WritePlanDescription(scheme.Id, scheme.Description);
        }
    }

    private void Setting_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not PowerSetting setting || SelectedScheme == null) return;
        try
        {
            var subgroup = SelectedScheme.Subgroups.FirstOrDefault(g => g.Settings.Contains(setting));
            if (subgroup == null) return;
            if (e.PropertyName == nameof(PowerSetting.AcValueIndex))
                _service.WriteACValue(SelectedScheme.Id, subgroup.Id, setting.Id, (uint)setting.AcValueIndex);
            else if (e.PropertyName == nameof(PowerSetting.DcValueIndex))
                _service.WriteDCValue(SelectedScheme.Id, subgroup.Id, setting.Id, (uint)setting.DcValueIndex);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not save \"{setting.Name}\": {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task UnhideAllSettingsAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        LoadingStatus = "Unhiding all power settings…";
        try
        {
            await Task.Run(() => _service.UnhideAllSettings());
            LoadingStatus = "Enumerating power schemes…";
            var schemes = await Task.Run(() => _service.GetAllSchemes(new Progress<string>(s => ReportStatus(s))));
            Schemes.Clear();
            foreach (var s in schemes) Schemes.Add(s);
            ApplySettingFilter();
            if (SelectedScheme == null && Schemes.Count > 0)
                SelectedScheme = Schemes.FirstOrDefault(s => s.IsActive) ?? Schemes.First();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unhide failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task LoadSchemesAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        LoadingStatus = "Loading power schemes…";
        try
        {
            LoadingStatus = "Enumerating power schemes…";
            var status = new Progress<string>(s => ReportStatus(s));
            var schemes = await Task.Run(() => _service.GetAllSchemes(status));
            Schemes.Clear();
            foreach (var s in schemes) Schemes.Add(s);
            ApplySettingFilter();
            
            if (SelectedScheme == null && Schemes.Count > 0)
                SelectedScheme = Schemes.FirstOrDefault(s => s.IsActive) ?? Schemes.First();
            if (Schemes.Count == 0)
                ErrorMessage = "No power schemes found.";
            RefreshKernelTweaks();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to enumerate power schemes: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task<Guid?> CreatePlanAsync(Guid baseId, string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "Custom plan";
        IsLoading = true;
        LoadingStatus = $"Creating \"{name}\"…";
        try
        {
            var newId = await Task.Run(() => _service.DuplicateScheme(baseId));
            await Task.Run(() =>
            {
                _service.WritePlanName(newId, name);
                _service.WritePlanDescription(newId, description ?? string.Empty);
            });
            IsLoading = false;
            await LoadSchemesAsync();
            SelectedScheme = Schemes.FirstOrDefault(s => s.Id == newId);
            return newId;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not create plan: {ex.Message}";
            return null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void RefreshKernelTweaks()
    {
        _suppressKernelWrite = true;
        try
        {
            foreach (var item in KernelTweaks)
                item.PropertyChanged -= KernelTweak_PropertyChanged;
            KernelTweaks.Clear();
            foreach (var def in WindowsSettingsService.All)
            {
                uint? raw = null;
                string loadError = string.Empty;
                try { raw = _kernelService.ReadRaw(def); }
                catch (Exception ex) { loadError = ex.Message; }
                var item = new KernelTweakItem
                {
                    Id = def.Id,
                    Name = def.Name,
                    Description = def.Description + (def.NeedsReboot ? " Takes effect after a restart." : "")
                        + (def.PerActiveScheme ? " Applies to the active power scheme." : ""),
                    IsOn = raw == def.OnValue,
                    StateText = loadError.Length > 0 ? $"Unreadable: {loadError}"
                        : raw == null ? "Not set (Windows default)"
                        : raw == def.OnValue ? "On"
                        : raw == def.OffValue ? "Off"
                        : $"Custom value ({raw})",
                };
                item.PropertyChanged += KernelTweak_PropertyChanged;
                KernelTweaks.Add(item);
            }
        }
        finally { _suppressKernelWrite = false; }
    }

    private void KernelTweak_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suppressKernelWrite) return;
        if (sender is not KernelTweakItem item || e.PropertyName != nameof(KernelTweakItem.IsOn)) return;
        var def = WindowsSettingsService.Find(item.Id);
        if (def == null) return;
        try
        {
            _kernelService.Write(def, item.IsOn);
            item.StateText = item.IsOn ? "On" : "Off";
        }
        catch (Exception ex)
        {
            _suppressKernelWrite = true;
            try { item.IsOn = !item.IsOn; } finally { _suppressKernelWrite = false; }
            item.StateText = $"Failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetKernelTweak(KernelTweakItem? item)
    {
        if (item == null) return;
        var def = WindowsSettingsService.Find(item.Id);
        if (def == null) return;
        try
        {
            _kernelService.Reset(def);
            RefreshKernelTweaks();
        }
        catch (Exception ex)
        {
            item.StateText = $"Failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SetActiveScheme(PowerScheme? scheme)
    {
        if (scheme == null) return;
        try
        {
            _service.SetActiveScheme(scheme.Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not activate plan: {ex.Message}";
            return;
        }
        
        foreach (var s in Schemes)
            s.IsActive = (s == scheme);
        RefreshKernelTweaks();
    }
    
    [RelayCommand]
    private void DeleteScheme(PowerScheme? scheme)
    {
        if (scheme == null || scheme.IsActive) return;

        try
        {
            _service.DeleteScheme(scheme.Id);
            Schemes.Remove(scheme);

            if (SelectedScheme == scheme)
                SelectedScheme = Schemes.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    public async Task RestoreDefaultSchemesAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        LoadingStatus = "Restoring Windows default power plans…";
        try
        {
            await Task.Run(() => PowerService.RestoreDefaultSchemes());
            await LoadSchemesAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Restore failed: {ex.Message} (try running as administrator)";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DuplicateSchemeAsync(PowerScheme? scheme)
    {
        if (scheme == null) return;
        
        try
        {
            var newId = _service.DuplicateScheme(scheme.Id);
            await LoadSchemesAsync();
            SelectedScheme = Schemes.FirstOrDefault(s => s.Id == newId);
        }
        catch { }
    }

    private static nint Hwnd =>
        App.MainWindow is { } window ? WinRT.Interop.WindowNative.GetWindowHandle(window) : 0;

    [RelayCommand]
    private async Task ExportSchemeAsync(PowerScheme? scheme)
    {
        if (scheme == null) return;
        try
        {
            string? path = Win32FilePicker.PickSaveFile(Hwnd, $"{scheme.Name}.pow",
                new[] { ("Power plan (*.pow)", "*.pow") }, "Export power plan", "pow");
            if (path is null) return;
            var (exitCode, output) = await RunPowerCfgAsync($"/export \"{path}\" {scheme.Id}");
            if (exitCode != 0 || !System.IO.File.Exists(path))
            {
                App.Notify("Power plans", "Export failed: " + DescribeFailure(output), Services.Notifications.AppNoticeSeverity.Error);
                return;
            }
            App.Notify("Power plans", $"Exported {scheme.Name}.", Services.Notifications.AppNoticeSeverity.Success);
        }
        catch (Exception ex)
        {
            App.Notify("Power plans", "Export failed: " + ex.Message, Services.Notifications.AppNoticeSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task ImportSchemeAsync()
    {
        try
        {
            string? path = Win32FilePicker.PickOpenFile(Hwnd,
                new[] { ("Power plan (*.pow)", "*.pow"), ("All files (*.*)", "*.*") }, "Import a power plan");
            if (path is null) return;

            var (exitCode, output) = await RunPowerCfgAsync($"/import \"{path}\"");

            Guid? imported = ParseImportedSchemeGuid(output);
            if (exitCode != 0 || imported is null)
            {
                App.Notify("Power plans", "Import failed: " + DescribeFailure(output), Services.Notifications.AppNoticeSeverity.Error);
                return;
            }

            string name = await Task.Run(() => _service.GetSchemeFriendlyName(imported.Value));
            await LoadSchemesAsync();

            if (string.IsNullOrWhiteSpace(name))
            {
                App.Notify("Power plans",
                    "Import failed: the file was accepted but holds no readable plan. It is probably not a valid .pow export.",
                    Services.Notifications.AppNoticeSeverity.Error);
                return;
            }

            PowerScheme? added = Schemes.FirstOrDefault(s => s.Id == imported.Value);
            if (added is not null) SelectedScheme = added;
            App.Notify("Power plans", $"Imported \"{name}\".", Services.Notifications.AppNoticeSeverity.Success);
        }
        catch (Exception ex)
        {
            App.Notify("Power plans", "Import failed: " + ex.Message, Services.Notifications.AppNoticeSeverity.Error);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunPowerCfgAsync(string arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("powercfg", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start powercfg.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string[] texts = await Task.WhenAll(stdout, stderr);
        return (process.ExitCode, string.Join(" ", texts).Trim());
    }

    private static Guid? ParseImportedSchemeGuid(string output)
    {
        var match = System.Text.RegularExpressions.Regex.Match(output, "[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
        return Guid.TryParse(match.Value, out Guid id) ? id : null;
    }

    private static string DescribeFailure(string output) =>
        string.IsNullOrWhiteSpace(output) ? "powercfg gave no reason." : output;

    public void UpdateSettingAcValue(PowerSetting setting, uint value, PowerSubgroup subgroup)
    {
        if (SelectedScheme == null) return;
        try
        {
            _service.WriteACValue(SelectedScheme.Id, subgroup.Id, setting.Id, value);
            setting.AcValueIndex = value;
            App.Notify("Power plans", $"{setting.Name}: plugged-in value saved.", Services.Notifications.AppNoticeSeverity.Success);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not save \"{setting.Name}\": {ex.Message}";
            App.Notify("Power plans", ErrorMessage, Services.Notifications.AppNoticeSeverity.Error);
        }
    }

    public void UpdateSettingDcValue(PowerSetting setting, uint value, PowerSubgroup subgroup)
    {
        if (SelectedScheme == null) return;
        try
        {
            _service.WriteDCValue(SelectedScheme.Id, subgroup.Id, setting.Id, value);
            setting.DcValueIndex = value;
            App.Notify("Power plans", $"{setting.Name}: battery value saved.", Services.Notifications.AppNoticeSeverity.Success);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not save \"{setting.Name}\": {ex.Message}";
            App.Notify("Power plans", ErrorMessage, Services.Notifications.AppNoticeSeverity.Error);
        }
    }
}
