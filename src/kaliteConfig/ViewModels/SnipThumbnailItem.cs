using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using System;
using System.ComponentModel;
using System.IO;
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

	internal static void ThumbLog(string message)
	{
		try
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "kaliteConfig");
			Directory.CreateDirectory(text);
			File.AppendAllText(Path.Combine(text, "snip-thumb.log"), $"{DateTime.Now:HH:mm:ss.fff} [T{Environment.CurrentManagedThreadId}] {message}\n");
		}
		catch
		{
		}
	}

	public async void EnsureThumbnailAsync(double decodeWidth, double rasterizationScale = 1.0)
	{
		try
		{
			await Card.EnsureAsync(decodeWidth, rasterizationScale);
			ThumbLog("recent '" + Path.GetFileName(FilePath) + "' -> " + (((object)Card.Image == null) ? Card.ShortErrorText : "ok"));
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			ThumbLog($"recent '{Path.GetFileName(FilePath)}' EX: {ex2.GetType().Name}: {ex2.Message}");
		}
	}

	public void CancelThumbnail()
	{
		Card.Cancel();
	}
}
