using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using WinRT;
using WinRT.kaliteConfigVtableClasses;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;
using kaliteConfig.Models;
using kaliteConfig.Services;

namespace kaliteConfig.ViewModels;

public sealed class SnipViewModel : ObservableObject
{
	private List<GalleryItem> _itemCache = new List<GalleryItem>();

	private List<SnipEntry> _allEntries = new List<SnipEntry>();

	private string _snipFolderSignature = "";

	private bool _refreshRunning;

	private Task? _refreshInFlight;

	public const int MaxRecentSnips = 5;

	private string _searchQuery = "";

	private int _typeIndex;

	private int _formatIndex;

	private int _dateIndex;

	private int _sortIndex;

	public List<string> _appOptions = new List<string>();

	private int _appIndex;

	private string _filterType = "All";

	private string _filterFormat = "All";

	private string _filterApp = "All";

	private SnipDateFilter _dateFilter = SnipDateFilter.Any;

	private SnipSortMode _sortMode = SnipSortMode.Newest;

	private bool _favoritesOnly;

	private bool _isListView;

	private GalleryItem? _selectedGalleryItem;

	private string _storageText = "";

	private List<(string Original, string Trash)> _undoBatch = new List<(string, string)>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? clearFiltersCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? disableWindowsSnippingCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? forceRemoveSnippingToolCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? captureScreenCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? undoDeleteCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? deleteSelectedCommand;

	public ObservableCollection<SnipThumbnailItem> RecentSnips { get; } = new ObservableCollection<SnipThumbnailItem>();

	public ObservableCollection<GalleryItem> GallerySnips { get; } = new ObservableCollection<GalleryItem>();

	public ObservableCollection<SnipPipeline> Pipelines { get; } = new ObservableCollection<SnipPipeline>();

	public string SearchQuery
	{
		get
		{
			return _searchQuery;
		}
		set
		{
			if (SetProperty(ref _searchQuery, value, "SearchQuery"))
			{
				NotifyFiltersChanged();
				RebuildGalleryAsync();
			}
		}
	}

	public List<string> TypeOptions => new List<string> { "All", "Region", "Window", "Fullscreen", "Freeform", "Clipboard", "Imported" };

	public int TypeIndex
	{
		get
		{
			return _typeIndex;
		}
		set
		{
			if (SetProperty(ref _typeIndex, value, "TypeIndex"))
			{
				_filterType = ((value > 0 && value < TypeOptions.Count) ? TypeOptions[value] : "All");
				NotifyFiltersChanged();
				RebuildGalleryAsync();
			}
		}
	}

	public List<string> FormatOptions => new List<string> { "All", "PNG", "JPEG", "WebP", "BMP" };

	public int FormatIndex
	{
		get
		{
			return _formatIndex;
		}
		set
		{
			if (SetProperty(ref _formatIndex, value, "FormatIndex"))
			{
				_filterFormat = ((value > 0 && value < FormatOptions.Count) ? FormatOptions[value] : "All");
				NotifyFiltersChanged();
				RebuildGalleryAsync();
			}
		}
	}

	public List<string> DateOptions => new List<string> { "Any time", "Today", "Last 7 days", "Last 30 days", "This month" };

	public int DateIndex
	{
		get
		{
			return _dateIndex;
		}
		set
		{
			if (SetProperty(ref _dateIndex, value, "DateIndex"))
			{
				if (1 == 0)
				{
				}
				SnipDateFilter dateFilter = value switch
				{
					1 => SnipDateFilter.Today, 
					2 => SnipDateFilter.Last7, 
					3 => SnipDateFilter.Last30, 
					4 => SnipDateFilter.ThisMonth, 
					_ => SnipDateFilter.Any, 
				};
				if (1 == 0)
				{
				}
				_dateFilter = dateFilter;
				NotifyFiltersChanged();
				RebuildGalleryAsync();
			}
		}
	}

	public List<string> SortOptions => new List<string> { "Newest first", "Oldest first", "Name (AZ)", "Name (ZA)", "Largest first", "Smallest first" };

	public int SortIndex
	{
		get
		{
			return _sortIndex;
		}
		set
		{
			if (SetProperty(ref _sortIndex, value, "SortIndex"))
			{
				if (1 == 0)
				{
				}
				SnipSortMode sortMode = value switch
				{
					1 => SnipSortMode.Oldest, 
					2 => SnipSortMode.NameAsc, 
					3 => SnipSortMode.NameDesc, 
					4 => SnipSortMode.SizeDesc, 
					5 => SnipSortMode.SizeAsc, 
					_ => SnipSortMode.Newest, 
				};
				if (1 == 0)
				{
				}
				_sortMode = sortMode;
				RebuildGalleryAsync();
			}
		}
	}

	public List<string> AppOptions => BuildAppOptions();

	public List<string> AvailableAppOptions => BuildAppOptions();

	public int SelectedAppIndex
	{
		get
		{
			return _appIndex;
		}
		set
		{
			if (SetProperty(ref _appIndex, value, "SelectedAppIndex"))
			{
				List<string> availableAppOptions = AvailableAppOptions;
				_filterApp = ((value > 0 && value < availableAppOptions.Count) ? availableAppOptions[value] : "All");
				NotifyFiltersChanged();
				RebuildGalleryAsync();
			}
		}
	}

	public bool FavoritesOnly
	{
		get
		{
			return _favoritesOnly;
		}
		set
		{
			if (SetProperty(ref _favoritesOnly, value, "FavoritesOnly"))
			{
				NotifyFiltersChanged();
				RebuildGalleryAsync();
			}
		}
	}

	public bool IsListView
	{
		get
		{
			return _isListView;
		}
		set
		{
			if (SetProperty(ref _isListView, value, "IsListView"))
			{
				OnPropertyChanged("IsGridView");
				OnPropertyChanged("GridViewVisibility");
				OnPropertyChanged("ListViewVisibility");
				OnPropertyChanged("StatusBarSnipCountText");
			}
		}
	}

