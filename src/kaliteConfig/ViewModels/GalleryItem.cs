using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT;
using WinRT.kaliteConfigVtableClasses;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using kaliteConfig.Models;
using kaliteConfig.Services;
using kaliteConfig.Services.Theming;

namespace kaliteConfig.ViewModels;

public class GalleryItem : ObservableObject
{
	private readonly SnipViewModel _owner;

	private double _thumbSize = 220.0;

	private bool _isSelected;

	private bool _isHover;

	private bool _isFavorite;

	private bool _thumbnailLoading;

	private bool _isCommandRunning;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? openCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? copyImageCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? copyPathCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? copyTextCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? deleteCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? renameCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openContainingFolderCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openInViewerCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleFavoriteCommand;

	public SnipEntry Entry { get; }

	public virtual string UniqueId => Entry.FilePath;

	public string Name => Entry.Name;

	public string FilePath => Entry.FilePath;

	public string SizeText => SnipGalleryQuery.FormatSize(Entry.SizeBytes);

	public string CreatedText => Entry.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

	public string SourceAppText => string.IsNullOrEmpty(Entry.SourceApp) ? "Unknown app" : Entry.SourceApp;

	public string ResolutionText => (Entry.Width > 0 && Entry.Height > 0) ? $"{Entry.Width} × {Entry.Height}" : "";

	public string TagsText => (Entry.Tags.Count > 0) ? string.Join(", ", Entry.Tags) : "";

	public string TypeText => Entry.Type;

	public string FilePathModifiedTitle => Path.GetFileName(FilePath);

	public double ThumbSize
	{
		get
		{
			return _thumbSize;
		}
		set
		{
			SetProperty(ref _thumbSize, value, "ThumbSize");
		}
	}

	public SnipCardState Card { get; }

	public BitmapImage? Thumbnail => Card.Image;

	public bool IsSelected
	{
		get
		{
			return _isSelected;
		}
		set
		{
			if (SetProperty(ref _isSelected, value, "IsSelected"))
			{
				OnPropertyChanged("TileBorder");
			}
		}
	}

	public bool IsHover
	{
		get
		{
			return _isHover;
		}
		set
		{
			if (SetProperty(ref _isHover, value, "IsHover"))
			{
				OnPropertyChanged("StarVisibility");
			}
		}
	}

	public bool IsFavorite
	{
		get
		{
			return _isFavorite;
		}
		set
		{
			if (SetProperty(ref _isFavorite, value, "IsFavorite"))
			{
				Entry.IsFavorite = value;
				OnPropertyChanged("FavoriteGlyph");
				OnPropertyChanged("StarBrush");
				_owner.SetFavoriteAsync(this, value);
				_owner.OnFavoriteChanged();
			}
		}
	}

	public string FavoriteGlyph => IsFavorite ? "\ue735" : "\ue734";

	public Visibility StarVisibility => (!IsHover) ? Visibility.Collapsed : Visibility.Visible;

	public Brush StarBrush => IsFavorite ? new SolidColorBrush(Colors.Gold) : ThemeHelper.SecondaryText();

	public Brush TileBorder
	{
		get
		{
			if (!IsSelected)
			{
				return new SolidColorBrush(Colors.Transparent);
			}
			try
			{
				ResourceDictionary resources = Application.Current.Resources;
				string key = "AccentFillColorDefaultBrush";
				if (resources.TryGetValue(key, out var value) && value is Brush result)
				{
					return result;
				}
			}
			catch
			{
			}
			return new SolidColorBrush(Color.FromArgb(byte.MaxValue, 25, 35, 90));
		}
	}

	public bool IsThumbnailLoading => _thumbnailLoading;

	public DateTime SortKeyCreatedUtc => Entry.CreatedUtc;

