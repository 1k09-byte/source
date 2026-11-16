using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using kaliteConfig.Services;

namespace kaliteConfig.ViewModels;

public sealed class SnipCardState : ObservableObject
{
	public enum Phase
	{
		Loading,
		Loaded,
		Failed,
		Missing
	}

	private readonly Func<int, CancellationToken, Task<SnipGalleryService.SnipThumbnailLoad>> _loader;

	private CancellationTokenSource? _cts;

	private bool _inFlight;

	private double _lastScale = 1.0;

	private double _lastCardPixels;

	private Phase _state = Phase.Loading;

	private BitmapImage? _image;

	private string _fullErrorText = "";

	private bool _looksBlank;

	private string _resolutionText = "";

	private int _pixelWidth;

	private int _pixelHeight;

	private bool _fillMode;

	public string FilePath { get; }

	public Phase State
	{
		get
		{
			return _state;
		}
		private set
		{
			if (SetProperty(ref _state, value, "State"))
			{
				RaiseDerived();
			}
		}
	}

	public BitmapImage? Image
	{
		get
		{
			return _image;
		}
		private set
		{
			if (SetProperty(ref _image, value, "Image"))
			{
				RaiseDerived();
			}
		}
	}

	public string FullErrorText
	{
		get
		{
			return _fullErrorText;
		}
		private set
		{
			if (SetProperty(ref _fullErrorText, value, "FullErrorText"))
			{
				OnPropertyChanged("ShortErrorText");
			}
		}
	}

	public string ShortErrorText
	{
		get
		{
			string result;
			if (State == Phase.Missing)
			{
				result = "File missing";
			}
			else
			{
				result = (string.IsNullOrWhiteSpace(_fullErrorText) ? "Preview failed" : _fullErrorText);
			}
			return result;
		}
	}

	public bool LooksBlank
	{
		get
		{
			return _looksBlank;
		}
		private set
		{
			if (SetProperty(ref _looksBlank, value, "LooksBlank"))
			{
				OnPropertyChanged("BlankBadgeVisibility");
			}
		}
	}

	public string ResolutionText
	{
		get
		{
			return _resolutionText;
		}
		private set
		{
			SetProperty(ref _resolutionText, value, "ResolutionText");
		}
	}

	public int RequestedTier { get; private set; }

	public bool FillMode
	{
		get
		{
			return _fillMode;
		}
		set
		{
			if (SetProperty(ref _fillMode, value, "FillMode"))
			{
				RaiseDerived();
			}
		}
	}

	public bool IsLoading => State == Phase.Loading;

	public bool IsFailed => State == Phase.Failed;

	public bool IsMissing => State == Phase.Missing;

	public Visibility ImageVisibility => ((object)Image == null) ? Visibility.Collapsed : Visibility.Visible;

	public Visibility SkeletonVisibility => ((object)Image != null || State != Phase.Loading) ? Visibility.Collapsed : Visibility.Visible;

	public Visibility ErrorVisibility
	{
		get
		{
			bool flag = (object)Image == null;
			bool flag2 = flag;
			if (flag2)
			{
				Phase state = State;
				bool flag3 = (uint)(state - 2) <= 1u;
				flag2 = flag3;
			}
			return (!flag2) ? Visibility.Collapsed : Visibility.Visible;
		}
	}

	public Visibility RetryVisibility => ((object)Image != null || State != Phase.Failed) ? Visibility.Collapsed : Visibility.Visible;

	public Visibility RemoveMissingVisibility => ((object)Image != null || State != Phase.Missing) ? Visibility.Collapsed : Visibility.Visible;

	public Visibility BlankBadgeVisibility => (!_looksBlank || (object)Image == null) ? Visibility.Collapsed : Visibility.Visible;

	public bool OneToOne => (object)Image != null && _lastCardPixels > 0.0 && (double)_pixelWidth <= _lastCardPixels && (double)_pixelHeight <= _lastCardPixels;

	public Stretch ImageStretch => FillMode ? Stretch.UniformToFill : ((!OneToOne) ? Stretch.Uniform : Stretch.None);

	public double ImageWidth => OneToOne ? ((double)_pixelWidth / ((_lastScale <= 0.0) ? 1.0 : _lastScale)) : double.NaN;

	public double ImageHeight => OneToOne ? ((double)_pixelHeight / ((_lastScale <= 0.0) ? 1.0 : _lastScale)) : double.NaN;

	public event Action? Loaded;

	public SnipCardState(string filePath, Func<int, CancellationToken, Task<SnipGalleryService.SnipThumbnailLoad>> loader)
	{
		FilePath = filePath;
		_loader = loader;
	}

	public async Task EnsureAsync(double cardDipSize, double rasterizationScale, bool force = false)
	{
		double scale = ((rasterizationScale <= 0.0) ? 1.0 : rasterizationScale);
		int required = SnipThumbnailService.RequiredPhysicalPixels(cardDipSize, scale);
		int tier = SnipThumbnailService.PickTier(required);
		_lastScale = scale;
		_lastCardPixels = required;
		if (!force && (object)Image != null && tier <= RequestedTier)
		{
			RaiseDerived();
		}
		else
		{
			if (_inFlight)
			{
				return;
			}
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = new CancellationTokenSource();
			CancellationToken ct = _cts.Token;
			_inFlight = true;
			try
			{
				if ((object)Image == null)
				{
					State = Phase.Loading;
				}
				SnipGalleryService.SnipThumbnailLoad result = await _loader(tier, ct).ConfigureAwait(continueOnCapturedContext: true);
				if (ct.IsCancellationRequested)
				{
					return;
				}
				if (result.Ok)
				{
					Image = result.Image;
					_pixelWidth = result.PixelWidth;
					_pixelHeight = result.PixelHeight;
					RequestedTier = result.Tier;
					LooksBlank = result.LooksBlank;
					if (result.Width > 0 && result.Height > 0)
					{
						ResolutionText = $"{result.Width} × {result.Height}";
					}
					FullErrorText = "";
					State = Phase.Loaded;
					Loaded?.Invoke();
				}
				else if (!string.IsNullOrEmpty(result.Error))
				{
					FullErrorText = result.Error;
					State = (result.Missing ? Phase.Missing : Phase.Failed);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex2)
			{
				Exception ex3 = ex2;
				FullErrorText = ex3.Message;
				if ((object)Image == null)
				{
					State = Phase.Failed;
				}
			}
			finally
			{
				_inFlight = false;
			}
		}
	}

	public Task RetryAsync(double cardDipSize, double rasterizationScale)
	{
		SnipThumbnailService.Invalidate(FilePath);
		Image = null;
		State = Phase.Loading;
		return EnsureAsync(cardDipSize, rasterizationScale, force: true);
	}

	public void Cancel()
	{
		try
		{
			_cts?.Cancel();
		}
		catch
		{
		}
	}

	private void RaiseDerived()
	{
		OnPropertyChanged("IsLoading");
		OnPropertyChanged("IsFailed");
		OnPropertyChanged("IsMissing");
		OnPropertyChanged("ImageVisibility");
		OnPropertyChanged("SkeletonVisibility");
		OnPropertyChanged("ErrorVisibility");
		OnPropertyChanged("RetryVisibility");
		OnPropertyChanged("RemoveMissingVisibility");
		OnPropertyChanged("BlankBadgeVisibility");
		OnPropertyChanged("ShortErrorText");
		OnPropertyChanged("ImageStretch");
		OnPropertyChanged("ImageWidth");
		OnPropertyChanged("ImageHeight");
	}
}