	public bool IsGridView
	{
		get
		{
			return !IsListView;
		}
		set
		{
			IsListView = !value;
		}
	}

	public Visibility GridViewVisibility => IsListView ? Visibility.Collapsed : Visibility.Visible;

	public Visibility ListViewVisibility => (!IsListView) ? Visibility.Collapsed : Visibility.Visible;

	public Visibility SelectionBarVisibility => (!HasSelection) ? Visibility.Collapsed : Visibility.Visible;

	public Visibility DetailsEmptyVisibility => (SelectedGalleryItem != null) ? Visibility.Collapsed : Visibility.Visible;

	public Visibility DetailsContentVisibility => (SelectedGalleryItem == null) ? Visibility.Collapsed : Visibility.Visible;

	public Visibility HasActiveFiltersVisibility => (!HasActiveFilters) ? Visibility.Collapsed : Visibility.Visible;

	public bool HasActiveFilters => !string.IsNullOrWhiteSpace(_searchQuery) || _filterType != "All" || _filterFormat != "All" || _filterApp != "All" || _favoritesOnly || _dateFilter != SnipDateFilter.Any;

	public GalleryItem? SelectedGalleryItem
	{
		get
		{
			return _selectedGalleryItem;
		}
		set
		{
			if (SetProperty(ref _selectedGalleryItem, value, "SelectedGalleryItem"))
			{
				OnPropertyChanged("StatusBarSnipCountText");
				OnPropertyChanged("DetailsEmptyVisibility");
				OnPropertyChanged("DetailsContentVisibility");
			}
		}
	}

	public string StatusBarRegistrationText => ((App)Application.Current).Sniper?.HotkeyStatusText ?? "Hotkey: not registered";

	public string CaptureHotkeyCaption => ((App)Application.Current).Sniper?.HotkeyLabel ?? "Not registered";

	public Brush StatusBarHotkeyBrush
	{
		get
		{
			SnipService sniper = ((App)Application.Current).Sniper;
			return (sniper != null && sniper.HotkeyRegistered) ? new SolidColorBrush(Colors.LightGreen) : new SolidColorBrush(Colors.Orange);
		}
	}

	public string StatusBarHotkeyTooltip
	{
		get
		{
			SnipService sniper = ((App)Application.Current).Sniper;
			if (sniper == null)
			{
				return "Capture service unavailable.";
			}
			return sniper.HotkeyRegistered ? (sniper.HotkeyLabel + " opens the capture overlay.") : ("No capture hotkey could be registered. " + sniper.HotkeyError);
		}
	}

	public string StatusBarPrtScnOwner
	{
		get
		{
			try
			{
				using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Control Panel\\Keyboard", writable: false);
				if (registryKey != null && registryKey.GetValue("PrintScreenKeyForSnippingEnabled") is int num && num == 0)
				{
					return "PrtScn: kaliteConfig";
				}
			}
			catch
			{
			}
			return "PrtScn: Windows Snipping Tool";
		}
	}

	public Brush StatusBarPrtScnBrush => (StatusBarPrtScnOwner == "PrtScn: kaliteConfig") ? new SolidColorBrush(Colors.LightGreen) : new SolidColorBrush(Colors.Orange);

	public string StatusBarPrtScnTooltip => (StatusBarPrtScnOwner == "PrtScn: kaliteConfig") ? "kaliteConfig owns the PrintScreen key; Windows Snipping Tool is unbound. Click to open Snip settings." : "Windows Snipping Tool still owns PrtScn. Use 'Override Windows Snipping Tool' in Snip settings to reclaim it.";

	public string StatusBarHijackText => StatusBarPrtScnOwner;

	public string ElevationBadgeText => IsRunningElevated ? "Running as administrator" : "";

	public Visibility ElevationBadgeVisibility => (!IsRunningElevated) ? Visibility.Collapsed : Visibility.Visible;

	public static bool IsRunningElevated
	{
		get
		{
			try
			{
				using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
				return new WindowsPrincipal(ntIdentity).IsInRole(WindowsBuiltInRole.Administrator);
			}
			catch
			{
				return false;
			}
		}
	}

	public string StorageText => _storageText;

	public string StatusBarSnipCountText => $"{GallerySnips.Count} snips";

	public bool IsInstantModeEnabled
	{
		get
		{
			return SnipSettingsService.Load().InstantMode;
		}
		set
		{
			SnipSettings snipSettings = SnipSettingsService.Load();
			snipSettings.InstantMode = value;
			SnipSettingsService.Save(snipSettings);
			OnPropertyChanged("IsInstantModeEnabled");
		}
	}

	public bool SnipToastEnabled
	{
		get
		{
			return SnipSettingsService.Load().CaptureToasts;
		}
		set
		{
			SnipSettings snipSettings = SnipSettingsService.Load();
			snipSettings.CaptureToasts = value;
			SnipSettingsService.Save(snipSettings);
			OnPropertyChanged("SnipToastEnabled");
		}
	}

	public List<string> HotkeyPresets => SnipHotkeyService.Presets.Select((SnipHotkeyService.HotkeyPreset p) => p.Label).Concat(new string[1] { "Custom." }).ToList();

	public string CustomHotkeyText
	{
		get
		{
			SnipSettings snipSettings = SnipSettingsService.Load();
			if (snipSettings.CustomHotkeyModifiers == 0 && snipSettings.CustomHotkeyVk == 0)
			{
				return "";
			}
			return SnipHotkeyService.FormatLabel(snipSettings.CustomHotkeyModifiers, snipSettings.CustomHotkeyVk);
		}
	}

