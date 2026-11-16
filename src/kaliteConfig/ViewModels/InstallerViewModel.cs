using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using WinRT;
using WinRT.kaliteConfigVtableClasses;
using kaliteConfig.Models;
using kaliteConfig.Services;
using kaliteConfig.Services.Notifications;

namespace kaliteConfig.ViewModels;

public class InstallerViewModel : ObservableObject
{
	private readonly IInstallerService _installerService;

	private CancellationTokenSource? _cts;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand<BrowserInstallItem?>? installBrowserCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand<BrowserInstallItem?>? updateBrowserCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand<BrowserInstallItem?>? uninstallBrowserCommand;


	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? cancelInstallCommand;

	public ObservableCollection<BrowserInstallItem> Browsers { get; } = new ObservableCollection<BrowserInstallItem>();

	public ObservableCollection<BrowserInstallItem> GameLaunchers { get; } = new ObservableCollection<BrowserInstallItem>();

	public ObservableCollection<BrowserInstallItem> SocialApps { get; } = new ObservableCollection<BrowserInstallItem>();

	public ObservableCollection<BrowserInstallItem> Utilities { get; } = new ObservableCollection<BrowserInstallItem>();

	public ObservableCollection<BrowserInstallItem> SystemTools { get; } = new ObservableCollection<BrowserInstallItem>();

	public ObservableCollection<BrowserInstallItem> Development { get; } = new ObservableCollection<BrowserInstallItem>();

	public ObservableCollection<BrowserInstallItem> VisibleUtilities { get; } = new ObservableCollection<BrowserInstallItem>();

	public ObservableCollection<BrowserInstallItem> VisibleSystemTools { get; } = new ObservableCollection<BrowserInstallItem>();

	public ObservableCollection<BrowserInstallItem> VisibleDevelopment { get; } = new ObservableCollection<BrowserInstallItem>();

	public ObservableCollection<BrowserInstallItem> VisibleBrowsers { get; } = new ObservableCollection<BrowserInstallItem>();

	public ObservableCollection<BrowserInstallItem> VisibleGameLaunchers { get; } = new ObservableCollection<BrowserInstallItem>();

