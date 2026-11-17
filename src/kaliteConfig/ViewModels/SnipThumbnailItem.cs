using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using kaliteConfig.Services;

namespace kaliteConfig.ViewModels;

public class SnipThumbnailItem : ObservableObject
{
	public string Filename { get; set; } = "";

	public string FilePath { get; set; } = "";

	public SnipCardState Card { get; }

	public BitmapImage? Thumbnail => Card.Image;

	public string ResolutionText => Card.ResolutionText;

	public string RelativeTimeText => SnipRelativeTime.Format(FilePath);

	public SnipThumbnailItem()
	{
		Card = new SnipCardState(FilePath, Loader);
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
		return SnipGalleryService.LoadThumbnailResultAsync(FilePath, tier, ct);
	}

	public async void EnsureThumbnailAsync(double decodeWidth, double rasterizationScale = 1.0)
	{
		try
		{
			await Card.EnsureAsync(decodeWidth, rasterizationScale);
		}
		catch
		{
		}
	}

	public void CancelThumbnail()
	{
		Card.Cancel();
	}
}