	public int SelectedHotkeyIndex
	{
		get
		{
			return SnipSettingsService.Load().HotkeyIndex;
		}
		set
		{
			if (value < 0 || value > 5)
			{
				value = 0;
			}
			SnipSettings snipSettings = SnipSettingsService.Load();
			snipSettings.HotkeyIndex = value;
			if (value != 5)
			{
				snipSettings.CustomHotkeyModifiers = 0u;
				snipSettings.CustomHotkeyVk = 0u;
			}
			SnipSettingsService.Save(snipSettings);
			OnPropertyChanged("SelectedHotkeyIndex");
			OnPropertyChanged("CustomHotkeyText");
			App app = (App)Application.Current;
			var (flag, content) = app.Sniper.RebindHotkey(value);
			RefreshHotkeyStatus();
			if (!flag)
			{
				NotifyMessage("Hotkey unavailable", content);
			}
			else if (!string.IsNullOrEmpty(app.Sniper.HotkeyError))
			{
				NotifyMessage("Hotkey changed to a free key", content);
			}
		}
	}

	public ObservableCollection<GalleryItem> SelectedItems { get; } = new ObservableCollection<GalleryItem>();

	public bool HasSelection => SelectedItems.Count > 0;

	public string SelectedCountText => $"{SelectedItems.Count} selected";

	public bool IsSelectionModeActive { get; set; }

	public bool HasUndo => _undoBatch.Count > 0;

	public string UndoMessage => (_undoBatch.Count > 0) ? $"{_undoBatch.Count} snip(s) in trash" : "";

	public ObservableCollection<SnipThumbnailItem> TrashItems { get; } = new ObservableCollection<SnipThumbnailItem>();

	public double GalleryThumbSize
	{
		get
		{
			return SnipSettingsService.Load().GalleryThumbSize;
		}
		set
		{
			SnipSettings snipSettings = SnipSettingsService.Load();
			if (snipSettings.GalleryThumbSize == value)
			{
				return;
			}
			snipSettings.GalleryThumbSize = value;
			SnipSettingsService.Save(snipSettings);
			foreach (GalleryItem item in _itemCache)
			{
				item.ThumbSize = value;
			}
			OnPropertyChanged("GalleryThumbSize");
		}
	}

	public bool ThumbnailFillMode
	{
		get
		{
			return SnipSettingsService.Load().ThumbnailFillMode;
		}
		set
		{
			SnipSettings snipSettings = SnipSettingsService.Load();
			if (snipSettings.ThumbnailFillMode == value)
			{
				return;
			}
			snipSettings.ThumbnailFillMode = value;
			SnipSettingsService.Save(snipSettings);
			foreach (GalleryItem item in _itemCache)
			{
				item.Card.FillMode = value;
			}
			OnPropertyChanged("ThumbnailFillMode");
		}
	}

	public int AutoDeleteDays
	{
		get
		{
			return SnipSettingsService.Load().AutoDeleteDays;
		}
		set
		{
			SnipSettings snipSettings = SnipSettingsService.Load();
			if (snipSettings.AutoDeleteDays != value)
			{
				snipSettings.AutoDeleteDays = value;
				SnipSettingsService.Save(snipSettings);
				OnPropertyChanged("AutoDeleteDays");
			}
		}
	}