	public bool IsCommandRunning
	{
		get
		{
			return _isCommandRunning;
		}
		set
		{
			SetProperty(ref _isCommandRunning, value, "IsCommandRunning");
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand OpenCommand => openCommand ?? (openCommand = new AsyncRelayCommand(OpenAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand CopyImageCommand => copyImageCommand ?? (copyImageCommand = new AsyncRelayCommand(CopyImageAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CopyPathCommand => copyPathCommand ?? (copyPathCommand = new RelayCommand(CopyPath));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand CopyTextCommand => copyTextCommand ?? (copyTextCommand = new AsyncRelayCommand(CopyTextAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand DeleteCommand => deleteCommand ?? (deleteCommand = new AsyncRelayCommand(DeleteAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RenameCommand => renameCommand ?? (renameCommand = new AsyncRelayCommand(RenameAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenContainingFolderCommand => openContainingFolderCommand ?? (openContainingFolderCommand = new RelayCommand(OpenContainingFolder));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenInViewerCommand => openInViewerCommand ?? (openInViewerCommand = new RelayCommand(OpenInViewer));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleFavoriteCommand => toggleFavoriteCommand ?? (toggleFavoriteCommand = new RelayCommand(ToggleFavorite));

	public GalleryItem(SnipViewModel owner, SnipEntry entry)
	{
		_owner = owner;
		Entry = entry;
		_thumbSize = owner?.GalleryThumbSize ?? 220.0;
		Card = new SnipCardState(entry.FilePath, Loader);
		Card.FillMode = owner?.ThumbnailFillMode ?? false;
		Card.PropertyChanged += (object? _, PropertyChangedEventArgs e) =>
		{
			string propertyName = e.PropertyName;
			if ((propertyName == "Image" || propertyName == "ResolutionText") ? true : false)
			{
				OnPropertyChanged("Thumbnail");
				OnPropertyChanged("ResolutionText");
			}
		};
	}

	private Task<SnipGalleryService.SnipThumbnailLoad> Loader(int tier, CancellationToken ct)
	{
		return SnipGalleryService.LoadThumbnailResultAsync(Entry.FilePath, tier, ct);
	}

	public async void EnsureThumbnailAsync(double cardDipSize, double rasterizationScale = 1.0)
	{
		_thumbnailLoading = true;
		try
		{
			await Card.EnsureAsync(cardDipSize, rasterizationScale);
			if ((object)Card.Image != null && Entry.Width == 0 && Card.ResolutionText != "")
			{
				string[] parts = Card.ResolutionText.Split('×');
				if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var w) && int.TryParse(parts[1].Trim(), out var h))
				{
					Entry.Width = w;
					Entry.Height = h;
					OnPropertyChanged("ResolutionText");
				}
			}
		}
		catch
		{
		}
		finally
		{
			_thumbnailLoading = false;
		}
	}

	public void CancelThumbnail()
	{
		Card.Cancel();
	}

	public void OnEntryChanged()
	{
		OnPropertyChanged("Name");
		OnPropertyChanged("FilePath");
	}

	private async Task OpenAsync()
	{
		await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(Entry.FilePath));
	}

	private async Task CopyImageAsync()
	{
		try
		{
			StorageFile file = await StorageFile.GetFileFromPathAsync(Entry.FilePath);
			DataPackage dp = new DataPackage();
			dp.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
			Clipboard.SetContent(dp);
			_owner.NotifyMessage("Copied to clipboard", Name + " copied as image.");
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_owner.NotifyMessage("Copy failed", ex2.Message);
		}
	}

	private void CopyPath()
	{
		DataPackage dataPackage = new DataPackage();
		dataPackage.SetText(Entry.FilePath);
		Clipboard.SetContent(dataPackage);
		_owner.NotifyMessage("Path copied", Entry.FilePath);
	}

	private async Task CopyTextAsync()
	{
		await _owner.ExtractTextAsync(this);
	}

	private async Task DeleteAsync()
	{
		await _owner.DeleteItemsAsync(new GalleryItem[1] { this });
	}

	private async Task RenameAsync()
	{
		await _owner.RequestRenameAsync(this);
	}

	private void OpenContainingFolder()
	{
		_owner.OpenContainingFolder(this);
	}

	private void OpenInViewer()
	{
		try
		{
			((App)Application.Current).Sniper.OpenInViewer(Entry.FilePath);
		}
		catch (Exception ex)
		{
			_owner.NotifyMessage("Viewer failed", ex.Message);
		}
	}

	private void ToggleFavorite()
	{
		IsFavorite = !IsFavorite;
	}
}
