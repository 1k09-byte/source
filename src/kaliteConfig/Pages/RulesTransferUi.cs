using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace kaliteConfig.Pages;

internal static class RulesTransferUi
{
    public static async Task<string?> ExportAsync()
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"kaliteConfig-rules-{DateTime.Now:yyyy-MM-dd-HHmm}",
        };
        picker.FileTypeChoices.Add("JSON rules file", new List<string> { ".json" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

        var file = await picker.PickSaveFileAsync();
        if (file is null) return null;

        try
        {
            string json = App.Current.ProfileWatcher.ExportRulesJson(out int count);
            await FileIO.WriteTextAsync(file, json);
            return $"Exported {count} rule(s) to {file.Path}.";
        }
        catch (Exception ex)
        {
            return "Export failed: " + ex.Message;
        }
    }

    public static async Task<string?> ImportAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

        var file = await picker.PickSingleFileAsync();
        if (file is null) return null;

        try
        {
            string json = await FileIO.ReadTextAsync(file);
            var result = await App.Current.ProfileWatcher.ImportRulesJsonAsync(json);
            return result.Message;
        }
        catch (Exception ex)
        {
            return "Import failed: " + ex.Message;
        }
    }
}