	public int AutoDeleteIndex
	{
		get
		{
			int autoDeleteDays = AutoDeleteDays;
			if (1 == 0)
			{
			}
			int result = autoDeleteDays switch
			{
				7 => 1, 
				30 => 2, 
				90 => 3, 
				365 => 4, 
				_ => 0, 
			};
			if (1 == 0)
			{
			}
			return result;
		}
		set
		{
			if (1 == 0)
			{
			}
			int num = value switch
			{
				1 => 7, 
				2 => 30, 
				3 => 90, 
				4 => 365, 
				_ => 0, 
			};
			if (1 == 0)
			{
			}
			int autoDeleteDays = num;
			AutoDeleteDays = autoDeleteDays;
			OnPropertyChanged("AutoDeleteIndex");
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ClearFiltersCommand => clearFiltersCommand ?? (clearFiltersCommand = new RelayCommand(ClearFilters));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand DisableWindowsSnippingCommand => disableWindowsSnippingCommand ?? (disableWindowsSnippingCommand = new RelayCommand(DisableWindowsSnipping));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ForceRemoveSnippingToolCommand => forceRemoveSnippingToolCommand ?? (forceRemoveSnippingToolCommand = new AsyncRelayCommand(ForceRemoveSnippingToolAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CaptureScreenCommand => captureScreenCommand ?? (captureScreenCommand = new RelayCommand(CaptureScreen));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand UndoDeleteCommand => undoDeleteCommand ?? (undoDeleteCommand = new RelayCommand(UndoDelete));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand DeleteSelectedCommand => deleteSelectedCommand ?? (deleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync));

	public event Action<string, string>? BannerRequested;

	public event EventHandler<GalleryItem>? RenameRequested;

	public SnipViewModel()
	{
		SnipPipeline snipPipeline = new SnipPipeline
		{
			Name = "Instant Share (Discord)",
			BoundProcess = "discord.exe",
			HotkeyLabel = "Ctrl + Shift + S"
		};
		snipPipeline.Actions.Add(new CopyAction());
		snipPipeline.Actions.Add(new SaveAction());
		Pipelines.Add(snipPipeline);
	}

	public Task RefreshGalleryAsync()
	{
		return RefreshCoreAsync(refreshFilters: true);
	}

	public async Task<bool> RefreshQuietAsync()
	{
		if (_refreshRunning)
		{
			Task running = _refreshInFlight;
			if (running != null)
			{
				try
				{
					await running.ConfigureAwait(continueOnCapturedContext: true);
				}
				catch
				{
				}
			}
			return false;
		}
		if (string.Equals(await SnipGalleryService.GetSnipSignatureAsync().ConfigureAwait(continueOnCapturedContext: true), _snipFolderSignature, StringComparison.Ordinal))
		{
			SnipThumbnailItem.ThumbLog("quiet: folder unchanged");
			return false;
		}
		SnipThumbnailItem.ThumbLog($"quiet: folder changed, {_allEntries.Count} entries cached, {GallerySnips.Count} in gallery");
		await RefreshCoreAsync(refreshFilters: false).ConfigureAwait(continueOnCapturedContext: true);
		SnipThumbnailItem.ThumbLog($"quiet: merged to {GallerySnips.Count} gallery / {RecentSnips.Count} recent");
		return true;
	}

	private Task RefreshCoreAsync(bool refreshFilters)
	{
		Task refreshInFlight = _refreshInFlight;
		if (refreshInFlight != null && !refreshInFlight.IsCompleted)
		{
			return refreshInFlight;
		}
		TaskCompletionSource taskCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_refreshInFlight = taskCompletionSource.Task;
		RunRefreshAsync(refreshFilters, taskCompletionSource);
		return taskCompletionSource.Task;
	}

	private async Task RunRefreshAsync(bool refreshFilters, TaskCompletionSource completion)
	{
		_refreshRunning = true;
		try
		{
			SnipGalleryService.CleanupOrphanedMeta();
			_allEntries = await SnipGalleryService.GetAllSnipEntriesAsync().ConfigureAwait(continueOnCapturedContext: true);
			_snipFolderSignature = await SnipGalleryService.GetSnipSignatureAsync().ConfigureAwait(continueOnCapturedContext: true);
			await RefreshStorageTextAsync().ConfigureAwait(continueOnCapturedContext: true);
			OnPropertyChanged("AvailableAppOptions");
			await RebuildGalleryAsync().ConfigureAwait(continueOnCapturedContext: true);
			MergeRecentSnips();
		}
		finally
		{
			_refreshRunning = false;
			if (_refreshInFlight == completion.Task)
			{
				_refreshInFlight = null;
			}
			completion.TrySetResult();
		}
	}

	private void MergeRecentSnips()
	{
		Dictionary<string, SnipThumbnailItem> dictionary = new Dictionary<string, SnipThumbnailItem>(StringComparer.OrdinalIgnoreCase);
		foreach (SnipThumbnailItem recentSnip in RecentSnips)
		{
			if (!string.IsNullOrEmpty(recentSnip.FilePath))
			{
				dictionary[recentSnip.FilePath] = recentSnip;
			}
		}
		List<SnipThumbnailItem> list = new List<SnipThumbnailItem>();
		foreach (SnipEntry item in _allEntries.OrderByDescending((SnipEntry e) => e.CreatedUtc).Take(5))
		{
			if (dictionary.TryGetValue(item.FilePath, out var value))
			{
				list.Add(value);
				continue;
			}
			list.Add(new SnipThumbnailItem
			{
				Filename = item.Name,
				FilePath = item.FilePath
			});
		}
		SilentListMerge.Sync(RecentSnips, list, (SnipThumbnailItem i) => i.FilePath);
		OnPropertyChanged("StatusBarSnipCountText");
	}

	private async Task RebuildGalleryAsync()
	{
		OnPropertyChanged("AvailableAppOptions");
		string app = _filterApp;
		if (app != "All" && !AvailableAppOptions.Contains(app))
		{
			_filterApp = "All";
			OnPropertyChanged("SelectedAppIndex");
		}
		List<SnipEntry> filtered = SnipGalleryQuery.Apply(_allEntries, BuildFilterSpec(), _sortMode);
		List<GalleryItem> flatItems = new List<GalleryItem>();
		Dictionary<string, GalleryItem> cacheByPath = _itemCache.ToDictionary((GalleryItem i) => i.Entry.FilePath, (GalleryItem i) => i, StringComparer.OrdinalIgnoreCase);
		foreach (SnipEntry entry in filtered)
		{
			if (cacheByPath.TryGetValue(entry.FilePath, out var cached) && cached.Entry.SizeBytes == entry.SizeBytes)
			{
				cached.Entry.Tags = entry.Tags;
				cached.Entry.IsFavorite = entry.IsFavorite;
				cached.Entry.SizeBytes = entry.SizeBytes;
				cached.Entry.Width = entry.Width;
				cached.Entry.Height = entry.Height;
				flatItems.Add(cached);
			}
			else
			{
				flatItems.Add(new GalleryItem(this, entry));
			}
			cached = null;
		}
		_itemCache = flatItems;
		SilentListMerge.Sync(GallerySnips, flatItems, (GalleryItem i) => i.UniqueId);
		if (SelectedGalleryItem != null && !GallerySnips.Contains(SelectedGalleryItem))
		{
			SelectedGalleryItem = null;
		}
		OnPropertyChanged("StatusBarSnipCountText");
		OnPropertyChanged("StorageText");
		await Task.CompletedTask;
	}

	private SnipFilterSpec BuildFilterSpec()
	{
		return new SnipFilterSpec
		{
			Query = _searchQuery,
			Type = _filterType,
			Format = _filterFormat,
			SourceApp = _filterApp,
			FavoritesOnly = _favoritesOnly,
			Date = _dateFilter
		};
	}

	private List<string> BuildAppOptions()
	{
		List<string> list = SnipGalleryQuery.DistinctSourceApps(_allEntries);
		if (_filterApp != "All" && !list.Contains(_filterApp))
		{
			_filterApp = "All";
			OnPropertyChanged("SelectedAppIndex");
		}
		List<string> list2 = new List<string> { "All apps" };
		list2.AddRange(list);
		return list2;
	}

	public void ClearFilters()
	{
		_searchQuery = "";
		OnPropertyChanged("SearchQuery");
		_typeIndex = 0;
		OnPropertyChanged("TypeIndex");
		_formatIndex = 0;
		OnPropertyChanged("FormatIndex");
		_dateIndex = 0;
		OnPropertyChanged("DateIndex");
		_appIndex = 0;
		OnPropertyChanged("SelectedAppIndex");
		_filterType = "All";
		_filterFormat = "All";
		_filterApp = "All";
		_dateFilter = SnipDateFilter.Any;
		_favoritesOnly = false;
		OnPropertyChanged("FavoritesOnly");
		OnPropertyChanged("HasActiveFiltersVisibility");
		RebuildGalleryAsync();
	}

	private void NotifyFiltersChanged()
	{
		OnPropertyChanged("HasActiveFiltersVisibility");
	}

	public void RefreshHotkeyStatus()
	{
		OnPropertyChanged("StatusBarRegistrationText");
		OnPropertyChanged("CaptureHotkeyCaption");
		OnPropertyChanged("StatusBarHotkeyBrush");
		OnPropertyChanged("StatusBarHotkeyTooltip");
	}

	private async Task RefreshStorageTextAsync()
	{
		(int Count, long Bytes) tuple = await SnipGalleryService.GetStorageStatsAsync();
		int count = tuple.Count;
		long bytes = tuple.Bytes;
		_storageText = $"{SnipGalleryQuery.FormatSize(bytes)} in {count} snips";
		OnPropertyChanged("StorageText");
	}

	public void ApplyCustomHotkey(uint modifiers, uint vk)
	{
		App app = (App)Application.Current;
		var (flag, text) = app.Sniper.TryCustomHotkey(modifiers, vk);
		if (!flag)
		{
			NotifyMessage("Hotkey taken", text + " The previous hotkey is still active.");
			OnPropertyChanged("SelectedHotkeyIndex");
			return;
		}
		SnipSettings snipSettings = SnipSettingsService.Load();
		snipSettings.HotkeyIndex = 5;
		snipSettings.CustomHotkeyModifiers = modifiers;
		snipSettings.CustomHotkeyVk = vk;
		SnipSettingsService.Save(snipSettings);
		OnPropertyChanged("SelectedHotkeyIndex");
		OnPropertyChanged("CustomHotkeyText");
		RefreshHotkeyStatus();
		NotifyMessage("Custom hotkey active", text);
	}

	public void DisableWindowsSnipping()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Control Panel\\Keyboard", writable: true);
			registryKey?.SetValue("PrintScreenKeyForSnippingEnabled", 0, RegistryValueKind.DWord);
			OnPropertyChanged("StatusBarHijackText");
			string text;
			try
			{
				using RegistryKey registryKey2 = Registry.LocalMachine.CreateSubKey("SOFTWARE\\Policies\\Microsoft\\TabletPC", writable: true);
				registryKey2?.SetValue("DisableSnippingTool", 1, RegistryValueKind.DWord);
				text = "Machine policy set.";
			}
			catch (Exception ex)
			{
				text = "Machine policy skipped (needs admin): " + ex.Message;
			}
			SnipSettings snipSettings = SnipSettingsService.Load();
			App app = (App)Application.Current;
			var (flag, text2) = app.Sniper.RebindHotkey(snipSettings.HotkeyIndex);
			if (!flag)
			{
				NotifyMessage("Windows Snipping disabled, hotkey busy", text2 + " Sign out/in, then pick a hotkey below. " + text);
			}
			else
			{
				NotifyMessage("Print Screen reclaimed", "Windows Snipping Tool unbound. " + text + " Sign out/in (or restart) once, then PrtScn opens kalite Snip.");
			}
		}
		catch (Exception ex2)
		{
			NotifyMessage("Override failed", ex2.Message);
		}
	}

	public async Task ForceRemoveSnippingToolAsync()
	{
		try
		{
			string cmd = "takeown /f \"C:\\Windows\\System32\\SnippingTool.exe\" /a\r\nicacls \"C:\\Windows\\System32\\SnippingTool.exe\" /grant administrators:F\r\nren \"C:\\Windows\\System32\\SnippingTool.exe\" \"SnippingTool.bak\"\r\nreg add \"HKCU\\Control Panel\\Keyboard\" /v PrintScreenKeyForSnippingEnabled /t REG_DWORD /d 0 /f\r\nreg add \"HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options\\SnippingTool.exe\" /v Debugger /t REG_SZ /d \"systray.exe\" /f\r\npowershell -Command \"Get-AppxPackage -AllUsers Microsoft.ScreenSketch | Remove-AppxPackage -AllUsers\"\r\n";
			string tempScript = Path.Combine(Path.GetTempPath(), "remove_snipping_tool.bat");
			File.WriteAllText(tempScript, cmd);
			ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + tempScript + "\"")
			{
				UseShellExecute = true,
				Verb = "runas",
				WindowStyle = ProcessWindowStyle.Hidden,
				CreateNoWindow = true
			};
			Process proc = Process.Start(psi);
			if (proc != null)
			{
				await proc.WaitForExitAsync();
			}
			try
			{
				File.Delete(tempScript);
			}
			catch
			{
			}
			OnPropertyChanged("StatusBarHijackText");
			App app = (App)Application.Current;
			app.Sniper?.RebindHotkey(SnipSettingsService.Load().HotkeyIndex);
			RefreshHotkeyStatus();
			NotifyMessage("Snipping Tool removed", "Built-in Snipping Tool has been completely removed and disabled.");
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			NotifyMessage("Removal failed", ex2.Message);
		}
	}