	public ObservableCollection<BrowserInstallItem> VisibleSocialApps { get; } = new ObservableCollection<BrowserInstallItem>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowInstalled
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("ShowInstalled");
				field = value;
				OnShowInstalledChanged(value);
				OnPropertyChanged("ShowInstalled");
			}
		}
	} = false;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool NothingToInstall
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("NothingToInstall");
				field = value;
				OnPropertyChanged("NothingToInstall");
			}
		}
	} = false;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool NoSearchMatches
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("NoSearchMatches");
				field = value;
				OnPropertyChanged("NoSearchMatches");
			}
		}
	} = false;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SearchQuery
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(field, value))
			{
				OnPropertyChanging("SearchQuery");
				field = value;
				OnSearchQueryChanged(value);
				OnPropertyChanged("SearchQuery");
			}
		}
	} = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasBrowsers
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("HasBrowsers");
				field = value;
				OnPropertyChanged("HasBrowsers");
			}
		}
	} = false;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasLaunchers
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("HasLaunchers");
				field = value;
				OnPropertyChanged("HasLaunchers");
			}
		}
	} = false;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasSocials
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("HasSocials");
				field = value;
				OnPropertyChanged("HasSocials");
			}
		}
	} = false;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasUtilities
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("HasUtilities");
				field = value;
				OnPropertyChanged("HasUtilities");
			}
		}
	} = false;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasSystemTools
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("HasSystemTools");
				field = value;
				OnPropertyChanged("HasSystemTools");
			}
		}
	} = false;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasDevelopment
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("HasDevelopment");
				field = value;
				OnPropertyChanged("HasDevelopment");
			}
		}
	} = false;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string FilterSummary
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(field, value))
			{
				OnPropertyChanging("FilterSummary");
				field = value;
				OnPropertyChanged("FilterSummary");
			}
		}
	} = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public BrowserInstallItem? SelectedBrowser
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<BrowserInstallItem>.Default.Equals(field, value))
			{
				OnPropertyChanging("SelectedBrowser");
				field = value;
				OnPropertyChanged("SelectedBrowser");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsInstalling
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsInstalling");
				field = value;
				OnPropertyChanged("IsInstalling");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string InstallStatusText
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(field, value))
			{
				OnPropertyChanging("InstallStatusText");
				field = value;
				OnPropertyChanged("InstallStatusText");
			}
		}
	} = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string UpdateStatusText
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(field, value))
			{
				OnPropertyChanging("UpdateStatusText");
				field = value;
				OnPropertyChanged("UpdateStatusText");
			}
		}
	} = string.Empty;

	public bool HasUpdates => Browsers.Concat(GameLaunchers).Concat(SocialApps).Concat(Utilities)
		.Concat(SystemTools)
		.Concat(Development)
		.Any((BrowserInstallItem i) => i.UpdateAvailable);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand<BrowserInstallItem?> InstallBrowserCommand => installBrowserCommand ?? (installBrowserCommand = new AsyncRelayCommand<BrowserInstallItem>(InstallBrowserAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand<BrowserInstallItem?> UpdateBrowserCommand => updateBrowserCommand ?? (updateBrowserCommand = new AsyncRelayCommand<BrowserInstallItem>(UpdateBrowserAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand<BrowserInstallItem?> UninstallBrowserCommand => uninstallBrowserCommand ?? (uninstallBrowserCommand = new AsyncRelayCommand<BrowserInstallItem>(UninstallBrowserAsync));


	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CancelInstallCommand => cancelInstallCommand ?? (cancelInstallCommand = new RelayCommand(CancelInstall));

	private bool ShouldShow(BrowserInstallItem item)
	{
		if (!MatchesSearch(item))
		{
			return false;
		}
		if (item.Status == BrowserInstallStatus.UpdateAvailable)
		{
			return true;
		}
		return ShowInstalled || (item.Status != BrowserInstallStatus.AlreadyInstalled && item.Status != BrowserInstallStatus.Installed);
	}

	private bool MatchesSearch(BrowserInstallItem item)
	{
		string text = SearchQuery.Trim();
		if (text.Length == 0)
		{
			return true;
		}
		return item.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || item.Description.Contains(text, StringComparison.OrdinalIgnoreCase) || item.ProductId.Contains(text, StringComparison.OrdinalIgnoreCase);
	}

	private void UpdateVisibilityFilters()
	{
		VisibleBrowsers.Clear();
		foreach (BrowserInstallItem browser in Browsers)
		{
			browser.IsVisible = ShouldShow(browser);
			if (browser.IsVisible)
			{
				VisibleBrowsers.Add(browser);
			}
		}
		VisibleGameLaunchers.Clear();
		foreach (BrowserInstallItem gameLauncher in GameLaunchers)
		{
			gameLauncher.IsVisible = ShouldShow(gameLauncher);
			if (gameLauncher.IsVisible)
			{
				VisibleGameLaunchers.Add(gameLauncher);
			}
		}
		VisibleSocialApps.Clear();
		foreach (BrowserInstallItem socialApp in SocialApps)
		{
			socialApp.IsVisible = ShouldShow(socialApp);
			if (socialApp.IsVisible)
			{
				VisibleSocialApps.Add(socialApp);
			}
		}
		VisibleUtilities.Clear();
		foreach (BrowserInstallItem utility in Utilities)
		{
			utility.IsVisible = ShouldShow(utility);
			if (utility.IsVisible)
			{
				VisibleUtilities.Add(utility);
			}
		}
		VisibleSystemTools.Clear();
		foreach (BrowserInstallItem systemTool in SystemTools)
		{
			systemTool.IsVisible = ShouldShow(systemTool);
			if (systemTool.IsVisible)
			{
				VisibleSystemTools.Add(systemTool);
			}
		}
		VisibleDevelopment.Clear();
		foreach (BrowserInstallItem item in Development)
		{
			item.IsVisible = ShouldShow(item);
			if (item.IsVisible)
			{
				VisibleDevelopment.Add(item);
			}
		}
		HasBrowsers = VisibleBrowsers.Count > 0;
		HasLaunchers = VisibleGameLaunchers.Count > 0;
		HasSocials = VisibleSocialApps.Count > 0;
		HasUtilities = VisibleUtilities.Count > 0;
		HasSystemTools = VisibleSystemTools.Count > 0;
		HasDevelopment = VisibleDevelopment.Count > 0;
		int num = VisibleBrowsers.Count + VisibleGameLaunchers.Count + VisibleSocialApps.Count + VisibleUtilities.Count + VisibleSystemTools.Count + VisibleDevelopment.Count;
		string text = SearchQuery.Trim();
		bool flag = text.Length > 0;
		NothingToInstall = num == 0 && !flag;
		NoSearchMatches = (num == 0) & flag;
		InstallerViewModel installerViewModel = this;
		string filterSummary;
		if (num != 0)
		{
			if (flag)
			{
				filterSummary = $"{num} {((num == 1) ? "match" : "matches")} for \"{text}\"";
			}
			else
			{
				filterSummary = (ShowInstalled ? $"{num} {((num == 1) ? "app" : "apps")}" : $"{num} {((num == 1) ? "app" : "apps")} ready to install");
			}
		}
		else
		{
			filterSummary = (flag ? ("No apps match \"" + text + "\"") : "Everything is installed");
		}
		installerViewModel.FilterSummary = filterSummary;
	}

	public InstallerViewModel(IInstallerService installerService)
	{
		_installerService = installerService;
		BrowserInstallItem browserInstallItem = new BrowserInstallItem
		{
			Name = "Zen Browser",
			ProductId = "Zen",
			ImagePath = "ms-appx:///Assets/zen-logo.png",
			DownloadUrl = "https://github.com/zen-browser/desktop/releases/latest/download/zen.installer.exe",
			SilentInstallArgs = "/S",
			InstallerFileName = "zen.installer.exe",
			InstalledCheckPath = "%PROGRAMFILES%\\Zen Browser\\zen.exe;%LOCALAPPDATA%\\Programs\\Zen Browser\\zen.exe;%LOCALAPPDATA%\\zen\\zen.exe",
			Description = "Zen Browser is a privacy-focused browser built to keep you safe and fast online."
		};
		browserInstallItem.Extensions.Add(new ExtensionItem
		{
			Name = "uBlock Origin",
			FirefoxAddonSlug = "ublock-origin",
			Description = "Blocks ads and trackers across every site you visit."
		});
		browserInstallItem.Extensions.Add(new ExtensionItem
		{
			Name = "Privacy Badger",
			FirefoxAddonSlug = "privacy-badger17",
			Description = "Automatically learns to block hidden trackers as you browse, made by the EFF."
		});
		browserInstallItem.Extensions.Add(new ExtensionItem
		{
			Name = "Location Guard",
			FirefoxAddonSlug = "location-guard",
			Description = "Hides your precise geographic location from websites by adding noise to it."
		});
		browserInstallItem.Extensions.Add(new ExtensionItem
		{
			Name = "I Still Don't Care About Cookies",
			FirefoxAddonSlug = "istilldontcareaboutcookies",
			Description = "Automatically dismisses cookie consent banners on most websites."
		});
		browserInstallItem.Extensions.Add(new ExtensionItem
		{
			Name = "Decentraleyes",
			FirefoxAddonSlug = "decentraleyes",
			Description = "Protects against tracking through free, centralized content delivery by serving local files."
		});
		browserInstallItem.Extensions.Add(new ExtensionItem
		{
			Name = "ClearURLs",
			FirefoxAddonSlug = "clearurls",
			Description = "Automatically removes tracking parameters from URLs."
		});
		browserInstallItem.Extensions.Add(new ExtensionItem
		{
			Name = "LocalCDN",
			FirefoxAddonSlug = "localcdn-fork-of-decentraleyes",
			Description = "Replaces CDN-delivered frameworks with local copies to cut tracking."
		});
		browserInstallItem.Extensions.Add(new ExtensionItem
		{
			Name = "Buster",
			FirefoxAddonSlug = "buster-captcha-solver",
			Description = "Solves image and audio captchas for you with one click."
		});
		browserInstallItem.Extensions.Add(new ExtensionItem
		{
			Name = "Consent-O-Matic",
			FirefoxAddonSlug = "consent-o-matic",
			Description = "Automatically answers cookie and GDPR popups to protect your privacy."
		});
		browserInstallItem.Extensions.Add(new ExtensionItem
		{
			Name = "SponsorBlock",
			FirefoxAddonSlug = "sponsorblock",
			Description = "Skips sponsorships, intros and filler in YouTube videos."
		});
		browserInstallItem.Extensions.Add(new ExtensionItem
		{
			Name = "uBlacklist",
			FirefoxAddonSlug = "ublacklist",
			Description = "Blocks chosen sites from appearing in your search results."
		});
		browserInstallItem.Extensions.Add(new ExtensionItem
		{
			Name = "Improve YouTube",
			FirefoxAddonSlug = "youtube-addon",
			Description = "Powerful YouTube tweaks: player size, autoplay, speed and more."
		});
		browserInstallItem.Extensions.Add(new ExtensionItem
		{
			Name = "Reload All Tabs",
			FirefoxAddonSlug = "reload-all-tabs",
			Description = "Reloads every open tab with one click."
		});
		BrowserInstallItem browserInstallItem2 = new BrowserInstallItem
		{
			Name = "Brave Browser",
			ProductId = "Brave",
			ImagePath = "ms-appx:///Assets/brave-logo.png",
			DownloadUrl = "https://brave-browser-downloads.s3.brave.com/latest/brave_installer-x64.exe",
			SilentInstallArgs = "--silent --install",
			InstallerFileName = "brave_installer-x64.exe",
			InstalledCheckPath = "%PROGRAMFILES%\\BraveSoftware\\Brave-Browser\\Application\\brave.exe;%PROGRAMFILES(X86)%\\BraveSoftware\\Brave-Browser\\Application\\brave.exe;%LOCALAPPDATA%\\BraveSoftware\\Brave-Browser\\Application\\brave.exe",
			Description = "Brave is a free and open-source web browser developed by Brave Software, Inc. based on the Chromium web browser."
		};
		browserInstallItem2.Extensions.Add(new ExtensionItem
		{
			Name = "uBlock Origin Lite",
			ChromiumExtensionId = "ddkjiahejlhfcafbddmgiahcphecmpfh",
			Description = "Blocks ads and trackers across every site you visit. (Fallback since MV2 uBlock Origin was removed from CWS)"
		});
		browserInstallItem2.Extensions.Add(new ExtensionItem
		{
			Name = "Privacy Badger",
			ChromiumExtensionId = "pkehgijcmpdhfbdbbnkijodmdjhbjlgp",
			Description = "Automatically learns to block hidden trackers. (Blocked by Brave natively / Redundant with Shields)",
			IsAvailable = false,
			IsSelected = false
		});
		browserInstallItem2.Extensions.Add(new ExtensionItem
		{
			Name = "Location Guard",
			ChromiumExtensionId = "cfohepagpmnodfdmjliccbbigdkfcgia",
			Description = "Hides your precise geographic location from websites by adding noise to it. (Unavailable - MV2 delisted from CWS)",
			IsAvailable = false,
			IsSelected = false
		});
		browserInstallItem2.Extensions.Add(new ExtensionItem
		{
			Name = "WebRTC Leak Shield",
			ChromiumExtensionId = "bppamachkoflopbagkdoflbgfjflfnfl",
			Description = "Prevents WebRTC from leaking your real IP address, even behind a VPN."
		});
		browserInstallItem2.Extensions.Add(new ExtensionItem
		{
			Name = "I Still Don't Care About Cookies",
			ChromiumExtensionId = "edibdbjcniadpccecjdfdjjppcpchdlm",
			Description = "Automatically dismisses cookie consent banners on most websites."
		});
		browserInstallItem2.Extensions.Add(new ExtensionItem
		{
			Name = "Decentraleyes",
			ChromiumExtensionId = "ldpochfccmkkmhdbclfhpagapcfdljkj",
			Description = "Protects against tracking through free, centralized content delivery by serving local files."
		});
		browserInstallItem2.Extensions.Add(new ExtensionItem
		{
			Name = "ClearURLs",
			ChromiumExtensionId = "lckanjgmijmafbedllaakclkaicjfmnk",
			Description = "Automatically removes tracking parameters from URLs. (Unavailable - MV2 delisted from CWS)",
			IsAvailable = false,
			IsSelected = false
		});
		browserInstallItem2.Extensions.Add(new ExtensionItem
		{
			Name = "LocalCDN",
			Description = "Replaces CDN-delivered frameworks with local copies. (Unavailable - delisted from CWS, Firefox only)",
			IsAvailable = false,
			IsSelected = false
		});
		browserInstallItem2.Extensions.Add(new ExtensionItem
		{
			Name = "Buster",
			ChromiumExtensionId = "mpbjkejclgfgadiemmefgebjfooflfhl",
			Description = "Solves image and audio captchas for you with one click."
		});
		browserInstallItem2.Extensions.Add(new ExtensionItem
		{
			Name = "Consent-O-Matic",
			ChromiumExtensionId = "mdjildafknihdffpkfmmpnpoiajfjnjd",
			Description = "Automatically answers cookie and GDPR popups to protect your privacy."
		});
		browserInstallItem2.Extensions.Add(new ExtensionItem
		{
			Name = "SponsorBlock",
			ChromiumExtensionId = "mnjggcdmjocbbbhaepdhchncahnbgone",
			Description = "Skips sponsorships, intros and filler in YouTube videos."
		});
		browserInstallItem2.Extensions.Add(new ExtensionItem
		{
			Name = "Mercury",
			ChromiumExtensionId = "jecgkhhikedmghacamdomkngdfdodbcb",
			Description = "Type-to-search your open tabs (Ctrl+Shift+L)."
		});
		browserInstallItem2.Extensions.Add(new ExtensionItem
		{
			Name = "uBlacklist",
			ChromiumExtensionId = "pncfbmialoiaghdehhbnbhkkgmjanfhe",
			Description = "Blocks chosen sites from appearing in your search results."
		});
		browserInstallItem2.Extensions.Add(new ExtensionItem
		{
			Name = "Improve YouTube",
			ChromiumExtensionId = "bnomihfieiccainjcjblhegjgglakjdd",
			Description = "Powerful YouTube tweaks: player size, autoplay, speed and more."
		});
		browserInstallItem2.Extensions.Add(new ExtensionItem
		{
			Name = "Reload All Tabs",
			ChromiumExtensionId = "biidfoapgncnlhpdhjapkkbankbnnhnj",
			Description = "Reloads every open tab with one click."
		});
		BrowserInstallItem browserInstallItem3 = new BrowserInstallItem
		{
			Name = "Vivaldi",
			ProductId = "Vivaldi",
			ImagePath = "ms-appx:///Assets/vivaldi-logo.png",
			DownloadUrl = "https://vivaldi.com/download/Vivaldi.x64.exe",
			SilentInstallArgs = "--vivaldi-silent --system-level",
			InstallerFileName = "vivaldi_installer.exe",
			InstalledCheckPath = "%PROGRAMFILES%\\Vivaldi\\Application\\vivaldi.exe;%LOCALAPPDATA%\\Vivaldi\\Application\\vivaldi.exe",
			Description = "A fast, highly customizable privacy browser built on Chromium."
		};
		BrowserInstallItem browserInstallItem4 = new BrowserInstallItem
		{
			Name = "Helium Browser",
			ProductId = "Helium",
			ImagePath = "ms-appx:///Assets/helium-logo.svg",
			DownloadUrl = "https://github.com/imputnet/helium-windows/releases/download/0.16.5.1/helium_0.16.5.1_x64-installer.exe",
			SilentInstallArgs = "/S",
			InstallerFileName = "helium_installer.exe",
			InstalledCheckPath = "%LOCALAPPDATA%\\imput\\Helium\\Application\\chrome.exe;%LOCALAPPDATA%\\imput\\Helium\\Application\\helium.exe;%LOCALAPPDATA%\\Programs\\Helium\\Helium.exe;%LOCALAPPDATA%\\Programs\\Helium\\chrome.exe;%PROGRAMFILES%\\imput\\Helium\\Application\\chrome.exe;%PROGRAMFILES%\\Helium\\Application\\chrome.exe;%PROGRAMFILES%\\Helium\\Helium.exe;%PROGRAMFILES(X86)%\\imput\\Helium\\Application\\chrome.exe;%PROGRAMFILES(X86)%\\Helium\\Helium.exe;%LOCALAPPDATA%\\Helium\\Application\\helium.exe",
			Description = "An open-source, bloat-free browser based on ungoogled-chromium designed purely for maximum privacy."
		};
		browserInstallItem4.Extensions.Add(new ExtensionItem
		{
			Name = "uBlock Origin Lite",
			ChromiumExtensionId = "ddkjiahejlhfcafbddmgiahcphecmpfh",
			Description = "Blocks ads and trackers across every site you visit. (Fallback since MV2 uBlock Origin was removed from CWS)"
		});
		browserInstallItem4.Extensions.Add(new ExtensionItem
		{
			Name = "Privacy Badger",
			ChromiumExtensionId = "pkehgijcmpdhfbdbbnkijodmdjhbjlgp",
			Description = "Automatically learns to block hidden trackers as you browse, made by the EFF."
		});
		browserInstallItem4.Extensions.Add(new ExtensionItem
		{
			Name = "Location Guard",
			ChromiumExtensionId = "cfohepagpmnodfdmjliccbbigdkfcgia",
			Description = "Hides your precise geographic location from websites by adding noise to it. (Unavailable - MV2 delisted from CWS)",
			IsAvailable = false,
			IsSelected = false
		});
		browserInstallItem4.Extensions.Add(new ExtensionItem
		{
			Name = "WebRTC Leak Shield",
			ChromiumExtensionId = "bppamachkoflopbagkdoflbgfjflfnfl",
			Description = "Prevents WebRTC from leaking your real IP address, even behind a VPN."
		});
		browserInstallItem4.Extensions.Add(new ExtensionItem
		{
			Name = "I Still Don't Care About Cookies",
			ChromiumExtensionId = "edibdbjcniadpccecjdfdjjppcpchdlm",
			Description = "Automatically dismisses cookie consent banners on most websites."
		});
		browserInstallItem4.Extensions.Add(new ExtensionItem
		{
			Name = "Decentraleyes",
			ChromiumExtensionId = "ldpochfccmkkmhdbclfhpagapcfdljkj",
			Description = "Protects against tracking through free, centralized content delivery by serving local files."
		});
		browserInstallItem4.Extensions.Add(new ExtensionItem
		{
			Name = "ClearURLs",
			ChromiumExtensionId = "lckanjgmijmafbedllaakclkaicjfmnk",
			Description = "Automatically removes tracking parameters from URLs. (Unavailable - MV2 delisted from CWS)",
			IsAvailable = false,
			IsSelected = false
		});
		browserInstallItem4.Extensions.Add(new ExtensionItem
		{
			Name = "LocalCDN",
			Description = "Replaces CDN-delivered frameworks with local copies. (Unavailable - delisted from CWS, Firefox only)",
			IsAvailable = false,
			IsSelected = false
		});
		browserInstallItem4.Extensions.Add(new ExtensionItem
		{
			Name = "Buster",
			ChromiumExtensionId = "mpbjkejclgfgadiemmefgebjfooflfhl",
			Description = "Solves image and audio captchas for you with one click."
		});
		browserInstallItem4.Extensions.Add(new ExtensionItem
		{
			Name = "Consent-O-Matic",
			ChromiumExtensionId = "mdjildafknihdffpkfmmpnpoiajfjnjd",
			Description = "Automatically answers cookie and GDPR popups to protect your privacy."
		});
		browserInstallItem4.Extensions.Add(new ExtensionItem
		{
			Name = "SponsorBlock",
			ChromiumExtensionId = "mnjggcdmjocbbbhaepdhchncahnbgone",
			Description = "Skips sponsorships, intros and filler in YouTube videos."
		});
		browserInstallItem4.Extensions.Add(new ExtensionItem
		{
			Name = "Mercury",
			ChromiumExtensionId = "jecgkhhikedmghacamdomkngdfdodbcb",
			Description = "Type-to-search your open tabs (Ctrl+Shift+L)."
		});
		browserInstallItem4.Extensions.Add(new ExtensionItem
		{
			Name = "uBlacklist",
			ChromiumExtensionId = "pncfbmialoiaghdehhbnbhkkgmjanfhe",
			Description = "Blocks chosen sites from appearing in your search results."
		});
		browserInstallItem4.Extensions.Add(new ExtensionItem
		{
			Name = "Improve YouTube",
			ChromiumExtensionId = "bnomihfieiccainjcjblhegjgglakjdd",
			Description = "Powerful YouTube tweaks: player size, autoplay, speed and more."
		});
		browserInstallItem4.Extensions.Add(new ExtensionItem
		{
			Name = "Reload All Tabs",
			ChromiumExtensionId = "biidfoapgncnlhpdhjapkkbankbnnhnj",
			Description = "Reloads every open tab with one click."
		});
		browserInstallItem3.Extensions.Add(new ExtensionItem
		{
			Name = "uBlock Origin Lite",
			ChromiumExtensionId = "ddkjiahejlhfcafbddmgiahcphecmpfh",
			Description = "Blocks ads and trackers across every site you visit. (Fallback since MV2 uBlock Origin was removed from CWS)"
		});
		browserInstallItem3.Extensions.Add(new ExtensionItem
		{
			Name = "Privacy Badger",
			ChromiumExtensionId = "pkehgijcmpdhfbdbbnkijodmdjhbjlgp",
			Description = "Automatically learns to block hidden trackers as you browse, made by the EFF."
		});
		browserInstallItem3.Extensions.Add(new ExtensionItem
		{
			Name = "Location Guard",
			ChromiumExtensionId = "cfohepagpmnodfdmjliccbbigdkfcgia",
			Description = "Hides your precise geographic location from websites by adding noise to it. (Unavailable - MV2 delisted from CWS)",
			IsAvailable = false,
			IsSelected = false
		});
		browserInstallItem3.Extensions.Add(new ExtensionItem
		{
			Name = "WebRTC Leak Shield",
			ChromiumExtensionId = "bppamachkoflopbagkdoflbgfjflfnfl",
			Description = "Prevents WebRTC from leaking your real IP address, even behind a VPN."
		});
		browserInstallItem3.Extensions.Add(new ExtensionItem
		{
			Name = "I Still Don't Care About Cookies",
			ChromiumExtensionId = "edibdbjcniadpccecjdfdjjppcpchdlm",
			Description = "Automatically dismisses cookie consent banners on most websites."
		});
		browserInstallItem3.Extensions.Add(new ExtensionItem
		{
			Name = "Decentraleyes",
			ChromiumExtensionId = "ldpochfccmkkmhdbclfhpagapcfdljkj",
			Description = "Protects against tracking through free, centralized content delivery by serving local files."
		});
		browserInstallItem3.Extensions.Add(new ExtensionItem
		{
			Name = "ClearURLs",
			ChromiumExtensionId = "lckanjgmijmafbedllaakclkaicjfmnk",
			Description = "Automatically removes tracking parameters from URLs. (Unavailable - MV2 delisted from CWS)",
			IsAvailable = false,
			IsSelected = false
		});
		browserInstallItem3.Extensions.Add(new ExtensionItem
		{
			Name = "LocalCDN",
			Description = "Replaces CDN-delivered frameworks with local copies. (Unavailable - delisted from CWS, Firefox only)",
			IsAvailable = false,
			IsSelected = false
		});
		browserInstallItem3.Extensions.Add(new ExtensionItem
		{
			Name = "Buster",
			ChromiumExtensionId = "mpbjkejclgfgadiemmefgebjfooflfhl",
			Description = "Solves image and audio captchas for you with one click."
		});
		browserInstallItem3.Extensions.Add(new ExtensionItem
		{
			Name = "Consent-O-Matic",
			ChromiumExtensionId = "mdjildafknihdffpkfmmpnpoiajfjnjd",
			Description = "Automatically answers cookie and GDPR popups to protect your privacy."
		});
		browserInstallItem3.Extensions.Add(new ExtensionItem
		{
			Name = "SponsorBlock",
			ChromiumExtensionId = "mnjggcdmjocbbbhaepdhchncahnbgone",
			Description = "Skips sponsorships, intros and filler in YouTube videos."
		});
		browserInstallItem3.Extensions.Add(new ExtensionItem
		{
			Name = "Mercury",
			ChromiumExtensionId = "jecgkhhikedmghacamdomkngdfdodbcb",
			Description = "Type-to-search your open tabs (Ctrl+Shift+L)."
		});
		browserInstallItem3.Extensions.Add(new ExtensionItem
		{
			Name = "uBlacklist",
			ChromiumExtensionId = "pncfbmialoiaghdehhbnbhkkgmjanfhe",
			Description = "Blocks chosen sites from appearing in your search results."
		});
		browserInstallItem3.Extensions.Add(new ExtensionItem
		{
			Name = "Improve YouTube",
			ChromiumExtensionId = "bnomihfieiccainjcjblhegjgglakjdd",
			Description = "Powerful YouTube tweaks: player size, autoplay, speed and more."
		});
		browserInstallItem3.Extensions.Add(new ExtensionItem
		{
			Name = "Reload All Tabs",
			ChromiumExtensionId = "biidfoapgncnlhpdhjapkkbankbnnhnj",
			Description = "Reloads every open tab with one click."
		});
		BrowserInstallItem item = new BrowserInstallItem
		{
			Name = "Epic Games Launcher",
			ProductId = "Epic Games Launcher",
			ImagePath = "ms-appx:///Assets/epic-logo.png",
			DownloadUrl = "https://launcher-public-service-prod06.ol.epicgames.com/launcher/api/installer/download/EpicGamesLauncherInstaller.msi",
			SilentInstallArgs = "/qn /norestart",
			InstallerFileName = "epic_installer.msi",
			InstalledCheckPath = "%PROGRAMFILES(X86)%\\Epic Games\\Launcher\\Portal\\Binaries\\Win32\\EpicGamesLauncher.exe;%PROGRAMFILES(X86)%\\Epic Games\\Launcher\\Portal\\Binaries\\Win64\\EpicGamesLauncher.exe",
			Description = "The official Epic Games storefront, featuring Unreal Engine titles, exclusive games, and free weekly releases."
		};
		BrowserInstallItem item2 = new BrowserInstallItem
		{
			Name = "EA app",
			ProductId = "EA app",
			ImagePath = "ms-appx:///Assets/ea-logo.png",
			DownloadUrl = "https://origin-a.akamaihd.net/EA-Desktop-Client-Download/installer-releases/EAappInstaller-13.783.0.6296-15340466.exe",
			SilentInstallArgs = "/quiet",
			InstallerFileName = "ea_installer.exe",
			InstalledCheckPath = "%PROGRAMFILES%\\Electronic Arts\\EA Desktop\\EA Desktop\\EADesktop.exe",
			Description = "The official Electronic Arts ecosystem, built for downloading and playing Battlefield, The Sims, and EA Sports titles."
		};
		BrowserInstallItem item3 = new BrowserInstallItem
		{
			Name = "Ubisoft Connect",
			ProductId = "Ubisoft Connect",
			ImagePath = "ms-appx:///Assets/ubisoft-logo.png",
			DownloadUrl = "https://ubistatic3-a.akamaihd.net/orbit/launcher_installer/UbisoftConnectInstaller.exe",
			SilentInstallArgs = "/S",
			InstallerFileName = "ubisoft_installer.exe",
			InstalledCheckPath = "%PROGRAMFILES(X86)%\\Ubisoft\\Ubisoft Game Launcher\\upc.exe",
			Description = "The central application for managing Ubisoft games, enabling cross-progression, rewards, and deep multiplayer integration."
		};
		BrowserInstallItem item4 = new BrowserInstallItem
		{
			Name = "Minecraft Launcher",
			ProductId = "Minecraft Launcher",
			ImagePath = "ms-appx:///Assets/minecraft-logo.png",
			DownloadUrl = "https://launcher.mojang.com/download/MinecraftInstaller.msi",
			SilentInstallArgs = "/qn",
			InstallerFileName = "minecraft_installer.msi",
			InstalledCheckPath = "%PROGRAMFILES(X86)%\\Minecraft Launcher\\MinecraftLauncher.exe",
			Description = "The unified launcher for Minecraft: Java Edition, Bedrock, and Dungeons, providing quick access to all block-building adventures."
		};
		BrowserInstallItem item5 = new BrowserInstallItem
		{
			Name = "Steam",
			ProductId = "Steam",
			ImagePath = "ms-appx:///Assets/steam-logo.png",
			DownloadUrl = "https://cdn.akamai.steamstatic.com/client/installer/SteamSetup.exe",
			SilentInstallArgs = "/S",
			InstallerFileName = "steam_installer.exe",
			InstalledCheckPath = "%PROGRAMFILES(X86)%\\Steam\\steam.exe",
			Description = "The ultimate gaming platform and community, featuring massive game libraries, workshop mods, and cloud saves operated by Valve."
		};
		BrowserInstallItem item6 = new BrowserInstallItem
		{
			Name = "Discord",
			ProductId = "Discord",
			ImagePath = "ms-appx:///Assets/discord-logo.svg",
			DownloadUrl = "https://discord.com/api/download?platform=win",
			SilentInstallArgs = "-s",
			InstallerFileName = "discord_installer.exe",
			InstalledCheckPath = "%LOCALAPPDATA%\\Discord\\*\\Discord.exe",
			Description = "The go-to voice, video, and text communication service used by gamers and communities worldwide."
		};
		BrowserInstallItem item7 = new BrowserInstallItem
		{
			Name = "Telegram Desktop",
			ProductId = "Telegram",
			ImagePath = "ms-appx:///Assets/telegram-logo.png",
			DownloadUrl = "https://telegram.org/dl/desktop/win64",
			SilentInstallArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
			InstallerFileName = "telegram_installer.exe",
			InstalledCheckPath = "%APPDATA%\\Telegram Desktop\\Telegram.exe",
			Description = "A fast, hyper-secure messaging app with seamless cloud syncing across all your devices."
		};
		BrowserInstallItem item8 = new BrowserInstallItem
		{
			Name = "WhatsApp",
			ProductId = "WhatsApp",
			ImagePath = "ms-appx:///Assets/whatsapp-logo.svg",
			WingetId = "9NKSQGP7F2NH",
			InstalledCheckPath = "%LOCALAPPDATA%\\WhatsApp\\WhatsApp.exe",
			Description = "Seamless cross-platform messaging and calling, keeping you connected straight from your desktop."
		};
		Browsers.Add(browserInstallItem);
		Browsers.Add(browserInstallItem2);
		Browsers.Add(browserInstallItem3);
		Browsers.Add(browserInstallItem4);
		GameLaunchers.Add(item);
		GameLaunchers.Add(item2);
		GameLaunchers.Add(item3);
		GameLaunchers.Add(item4);
		GameLaunchers.Add(item5);
		GameLaunchers.Add(new BrowserInstallItem
		{
			Name = "Froststrap",
			ImagePath = "ms-appx:///Assets/froststrap-logo.png",
			WingetId = "Froststrap.Froststrap",
			InstalledCheckPath = "%LOCALAPPDATA%\\Froststrap\\Froststrap.exe",
			Description = "Fast Roblox bootstrapper focused on performance and customization."
		});
		SocialApps.Add(item6);
		SocialApps.Add(item7);
		SocialApps.Add(item8);
		SocialApps.Add(new BrowserInstallItem
		{
			Name = "Equibop",
			ProductId = "Equibop",
			WingetId = "Equicord.Equibop",
			InstalledCheckPath = "%LOCALAPPDATA%\\equibop\\equibop.exe",
			Description = "Vencord-based Discord client with plugins and themes."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "Medal",
			ProductId = "Medal",
			ImagePath = "ms-appx:///Assets/medal-logo.png",
			DownloadUrl = "https://install.medal.tv/",
			SilentInstallArgs = "--silent",
			InstallerFileName = "MedalSetup.exe",
			InstalledCheckPath = "%LOCALAPPDATA%\\Medal\\app-*\\Medal.exe;%LOCALAPPDATA%\\Medal\\Medal.exe",
			Description = "Clip your gameplay without dropping frames, then edit and share it."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "Autoruns",
			ImagePath = "ms-appx:///Assets/autoruns-logo.png",
			DownloadUrl = "https://download.sysinternals.com/files/Autoruns.zip",
			SilentInstallArgs = string.Empty,
			InstallerFileName = "Autoruns.zip",
			ToolInstallDir = "%PROGRAMDATA%\\kaliteTools\\Autoruns",
			ToolExeName = "Autoruns64.exe",
			InstalledCheckPath = "%PROGRAMDATA%\\kaliteTools\\Autoruns\\Autoruns64.exe",
			Description = "Sysinternals utility that shows every autostart location - startup folders, services, drivers, scheduled tasks and more - so nothing launches behind your back."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "Everything",
			ImagePath = "ms-appx:///Assets/everything-logo.png",
			WingetId = "voidtools.Everything",
			InstalledCheckPath = "%PROGRAMFILES%\\Everything\\Everything.exe",
			Description = "Instant file search across every drive by name."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "7-Zip",
			ImagePath = "ms-appx:///Assets/7zip-logo.png",
			WingetId = "7zip.7zip",
			InstalledCheckPath = "%PROGRAMFILES%\\7-Zip\\7z.exe",
			Description = "Free archiver for zip, 7z, rar and everything else."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "NanaZip",
			ImagePath = "ms-appx:///Assets/nanazip-logo.png",
			WingetId = "M2Team.NanaZip",
			InstalledCheckPath = "%PROGRAMFILES%\\NanaZip\\NanaZip.exe",
			Description = "Modern 7-Zip fork with Windows 11 shell integration."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "Process Explorer",
			ImagePath = "ms-appx:///Assets/procexp-logo.png",
			DownloadUrl = "https://download.sysinternals.com/files/ProcessExplorer.zip",
			SilentInstallArgs = string.Empty,
			InstallerFileName = "ProcessExplorer.zip",
			ToolInstallDir = "%PROGRAMDATA%\\kaliteTools\\ProcessExplorer",
			ToolExeName = "procexp64.exe",
			InstalledCheckPath = "%PROGRAMDATA%\\kaliteTools\\ProcessExplorer\\procexp64.exe;%PROGRAMDATA%\\kaliteTools\\ProcessExplorer\\procexp.exe",
			Description = "Sysinternals task manager on steroids: handles, DLLs and live process trees."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "Rufus",
			ImagePath = "ms-appx:///Assets/rufus-logo.png",
			WingetId = "Rufus.Rufus",
			InstalledCheckPath = string.Empty,
			Description = "Create bootable USB drives in seconds."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "Ventoy",
			ImagePath = "ms-appx:///Assets/ventoy-logo.png",
			WingetId = "Ventoy.Ventoy",
			InstalledCheckPath = string.Empty,
			Description = "Multiboot USB tool: drop ISOs on a stick and boot any of them."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "Visual Studio Code",
			ImagePath = "ms-appx:///Assets/vscode-logo.png",
			WingetId = "Microsoft.VisualStudioCode",
			InstalledCheckPath = "%LOCALAPPDATA%\\Programs\\Microsoft VS Code\\Code.exe",
			Description = "Lightweight, extensible code editor."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "Visual Studio 2026 Community",
			ImagePath = "ms-appx:///Assets/vs-logo.png",
			WingetId = "Microsoft.VisualStudio.Community",
			InstalledCheckPath = "%PROGRAMFILES%\\Microsoft Visual Studio\\18\\Community\\Common7\\IDE\\devenv.exe",
			Description = "Full .NET and C++ IDE, free Community edition."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "Git",
			ImagePath = "ms-appx:///Assets/git-logo.png",
			WingetId = "Git.Git",
			InstalledCheckPath = "%PROGRAMFILES%\\Git\\bin\\git.exe",
			Description = "Distributed version control for everything you build."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "Python 3.14",
			ImagePath = "ms-appx:///Assets/python-logo.png",
			WingetId = "Python.Python.3.14",
			InstalledCheckPath = "%LOCALAPPDATA%\\Programs\\Python\\Python314\\python.exe",
			Description = "Latest Python runtime for scripts and tools."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "OBS Studio",
			ImagePath = "ms-appx:///Assets/obs-logo.png",
			WingetId = "OBSProject.OBSStudio",
			InstalledCheckPath = "%PROGRAMFILES%\\obs-studio\\bin\\64bit\\obs64.exe",
			Description = "Free recording and streaming studio."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "Bitwarden",
			ImagePath = "ms-appx:///Assets/bitwarden-logo.png",
			WingetId = "Bitwarden.Bitwarden",
			InstalledCheckPath = "%LOCALAPPDATA%\\Programs\\Bitwarden\\Bitwarden.exe",
			Description = "Open-source password manager with end-to-end encryption."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "Wireshark",
			ImagePath = "ms-appx:///Assets/wireshark-logo.png",
			WingetId = "WiresharkFoundation.Wireshark",
			InstalledCheckPath = "%PROGRAMFILES%\\Wireshark\\Wireshark.exe",
			Description = "Deep packet inspection for diagnosing network traffic."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "AnyDesk",
			ImagePath = "ms-appx:///Assets/anydesk-logo.png",
			WingetId = "AnyDesk.AnyDesk",
			InstalledCheckPath = "%PROGRAMFILES%\\AnyDesk\\AnyDesk.exe",
			Description = "Fast remote desktop access."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "RustDesk",
			ImagePath = "ms-appx:///Assets/rustdesk-logo.png",
			DownloadUrl = "https://github.com/rustdesk/rustdesk/releases/download/1.5.1/rustdesk-1.5.1-x86_64.exe",
			SilentInstallArgs = "--silent-install",
			InstallerFileName = "rustdesk-1.5.1-x86_64.exe",
			InstalledCheckPath = "%PROGRAMFILES%\\RustDesk\\rustdesk.exe",
			Description = "Open-source remote desktop, self-hostable."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "TCPView",
			ProductId = "TCPView",
			ImagePath = "ms-appx:///Assets/tcpview-logo.png",
			WingetId = "Microsoft.Sysinternals.TCPView",
			InstalledCheckPath = "%PROGRAMFILES%\\Sysinternals\\tcpview.exe;%LOCALAPPDATA%\\Microsoft\\WinGet\\Packages\\Microsoft.Sysinternals.TCPView_*\\tcpview.exe",
			Description = "Sysinternals tool showing every active network connection and port."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "StartAllBack",
			ProductId = "StartAllBack",
			ImagePath = "ms-appx:///Assets/startallback-logo.png",
			WingetId = "StartIsBack.StartAllBack",
			InstalledCheckPath = "%PROGRAMFILES%\\StartAllBack\\StartAllBackCfg.exe;%PROGRAMFILES(X86)%\\StartAllBack\\StartAllBackCfg.exe;%LOCALAPPDATA%\\Microsoft\\WinGet\\Packages\\StartIsBack.StartAllBack_*\\StartAllBackCfg.exe",
			Description = "Customise the Windows 11 Start menu and taskbar.",
			Note = "Paid after trial"
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "AutoHotkey",
			ProductId = "AutoHotkey",
			ImagePath = "ms-appx:///Assets/autohotkey-logo.png",
			WingetId = "AutoHotkey.AutoHotkey",
			InstalledCheckPath = "%PROGRAMFILES%\\AutoHotkey\\AutoHotkey.exe;%PROGRAMFILES(X86)%\\AutoHotkey\\AutoHotkey.exe;%LOCALAPPDATA%\\Microsoft\\WinGet\\Packages\\AutoHotkey.AutoHotkey_*\\AutoHotkey.exe",
			Description = "Scripting language for hotkeys, macros and automation."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "Obsidian",
			ProductId = "Obsidian",
			ImagePath = "ms-appx:///Assets/obsidian-logo.png",
			WingetId = "Obsidian.Obsidian",
			InstalledCheckPath = "%LOCALAPPDATA%\\Obsidian\\Obsidian.exe;%LOCALAPPDATA%\\Programs\\Obsidian\\Obsidian.exe",
			Description = "Markdown-based notes and knowledge base."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "Streamlabs Desktop",
			ProductId = "Streamlabs",
			WingetId = "Streamlabs.Streamlabs",
			InstalledCheckPath = "%PROGRAMFILES%\\Streamlabs Desktop\\Streamlabs Desktop.exe",
			Description = "Free streaming studio: go live, record and grow your channel."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "AB Download Manager",
			ProductId = "AB Download Manager",
			WingetId = "amir1376.ABDownloadManager",
			InstalledCheckPath = "%LOCALAPPDATA%\\ABDownloadManager\\ABDownloadManager.exe",
			Description = "Fast, open-source download manager with browser integration."
		});
		Utilities.Add(new BrowserInstallItem
		{
			Name = "voidImageViewer",
			ProductId = "void Image Viewer",
			WingetId = "voidtools.voidImageViewer",
			InstalledCheckPath = "%PROGRAMFILES%\\voidImageViewer\\voidImageViewer.exe",
			Description = "Featherweight image viewer from the maker of Everything."
		});
		SystemTools.Add(new BrowserInstallItem
		{
			Name = "DDU (Display Driver Uninstaller)",
			ProductId = "Display Driver Uninstaller",
			WingetId = "Wagnardsoft.DisplayDriverUninstaller",
			InstalledCheckPath = "%PROGRAMFILES%\\Display Driver Uninstaller\\Display Driver Uninstaller.exe;%LOCALAPPDATA%\\Microsoft\\WinGet\\Packages\\Wagnardsoft.DisplayDriverUninstaller_*\\Display Driver Uninstaller.exe",
			Description = "Completely removes GPU drivers and leftovers.",
			Note = "Use in Safe Mode"
		});
		SystemTools.Add(new BrowserInstallItem
		{
			Name = "CPU-Z",
			ProductId = "CPU-Z",
			ImagePath = "ms-appx:///Assets/cpuz-logo.png",
			WingetId = "CPUID.CPU-Z",
			InstalledCheckPath = "%PROGRAMFILES%\\CPUID\\CPU-Z\\cpuz.exe;%PROGRAMFILES(X86)%\\CPUID\\CPU-Z\\cpuz.exe;%LOCALAPPDATA%\\Microsoft\\WinGet\\Packages\\CPUID.CPU-Z_*\\cpuz.exe",
			Description = "Detailed CPU, motherboard and RAM information."
		});
		SystemTools.Add(new BrowserInstallItem
		{
			Name = "GPU-Z",
			ProductId = "GPU-Z",
			ImagePath = "ms-appx:///Assets/gpuz-logo.png",
			WingetId = "TechPowerUp.GPU-Z",
			InstalledCheckPath = "%PROGRAMFILES%\\GPU-Z\\GPU-Z.exe;%PROGRAMFILES(X86)%\\GPU-Z\\GPU-Z.exe;%LOCALAPPDATA%\\Microsoft\\WinGet\\Packages\\TechPowerUp.GPU-Z_*\\GPU-Z.exe",
			Description = "Graphics card specs, clocks and sensor monitoring."
		});
		SystemTools.Add(new BrowserInstallItem
		{
			Name = "HWiNFO",
			ProductId = "HWiNFO",
			ImagePath = "ms-appx:///Assets/hwinfo-logo.png",
			WingetId = "REALiX.HWiNFO",
			InstalledCheckPath = "%PROGRAMFILES%\\HWiNFO64\\HWiNFO64.exe;%PROGRAMFILES%\\HWiNFO\\HWiNFO.exe;%LOCALAPPDATA%\\Microsoft\\WinGet\\Packages\\REALiX.HWiNFO_*\\HWiNFO64.exe",
			Description = "In-depth hardware info with live sensors and temperatures."
		});
		SystemTools.Add(new BrowserInstallItem
		{
			Name = "NVCleanstall",
			ProductId = "NVCleanstall",
			ImagePath = "ms-appx:///Assets/nvcleanstall-logo.png",
			WingetId = "TechPowerUp.NVCleanstall",
			InstalledCheckPath = "%PROGRAMFILES%\\NVCleanstall\\NVCleanstall.exe;%LOCALAPPDATA%\\Microsoft\\WinGet\\Packages\\TechPowerUp.NVCleanstall_*\\NVCleanstall.exe",
			Description = "Custom NVIDIA driver installer: strip telemetry and bloat.",
			Note = "NVIDIA GPUs only"
		});
		SystemTools.Add(new BrowserInstallItem
		{
			Name = "MiniTool Partition Wizard",
			ProductId = "MiniTool Partition Wizard",
			ImagePath = "ms-appx:///Assets/minitool-logo.png",
			WingetId = "MiniTool.PartitionWizard.Free",
			InstalledCheckPath = "%PROGRAMFILES%\\MiniTool Partition Wizard*\\PartitionWizard.exe;%PROGRAMFILES(X86)%\\MiniTool Partition Wizard*\\PartitionWizard.exe;%LOCALAPPDATA%\\Microsoft\\WinGet\\Packages\\MiniTool.PartitionWizard.Free_*\\PartitionWizard.exe",
			Description = "Resize, merge, clone and recover disk partitions."
		});
		SystemTools.Add(new BrowserInstallItem
		{
			Name = "Intel Extreme Tuning Utility",
			ProductId = "Extreme Tuning Utility",
			ImagePath = "ms-appx:///Assets/intel-logo.png",
			WingetId = "Intel.IntelExtremeTuningUtility",
			InstalledCheckPath = "%PROGRAMFILES(X86)%\\Intel\\Intel(R) Extreme Tuning Utility\\Client\\XtuUiLauncher.exe;%PROGRAMFILES%\\Intel\\Intel(R) Extreme Tuning Utility\\Client\\XtuUiLauncher.exe",
			Description = "Overclock, stress-test and monitor Intel CPUs from Windows.",
			Note = "Unlocked Intel CPUs only"
		});
		SystemTools.Add(new BrowserInstallItem
		{
			Name = "CRU (Custom Resolution Utility)",
			ProductId = "Custom Resolution Utility",
			WingetId = "ToastyX.CustomResolutionUtility",
			InstalledCheckPath = "%LOCALAPPDATA%\\Microsoft\\WinGet\\Packages\\ToastyX.CustomResolutionUtility_*\\CRU.exe",
			Description = "Edit monitor EDID to add custom resolutions and refresh rates.",
			Note = "Restart needed after applying"
		});
		SystemTools.Add(new BrowserInstallItem
		{
			Name = "simplewall",
			ProductId = "simplewall",
			WingetId = "Henry++.simplewall",
			InstalledCheckPath = "%PROGRAMFILES%\\simplewall\\simplewall.exe",
			Description = "Lightweight firewall: choose exactly which apps may reach the network."
		});
		SystemTools.Add(new BrowserInstallItem
		{
			Name = "Portmaster",
			ProductId = "Portmaster",
			WingetId = "Safing.Portmaster",
			InstalledCheckPath = "%PROGRAMDATA%\\Safing\\Portmaster\\portmaster-app.exe;%PROGRAMFILES%\\Safing\\Portmaster\\portmaster-app.exe",
			Description = "Open-source application firewall with tracker blocking."
		});
		Development.Add(new BrowserInstallItem
		{
			Name = "Node.js (LTS)",
			ProductId = "Node.js",
			ImagePath = "ms-appx:///Assets/nodejs-logo.png",
			WingetId = "OpenJS.NodeJS.LTS",
			InstalledCheckPath = "%PROGRAMFILES%\\nodejs\\node.exe;%PROGRAMFILES(X86)%\\nodejs\\node.exe",
			Description = "JavaScript runtime for web tooling and scripts."
		});
		foreach (BrowserInstallItem browser in Browsers)
		{
			if (_installerService.IsBrowserInstalled(browser))
			{
				browser.Status = BrowserInstallStatus.AlreadyInstalled;
			}
		}
		foreach (BrowserInstallItem gameLauncher in GameLaunchers)
		{
			if (_installerService.IsBrowserInstalled(gameLauncher))
			{
				gameLauncher.Status = BrowserInstallStatus.AlreadyInstalled;
			}
		}
		foreach (BrowserInstallItem socialApp in SocialApps)
		{
			if (_installerService.IsBrowserInstalled(socialApp))
			{
				socialApp.Status = BrowserInstallStatus.AlreadyInstalled;
			}
		}
		foreach (BrowserInstallItem utility in Utilities)
		{
			if (_installerService.IsBrowserInstalled(utility))
			{
				utility.Status = BrowserInstallStatus.AlreadyInstalled;
			}
		}
		foreach (BrowserInstallItem systemTool in SystemTools)
		{
			if (_installerService.IsBrowserInstalled(systemTool))
			{
				systemTool.Status = BrowserInstallStatus.AlreadyInstalled;
			}
		}
		foreach (BrowserInstallItem item9 in Development)
		{
			if (_installerService.IsBrowserInstalled(item9))
			{
				item9.Status = BrowserInstallStatus.AlreadyInstalled;
			}
		}
		UpdateVisibilityFilters();
		DetectUpdatesAsync();
		SelectedBrowser = VisibleBrowsers.FirstOrDefault();
		foreach (BrowserInstallItem trackable in Browsers.Concat(GameLaunchers).Concat(SocialApps).Concat(Utilities).Concat(SystemTools).Concat(Development))
		{
			trackable.PropertyChanged += OnItemExtensionSelectionChanged;
		}
		_ = WarmWingetInventoryAsync();
	}

	private async Task WarmWingetInventoryAsync()
	{
		try
		{
			if (WingetInventory.HasSnapshot)
			{
				return;
			}
			await WingetInventory.WarmUpAsync();
			DispatcherQueue? dispatcher = App.MainWindow?.DispatcherQueue;
			if (dispatcher is null)
			{
				return;
			}
			dispatcher.TryEnqueue(() =>
			{
				foreach (BrowserInstallItem item in Browsers.Concat(GameLaunchers).Concat(SocialApps).Concat(Utilities).Concat(SystemTools).Concat(Development))
				{
					if (item.Status == BrowserInstallStatus.AlreadyInstalled || item.Status == BrowserInstallStatus.Installed)
					{
						continue;
					}
					if (_installerService.IsBrowserInstalled(item))
					{
						item.Status = BrowserInstallStatus.AlreadyInstalled;
					}
				}
				UpdateVisibilityFilters();
				_ = DetectUpdatesAsync();
			});
		}
		catch { }
	}

	private void OnItemExtensionSelectionChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName != nameof(BrowserInstallItem.ExtensionsLabel)) return;
		if (sender is not BrowserInstallItem item || item.Extensions.Count == 0) return;
		if (!IsBrowserInstalledSafe(item)) return;
		_ = ApplySelectedExtensionsAsync(item);
	}

	private async Task ApplySelectedExtensionsAsync(BrowserInstallItem item)
	{
		try
		{
			string? warning = await Task.Run(() => _installerService.ApplyExtensions(item));
			if (warning != null)
			{
			item.ErrorMessage = warning;
			App.Notify(item.Name, warning, AppNoticeSeverity.Warning);
			return;
		}
		int count = item.Extensions.Count(e => e.IsSelected);
		string message = count > 0
			? $"{count} extension{(count == 1 ? string.Empty : "s")} applied to {item.Name}."
			: "Extensions cleared from " + item.Name + ".";
		item.ErrorMessage = message;
		App.Notify(item.Name, message, AppNoticeSeverity.Success);
		}
		catch (Exception ex)
		{
			item.ErrorMessage = ex.Message;
			App.Notify(item.Name, ex.Message, AppNoticeSeverity.Error);
		}
	}

	private bool IsBrowserInstalledSafe(BrowserInstallItem item)
	{
		try
		{
			return _installerService.IsBrowserInstalled(item);
		}
		catch
		{
			return false;
		}
	}

	private async Task DetectUpdatesAsync()
	{
		try
		{
			List<BrowserInstallItem> all = Browsers.Concat(GameLaunchers).Concat(SocialApps).Concat(Utilities)
				.Concat(SystemTools)
				.Concat(Development)
				.ToList();
			HashSet<string> upgradable = await _installerService.GetUpgradableWingetIdsAsync(CancellationToken.None);
			List<BrowserInstallItem> flagged = await Task.Run(() =>
			{
				foreach (BrowserInstallItem item in all)
				{
					if (!string.IsNullOrEmpty(item.WingetId) && upgradable.Contains(item.WingetId) && IsBrowserInstalledSafe(item))
					{
						item.UpdateAvailable = true;
						item.Status = BrowserInstallStatus.UpdateAvailable;
					}
					else
					{
						_installerService.DetectUpdate(item);
					}
				}
				return all.Where((BrowserInstallItem i) => i.UpdateAvailable).ToList();
			});
			if (flagged.Count != 0)
			{
				DispatcherQueue dq = App.MainWindow.DispatcherQueue;
				dq.TryEnqueue(() =>
				{
					UpdateStatusText = $"{flagged.Count} update{((flagged.Count == 1) ? "" : "s")} available";
					UpdateVisibilityFilters();
					OnPropertyChanged("HasUpdates");
				});
			}
		}
		catch
		{
		}
	}

	private async Task InstallBrowserAsync(BrowserInstallItem? item)
	{
		if (item == null)
		{
			return;
		}
		SelectedBrowser = item;
		_cts = new CancellationTokenSource();
		item.ErrorMessage = string.Empty;
		IsInstalling = true;
		InstallStatusText = "Initializing...";
		Progress<BrowserInstallStatus> progress = new Progress<BrowserInstallStatus>((BrowserInstallStatus status) =>
		{
			item.Status = status;
			InstallStatusText = item.StatusText;
			if (status == BrowserInstallStatus.Installed || status == BrowserInstallStatus.Failed || status == BrowserInstallStatus.AlreadyInstalled)
			{
				IsInstalling = false;
				UpdateVisibilityFilters();
				App.Notify(item.Name, status switch
				{
					BrowserInstallStatus.Failed => (item.ErrorMessage.Length > 0) ? item.ErrorMessage : "The installer did not finish.", 
					BrowserInstallStatus.Installed => "Installed and ready to use.", 
					_ => "Already installed.", 
				}, status switch
				{
					BrowserInstallStatus.Failed => AppNoticeSeverity.Error, 
					BrowserInstallStatus.Installed => AppNoticeSeverity.Success, 
					_ => AppNoticeSeverity.Info, 
				});
			}
		});
		Progress<double> downloadProgress = new Progress<double>((double val) =>
		{
			item.DownloadProgress = val;
			InstallStatusText = $"Downloading... ({val:F1}%)";
		});
		Progress<string> errorProgress = new Progress<string>((string error) =>
		{
			item.ErrorMessage = error;
		});
		try
		{
			await Task.Run(() => _installerService.InstallBrowserAsync(item, progress, downloadProgress, errorProgress, _cts.Token));
		}
		catch (OperationCanceledException)
		{
			item.ErrorMessage = "Cancelled";
			item.Status = BrowserInstallStatus.Failed;
			UpdateVisibilityFilters();
		}
		IsInstalling = false;
		_cts.Dispose();
		_cts = null;
	}

	private async Task UpdateBrowserAsync(BrowserInstallItem? item)
	{
		if (item == null)
		{
			return;
		}
		SelectedBrowser = item;
		_cts = new CancellationTokenSource();
		item.ErrorMessage = string.Empty;
		IsInstalling = true;
		InstallStatusText = "Updating...";
		Progress<BrowserInstallStatus> progress = new Progress<BrowserInstallStatus>((BrowserInstallStatus status) =>
		{
			item.Status = status;
			InstallStatusText = item.StatusText;
			if ((status == BrowserInstallStatus.Installed || status == BrowserInstallStatus.Failed) ? true : false)
			{
				IsInstalling = false;
				UpdateVisibilityFilters();
				string title = item.Name + " update";
				string message = ((status == BrowserInstallStatus.Installed) ? "Updated to the latest version." : ((item.ErrorMessage.Length > 0) ? item.ErrorMessage : "The update did not finish."));
				App.Notify(title, message, (status == BrowserInstallStatus.Installed) ? AppNoticeSeverity.Success : AppNoticeSeverity.Error);
			}
		});
		Progress<double> downloadProgress = new Progress<double>((double val) =>
		{
			item.DownloadProgress = val;
			InstallStatusText = $"Downloading... ({val:F1}%)";
		});
		Progress<string> errorProgress = new Progress<string>((string error) =>
		{
			item.ErrorMessage = error;
		});
		try
		{
			await _installerService.UpdateBrowserAsync(item, progress, downloadProgress, errorProgress, _cts.Token);
		}
		catch (OperationCanceledException)
		{
			item.ErrorMessage = "Cancelled";
			item.Status = BrowserInstallStatus.UpdateAvailable;
			UpdateVisibilityFilters();
		}
		catch (Exception ex2)
		{
			Exception ex3 = ex2;
			item.ErrorMessage = ex3.Message;
			item.Status = BrowserInstallStatus.Failed;
		}
		finally
		{
			IsInstalling = false;
			_cts.Dispose();
			_cts = null;
		}
	}

	private async Task UninstallBrowserAsync(BrowserInstallItem? item)
	{
		if (item == null)
		{
			return;
		}
		SelectedBrowser = item;
		_cts = new CancellationTokenSource();
		IsInstalling = true;
		InstallStatusText = "Uninstalling...";
		try
		{
			await _installerService.UninstallBrowserAsync(item, _cts.Token);
			item.Status = BrowserInstallStatus.NotInstalled;
			InstallStatusText = "";
			UpdateVisibilityFilters();
			App.Notify(item.Name, "Uninstalled.", AppNoticeSeverity.Success);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			InstallStatusText = "Uninstall Failed: " + ex2.Message;
			App.Notify(item.Name, "Uninstall failed: " + ex2.Message, AppNoticeSeverity.Error);
		}
		finally
		{
			IsInstalling = false;
			_cts.Dispose();
			_cts = null;
		}
	}

	private void CancelInstall()
	{
		_cts?.Cancel();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnShowInstalledChanged(bool value)
	{
		UpdateVisibilityFilters();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSearchQueryChanged(string value)
	{
		UpdateVisibilityFilters();
	}
}