	public void CaptureScreen()
	{
		App app = (App)Application.Current;
		app.Sniper?.TriggerCapture();
	}

	public void OnFavoriteChanged()
	{
		OnPropertyChanged("StorageText");
		RebuildGalleryAsync();
	}

	public void NotifyMessage(string title, string content)
	{
		BannerRequested?.Invoke(title, content);
	}

	public async Task SetFavoriteAsync(GalleryItem item, bool value)
	{
		SnipMeta meta = await SnipGalleryService.LoadMetaAsync(item.Entry.FilePath);
		if (meta == null)
		{
			meta = new SnipMeta();
		}
		meta.IsFavorite = value;
		item.Entry.IsFavorite = value;
		await SnipGalleryService.SaveMetaAsync(item.Entry.FilePath, meta);
	}

	public void OnSelectionChanged()
	{
		SelectedItems.Clear();
		foreach (GalleryItem gallerySnip in GallerySnips)
		{
			if (gallerySnip.IsSelected)
			{
				SelectedItems.Add(gallerySnip);
			}
		}
		OnPropertyChanged("HasSelection");
		OnPropertyChanged("SelectedCountText");
		OnPropertyChanged("SelectionBarVisibility");
		OnPropertyChanged("StatusBarSnipCountText");
	}

	public async Task DeleteItemsAsync(IEnumerable<GalleryItem> items)
	{
		List<GalleryItem> list = items.Where((GalleryItem i) => i.Entry.FilePath != null).ToList();
		if (list.Count != 0)
		{
			List<string> paths = list.Select((GalleryItem i) => i.Entry.FilePath).ToList();
			_undoBatch = await SnipGalleryService.DeleteToTrashAsync(paths);
			if (_undoBatch.Count > 0)
			{
				OnPropertyChanged("HasUndo");
				OnPropertyChanged("UndoMessage");
				NotifyMessage("Moved to trash", $"{_undoBatch.Count} snip(s) moved. Use Undo to restore.");
			}
			await RefreshGalleryAsync();
		}
	}

	public void UndoDelete()
	{
		if (_undoBatch.Count != 0)
		{
			SnipGalleryService.UndoDelete(_undoBatch);
			_undoBatch.Clear();
			OnPropertyChanged("HasUndo");
			OnPropertyChanged("UndoMessage");
			NotifyMessage("Restored", "The deleted snips were restored.");
			RefreshGalleryAsync();
		}
	}

	public async Task DeleteSelectedAsync()
	{
		await DeleteItemsAsync(SelectedItems.ToList());
	}

	public async Task RefreshTrashAsync()
	{
		List<SnipGalleryService.TrashEntry> entries = await Task.Run(() => SnipGalleryService.GetTrashEntries()).ConfigureAwait(continueOnCapturedContext: true);
		TrashItems.Clear();
		foreach (SnipGalleryService.TrashEntry e in entries)
		{
			TrashItems.Add(new SnipThumbnailItem
			{
				Filename = e.Name,
				FilePath = e.TrashPath
			});
		}
	}

	public async Task DeleteGalleryPathsAsync(List<string> paths)
	{
		HashSet<string> set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
		await DeleteItemsAsync(GallerySnips.Where((GalleryItem g) => set.Contains(g.Entry.FilePath)).ToList()).ConfigureAwait(continueOnCapturedContext: true);
	}

	public async Task<List<(string FilePath, string Name, long Bytes)>> FindBlankSnipsAsync()
	{
		return await SnipGalleryService.FindBlankSnipsAsync().ConfigureAwait(continueOnCapturedContext: true);
	}

	public async Task RestoreTrashAsync(List<string> trashPaths)
	{
		int done = await Task.Run(() => SnipGalleryService.RestoreFromTrash(trashPaths)).ConfigureAwait(continueOnCapturedContext: true);
		NotifyMessage("Restored", $"{done} snip(s) moved back to the gallery.");
		await RefreshTrashAsync().ConfigureAwait(continueOnCapturedContext: true);
		await RefreshGalleryAsync().ConfigureAwait(continueOnCapturedContext: true);
	}

	public async Task DeleteTrashForeverAsync(List<string> trashPaths)
	{
		int done = await Task.Run(() => SnipGalleryService.DeleteForever(trashPaths)).ConfigureAwait(continueOnCapturedContext: true);
		NotifyMessage("Deleted forever", $"{done} snip(s) permanently removed.");
		await RefreshTrashAsync().ConfigureAwait(continueOnCapturedContext: true);
	}

	public async Task EmptyTrashAsync()
	{
		await Task.Run(() =>
		{
			SnipGalleryService.PurgeTrash();
		}).ConfigureAwait(continueOnCapturedContext: true);
		NotifyMessage("Bin emptied", "Everything in the bin was permanently removed.");
		await RefreshTrashAsync().ConfigureAwait(continueOnCapturedContext: true);
	}

	public async Task CopySelectedAsync()
	{
		List<StorageFile> files = new List<StorageFile>();
		foreach (GalleryItem item in SelectedItems)
		{
			try
			{
				List<StorageFile> list = files;
				list.Add(await StorageFile.GetFileFromPathAsync(item.Entry.FilePath));
			}
			catch
			{
			}
		}
		if (files.Count != 0)
		{
			DataPackage dp = new DataPackage();
			dp.SetStorageItems(files);
			Clipboard.SetContent(dp);
			NotifyMessage("Copied", $"{files.Count} file(s) copied to clipboard.");
		}
	}

	public async Task ExportSelectedZipAsync(string zipPath)
	{
		List<string> paths = SelectedItems.Select((GalleryItem i) => i.Entry.FilePath).Where(File.Exists).ToList();
		if (paths.Count != 0)
		{
			await SnipGalleryService.ExportZipAsync(paths, zipPath);
			NotifyMessage("Archive saved", zipPath);
		}
	}

	public async Task MoveSelectedAsync(string destDir)
	{
		int moved = await SnipGalleryService.MoveFilesAsync(SelectedItems.Select((GalleryItem i) => i.Entry.FilePath), destDir);
		NotifyMessage("Moved", $"{moved} snip(s) moved to {destDir}");
		await RefreshGalleryAsync();
	}

	public async Task ConvertSelectedAsync(string destDir, string targetExtension)
	{
		int done = 0;
		foreach (GalleryItem item in SelectedItems.ToList())
		{
			try
			{
				if (await SnipGalleryService.ConvertImageAsync(item.Entry.FilePath, destDir, targetExtension))
				{
					done++;
				}
			}
			catch
			{
			}
		}
		NotifyMessage("Converted", $"{done} image(s) saved as {targetExtension.TrimStart('.')} in {destDir}");
	}

	public Task RequestRenameAsync(GalleryItem item)
	{
		RenameRequested?.Invoke(this, item);
		return Task.CompletedTask;
	}

	public async Task ApplyRenameAsync(GalleryItem item, string newBaseName)
	{
		if (string.IsNullOrWhiteSpace(newBaseName))
		{
			return;
		}
		string ext = Path.GetExtension(item.Entry.FilePath);
		string safeName = SnipGalleryQuery.SafeFileName(newBaseName, ext);
		string newPath = Path.Combine(Path.GetDirectoryName(item.Entry.FilePath), safeName);
		if (string.Equals(newPath, item.Entry.FilePath, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		if (File.Exists(newPath))
		{
			NotifyMessage("Rename failed", "A file with that name already exists.");
			return;
		}
		try
		{
			File.Move(item.Entry.FilePath, newPath);
			string metaSrc = item.Entry.FilePath + ".meta.json";
			if (File.Exists(metaSrc))
			{
				File.Move(metaSrc, newPath + ".meta.json", overwrite: true);
			}
			string thumb = SnipGalleryService.ThumbPathFor(item.Entry.FilePath);
			if (File.Exists(thumb))
			{
				try
				{
					File.Delete(thumb);
				}
				catch
				{
				}
			}
			item.Entry.FilePath = newPath;
			item.Entry.Name = safeName;
			item.OnEntryChanged();
			NotifyMessage("Renamed", safeName);
			await RefreshGalleryAsync();
		}
		catch (Exception ex)
		{
			NotifyMessage("Rename failed", ex.Message);
		}
	}

	public void OpenContainingFolder(GalleryItem item)
	{
		try
		{
			ProcessStartInfo startInfo = new ProcessStartInfo("explorer.exe")
			{
				UseShellExecute = true,
				Arguments = "/select,\"" + item.Entry.FilePath + "\""
			};
			Process.Start(startInfo);
		}
		catch
		{
		}
	}

	public async Task ExtractTextAsync(GalleryItem item)
	{
		try
		{
			using IRandomAccessStream stream = await (await StorageFile.GetFileFromPathAsync(item.Entry.FilePath)).OpenAsync(FileAccessMode.Read);
			SoftwareBitmap sourceBitmap = await (await BitmapDecoder.CreateAsync(stream)).GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
			OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages();
			if (engine == null)
			{
				NotifyMessage("OCR unavailable", "No OCR language pack installed on this system.");
				return;
			}
			string text = (await engine.RecognizeAsync(sourceBitmap)).Text;
			SnipMeta meta = (await SnipGalleryService.LoadMetaAsync(item.Entry.FilePath)) ?? new SnipMeta();
			meta.OcrText = text;
			await SnipGalleryService.SaveMetaAsync(item.Entry.FilePath, meta);
			item.Entry.OcrText = text;
			string preview = (string.IsNullOrWhiteSpace(text) ? "No text detected." : text.Replace("\r", " ").Replace("\n", " ").Trim());
			if (preview.Length > 160)
			{
				preview = preview.Substring(0, 157) + "...";
			}
			if (string.IsNullOrWhiteSpace(text))
			{
				NotifyMessage("OCR complete", "No text was detected in this image.");
				return;
			}
			DataPackage dp = new DataPackage();
			dp.SetText(text);
			Clipboard.SetContent(dp);
			NotifyMessage("Text extracted", preview ?? "");
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			NotifyMessage("OCR failed", ex2.Message);
		}
	}

	public async Task RetryCardAsync(GalleryItem item, double cardDipSize, double rasterizationScale)
	{
		await item.Card.RetryAsync(cardDipSize, rasterizationScale);
		if ((object)item.Card.Image == null && item.Card.IsMissing)
		{
			NotifyMessage("File missing", item.Name + " was deleted or moved outside the app.");
		}
	}

	public async Task RetryCardAsync(SnipThumbnailItem item, double cardDipSize, double rasterizationScale)
	{
		await item.Card.RetryAsync(cardDipSize, rasterizationScale);
		if ((object)item.Card.Image == null && item.Card.IsMissing)
		{
			NotifyMessage("File missing", item.Filename + " was deleted or moved outside the app.");
		}
	}

	public async Task RemoveFromGalleryAsync(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return;
		}
		try
		{
			string meta = filePath + ".meta.json";
			if (!File.Exists(filePath) && File.Exists(meta))
			{
				File.Delete(meta);
			}
		}
		catch
		{
		}
		SnipThumbnailService.DeleteCachedThumbnails(filePath);
		_allEntries = _allEntries.Where((SnipEntry e) => !string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase)).ToList();
		_itemCache = _itemCache.Where((GalleryItem i) => !string.Equals(i.Entry.FilePath, filePath, StringComparison.OrdinalIgnoreCase)).ToList();
		GalleryItem gallery = GallerySnips.FirstOrDefault((GalleryItem i) => string.Equals(i.Entry.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
		if (gallery != null)
		{
			GallerySnips.Remove(gallery);
		}
		SnipThumbnailItem recent = RecentSnips.FirstOrDefault((SnipThumbnailItem i) => string.Equals(i.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
		if (recent != null)
		{
			RecentSnips.Remove(recent);
		}
		GalleryItem sel = SelectedGalleryItem;
		if (sel != null && string.Equals(sel.Entry.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
		{
			SelectedGalleryItem = null;
		}
		OnPropertyChanged("StatusBarSnipCountText");
		await RefreshStorageTextAsync();
		NotifyMessage("Removed from gallery", Path.GetFileName(filePath) + " was already gone; the gallery entry was removed.");
	}

	public void RefreshCardDpi(double rasterizationScale)
	{
		foreach (GalleryItem gallerySnip in GallerySnips)
		{
			gallerySnip.Card.EnsureAsync(gallerySnip.ThumbSize, rasterizationScale);
		}
		foreach (SnipThumbnailItem recentSnip in RecentSnips)
		{
			recentSnip.Card.EnsureAsync(240.0, rasterizationScale);
		}
	}

	public async Task RunAutoDeleteAsync()
	{
		int days = AutoDeleteDays;
		if (days > 0)
		{
			int removed = await SnipGalleryService.AutoDeleteOlderThanAsync(days);
			if (removed > 0)
			{
				NotifyMessage("Auto-delete", $"{removed} old snip(s) removed.");
				await RefreshGalleryAsync();
			}
		}
	}

	public async Task GroupFilesAsync(IEnumerable<GalleryItem> items)
	{
		List<GalleryItem> list = ((items != null) ? items.ToList() : null);
		if (list == null || list.Count < 2)
		{
			return;
		}
		string baseDir = Path.GetDirectoryName(list[0].Entry.FilePath);
		string newDir = Path.Combine(baseDir, "Screenshots 1");
		int counter = 1;
		while (Directory.Exists(newDir) || File.Exists(newDir))
		{
			counter++;
			newDir = Path.Combine(baseDir, $"Screenshots {counter}");
		}
		Directory.CreateDirectory(newDir);
		_undoBatch.Clear();
		foreach (GalleryItem item in list)
		{
			string newPath = Path.Combine(newDir, Path.GetFileName(item.Entry.FilePath));
			File.Move(item.Entry.FilePath, newPath);
			SnipMeta meta = await SnipGalleryService.LoadMetaAsync(item.Entry.FilePath);
			if (meta != null)
			{
				await SnipGalleryService.SaveMetaAsync(newPath, meta);
				File.Delete(Path.ChangeExtension(item.Entry.FilePath, ".xml"));
			}
			_undoBatch.Add((item.Entry.FilePath, newPath));
		}
		OnPropertyChanged("HasUndo");
		NotifyMessage("Grouped selected files", "Created " + Path.GetFileName(newDir) + ". Use Undo to unpack.");
		await RefreshCoreAsync(refreshFilters: false);
		ClearSelection();
	}

	public void ClearSelection()
	{
		SelectedItems.Clear();
		foreach (GalleryItem gallerySnip in GallerySnips)
		{
			gallerySnip.IsSelected = false;
		}
		OnSelectionChanged();
	}

	public async Task MergeIntoFolderAsync(GalleryItem target, GalleryItem source)
	{
		if (target == source)
		{
			return;
		}
		_undoBatch.Clear();
		string baseDir = Path.GetDirectoryName(target.Entry.FilePath);
		string newDir = Path.Combine(baseDir, "Screenshots 1");
		int counter = 1;
		while (Directory.Exists(newDir) || File.Exists(newDir))
		{
			counter++;
			newDir = Path.Combine(baseDir, $"Screenshots {counter}");
		}
		Directory.CreateDirectory(newDir);
		string targetNewPath = Path.Combine(newDir, Path.GetFileName(target.Entry.FilePath));
		File.Move(target.Entry.FilePath, targetNewPath);
		SnipMeta metaT = await SnipGalleryService.LoadMetaAsync(target.Entry.FilePath);
		if (metaT != null)
		{
			await SnipGalleryService.SaveMetaAsync(targetNewPath, metaT);
			File.Delete(Path.ChangeExtension(target.Entry.FilePath, ".xml"));
		}
		_undoBatch.Add((target.Entry.FilePath, targetNewPath));
		string targetDir = newDir;
		GalleryItem[] sourcesToMove = new GalleryItem[1] { source };
		foreach (GalleryItem s in sourcesToMove.ToList())
		{
			string sNewPath = Path.Combine(targetDir, Path.GetFileName(s.Entry.FilePath));
			if (s.Entry.FilePath != sNewPath)
			{
				File.Move(s.Entry.FilePath, sNewPath);
				SnipMeta metaS = await SnipGalleryService.LoadMetaAsync(s.Entry.FilePath);
				if (metaS != null)
				{
					await SnipGalleryService.SaveMetaAsync(sNewPath, metaS);
					File.Delete(Path.ChangeExtension(s.Entry.FilePath, ".xml"));
				}
				_undoBatch.Add((s.Entry.FilePath, sNewPath));
			}
		}
		OnPropertyChanged("HasUndo");
		NotifyMessage("Merged into folder", "Created grouping in " + Path.GetFileName(targetDir) + ". Use Undo to unpack.");
		await RefreshCoreAsync(refreshFilters: false);
	}

	public async Task MoveToRootAsync(IEnumerable<GalleryItem> items)
	{
		List<GalleryItem> list = ((items != null) ? items.ToList() : null);
		if (list == null || list.Count == 0)
		{
			return;
		}
		string rootDir = SnipGalleryService.GetSnipsDirectory();
		_undoBatch.Clear();
		foreach (GalleryItem item in list)
		{
			string newPath = Path.Combine(rootDir, Path.GetFileName(item.Entry.FilePath));
			if (item.Entry.FilePath != newPath)
			{
				File.Move(item.Entry.FilePath, newPath);
				SnipMeta meta = await SnipGalleryService.LoadMetaAsync(item.Entry.FilePath);
				if (meta != null)
				{
					await SnipGalleryService.SaveMetaAsync(newPath, meta);
					File.Delete(Path.ChangeExtension(item.Entry.FilePath, ".xml"));
				}
				_undoBatch.Add((item.Entry.FilePath, newPath));
			}
		}
		OnPropertyChanged("HasUndo");
		NotifyMessage("Moved to Gallery", $"Moved {list.Count} item(s) back to the main gallery. Use Undo to revert.");
		await RefreshCoreAsync(refreshFilters: false);
		ClearSelection();
	}
}
