#pragma warning disable CS8600 // Null cast from decompiled code
#pragma warning disable CS8601 // Possible null assignment from decompiled code
#pragma warning disable CS8625 // Cannot convert null literal from decompiled code
#pragma warning disable CS4014 // Unawaited task from decompiled code
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CefSharp;
using CefSharp.Wpf;
using Zenith.App.Handlers;
using Zenith.App.Models;
using Zenith.App.Services;
using Zenith.App.ViewModels;

namespace Zenith.App;

public partial class MainWindow : Window
{
	private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

	private const int DWMWCP_DONOTROUND = 1;

	private readonly Dictionary<Guid, ChromiumWebBrowser> _browsers = new Dictionary<Guid, ChromiumWebBrowser>();

	private double _currentZoom = 1.0;

	private bool _isHubPinned = false;

	private int _currentHubTabIndex = 2;

	private bool _isSettingsPinned = false;

	private X509Certificate2? _currentCertificate = null;

	private DownloadItemModel? _currentDownloadingModel = null;

	private bool _isAddressBarActiveOnStartPage = false;

	public MainViewModel ViewModel { get; }

	public ICommand AnimatedCloseTabCommand { get; private set; } = null;

	[DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

	public MainWindow()
	{
		ViewModel = new MainViewModel();
		AnimatedCloseTabCommand = new ActionCommand<BrowserTab>((BrowserTab tab) =>
		{
			CloseTabWithAnimation(tab);
		}, (BrowserTab tab) => ViewModel.Tabs.Count > 1);
		InitializeComponent();
		DataContext = ViewModel;
		ViewModel.PropertyChanged += ViewModel_PropertyChanged;
		HistoryItemsList.ItemsSource = HistoryService.Items;
		ActiveDownloadsList.ItemsSource = DownloadsService.ActiveDownloads;
		PastDownloadsList.ItemsSource = DownloadsService.PastDownloads;
		UpdateFavoritesHubList();
		FavoritesService.FavoritesChanged += () =>
		{
			Dispatcher.InvokeAsync(() =>
			{
				UpdateFavoriteStar(ViewModel.SelectedTab?.Url);
				UpdateFavoritesHubList();
			});
		};
		DownloadsService.DownloadStarted += OnDownloadStarted;
		DownloadsService.DownloadUpdated += OnDownloadUpdated;
		DownloadsService.DownloadFinished += OnDownloadFinished;
		DownloadsService.DownloadCancelled += OnDownloadCancelled;
		Loaded += (object s, RoutedEventArgs e) =>
		{
			LangService.LanguageChanged -= ApplyLanguage;
			LangService.LanguageChanged += ApplyLanguage;
			ApplyLanguage();
			OpenInitialTab();
			ApplyFavoritesBarVisibility();
		};
		Activated += (object? s, EventArgs e) =>
		{
			if (WindowState != WindowState.Maximized)
			{
				WindowBorder.BorderBrush = (Brush)FindResource("AccentBrush");
			}
		};
		Deactivated += (object? s, EventArgs e) =>
		{
			if (WindowState != WindowState.Maximized)
			{
				WindowBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(112, 112, 112));
			}
		};
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		WindowInteropHelper windowInteropHelper = new WindowInteropHelper(this);
		if (windowInteropHelper.Handle != IntPtr.Zero)
		{
			int pvAttribute = 1;
			DwmSetWindowAttribute(windowInteropHelper.Handle, 33, ref pvAttribute, 4);
		}
	}

	private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "SelectedTab" && ViewModel.SelectedTab != null)
		{
			SwitchToBrowser(ViewModel.SelectedTab);
		}
	}

	private ChromiumWebBrowser GetOrCreateBrowser(BrowserTab tab)
	{
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Expected Obj, but got Unknown
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0105: Expected Obj, but got Unknown
		if (_browsers.TryGetValue(tab.Id, out ChromiumWebBrowser value))
		{
			return value;
		}
		string initialAddress = (string.IsNullOrEmpty(tab.Url) ? "zenith://newtab" : tab.Url);
		ChromiumWebBrowser chromiumWebBrowser = new ChromiumWebBrowser(initialAddress)
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
			DisplayHandler = new ZenithDisplayHandler(tab),
			DownloadHandler = new ZenithDownloadHandler(),
			AudioHandler = new ZenithAudioHandler(tab),
			RequestHandler = new ZenithRequestHandler(),
			LifeSpanHandler = new ZenithLifeSpanHandler(
				tab,
				targetUrl => ((DispatcherObject)this).Dispatcher.InvokeAsync(() => OpenInNewTab(targetUrl)),
				closingTab => ((DispatcherObject)this).Dispatcher.InvokeAsync(() => CloseTabWithAnimation(closingTab))
			),
		};
		chromiumWebBrowser.LoadError += (object? s, LoadErrorEventArgs e) =>
		{
			// Only handle main frame navigation errors and ignore aborted loads (e.g. user cancelled or redirected)
			if (!e.Frame.IsMain) return;
			if (e.ErrorCode == CefErrorCode.Aborted) return;

			string failedUrl = e.FailedUrl ?? "";
			if (failedUrl.StartsWith("zenith://", StringComparison.OrdinalIgnoreCase)) return;

			// Map CefErrorCode to authentic Edge 1507 Error Page
			string errorHtm = e.ErrorCode switch
			{
				CefErrorCode.NameNotResolved => "dnserror.html",
				CefErrorCode.InternetDisconnected => "dnserror.html",
				CefErrorCode.ConnectionRefused => "dnserror.html",
				CefErrorCode.ConnectionTimedOut => "servbusy.htm",
				CefErrorCode.ConnectionClosed => "servbusy.htm",
				CefErrorCode.ConnectionReset => "servbusy.htm",
				CefErrorCode.SslProtocolError => "tlserror.htm",
				CefErrorCode.CertAuthorityInvalid => "invalidcert.htm",
				CefErrorCode.CertCommonNameInvalid => "invalidcert.htm",
				CefErrorCode.CertDateInvalid => "invalidcert.htm",
				CefErrorCode.ProxyConnectionFailed => "proxyerror.htm",
				CefErrorCode.TunnelConnectionFailed => "proxyerror.htm",
				_ => "dnserror.html"
			};

			string edgeErrorUrl = $"zenith://error/{errorHtm}#{failedUrl}";
			((DispatcherObject)this).Dispatcher.InvokeAsync(() =>
			{
				chromiumWebBrowser.Load(edgeErrorUrl);
			});
		};
		chromiumWebBrowser.JavascriptMessageReceived += (object? s, JavascriptMessageReceivedEventArgs e) =>
		{
			((DispatcherObject)this).Dispatcher.InvokeAsync((Action)(() =>
			{
				switch (e.Message?.ToString())
				{
				case "focus_address_bar":
					ActivateAddressBarOnStartPage();
					break;
				case "blur_address_bar":
					DeactivateAddressBarOnStartPage();
					break;
				case "zenith_audio_play":
					tab.IsPlayingAudio = true;
					break;
				case "zenith_audio_pause":
					tab.IsPlayingAudio = false;
					break;
				case "zenith_window_close":
					CloseTabWithAnimation(tab);
					break;
				}
			}));
		};
		chromiumWebBrowser.FrameLoadStart += (object? s, FrameLoadStartEventArgs e) =>
		{
			if (e?.Frame?.IsMain == true && ZenithAdBlockService.IsEnabled &&
				(e.Url?.Contains("youtube.com") == true))
			{
				string earlyCSS = @"
(function() {
    if (document.getElementById('__zenith_yt_early_css')) return;
    var s = document.createElement('style');
    s.id = '__zenith_yt_early_css';
    s.textContent = '.video-ads,#player-ads,.ytp-ad-module,.ytp-ad-overlay-container,.ytp-ad-text-overlay,.ytp-ad-image-overlay,.ytp-ad-action-interstitial,ytd-display-ad-renderer,ytd-action-companion-ad-renderer,ytd-banner-promo-renderer,ytd-promoted-video-renderer,ytd-compact-promoted-video-renderer,.ytd-ad-slot-renderer,#masthead-ad,ytd-primetime-promo-renderer,ytd-ad-slot-renderer,ytd-in-feed-ad-layout-renderer,#ad-instream-overlay,.ad-container { display:none!important;visibility:hidden!important;height:0!important; }';
    (document.head || document.documentElement).appendChild(s);
})();
";
				e.Frame.ExecuteJavaScriptAsync(earlyCSS);
			}
		};
		chromiumWebBrowser.FrameLoadEnd += (object? s, FrameLoadEndEventArgs e) =>
		{
			if (e == null || e.Frame == null || !e.Frame.IsValid) return;

			string currentUrl = e.Url ?? "";
			if (e.Frame.IsMain && !currentUrl.StartsWith("zenith://", StringComparison.OrdinalIgnoreCase))
			{
				e.Frame.ExecuteJavaScriptAsync("(function(){ if(window.__zenithWindowCloseHooked)return; window.__zenithWindowCloseHooked=true; try{ window.close=function(){ if(window.CefSharp&&CefSharp.PostMessage){ CefSharp.PostMessage('zenith_window_close'); } }; if(window.self) window.self.close=window.close; }catch(e){} })();");
				e.Frame.ExecuteJavaScriptAsync("(function() {\r\n                    if (window.__zenithAudioHooked) return;\r\n                    window.__zenithAudioHooked = true;\r\n                    function checkMedia() {\r\n                        var media = document.querySelectorAll('audio, video');\r\n                        var playing = false;\r\n                        for (var i = 0; i < media.length; i++) {\r\n                            var m = media[i];\r\n                            if (!m.paused && !m.ended && m.currentTime > 0 && !m.muted && m.volume > 0) {\r\n                                playing = true;\r\n                                break;\r\n                            }\r\n                        }\r\n                        if (window.CefSharp && CefSharp.PostMessage) {\r\n                            CefSharp.PostMessage(playing ? 'zenith_audio_play' : 'zenith_audio_pause');\r\n                        }\r\n                    }\r\n                    document.addEventListener('play', function() { setTimeout(checkMedia, 150); }, true);\r\n                    document.addEventListener('pause', function() { setTimeout(checkMedia, 150); }, true);\r\n                    document.addEventListener('ended', function() { setTimeout(checkMedia, 150); }, true);\r\n                    document.addEventListener('volumechange', function() { setTimeout(checkMedia, 150); }, true);\r\n                    setInterval(checkMedia, 2000);\r\n                })();");

				if (ZenithAdBlockService.IsEnabled && currentUrl.Contains("youtube.com"))
				{
					string ytAdScript = @"
(function zenithYTAdBlock() {
    'use strict';
    if (window.__zenithYTAdBlock) return;
    window.__zenithYTAdBlock = true;

    // ── 1. Blokada sendBeacon (pinging reklam) ────────────────────────────────────────────
    var _origBeacon = navigator.sendBeacon ? navigator.sendBeacon.bind(navigator) : null;
    if (_origBeacon) {
        navigator.sendBeacon = function(url, data) {
            if (typeof url === 'string' && (
                url.includes('/ptracking') ||
                url.includes('/api/stats/ads') ||
                url.includes('/api/stats/qoe?adformat') ||
                url.includes('doubleclick.net') ||
                url.includes('googleadservices')
            )) return true;
            return _origBeacon(url, data);
        };
    }

    // ── 2. XHR interception ─────────────────────────────────────────────────────────────────────────────
    var _origXHROpen = XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open = function(method, url) {
        if (typeof url === 'string' && (
            url.includes('/api/stats/ads') ||
            url.includes('/ptracking') ||
            url.includes('/pagead/') ||
            url.includes('imasdk.googleapis.com') ||
            url.includes('/get_video_info') ||
            url.includes('/youtubei/v1/log_event') ||
            url.includes('doubleclick.net') ||
            url.includes('googleadservices.com') ||
            url.includes('/api/ads/') ||
            url.includes('googlevideo.com/videoplayback') && (url.includes('ctier=A') || url.includes('&oad='))
        )) {
            arguments[1] = 'about:invalid';
        }
        return _origXHROpen.apply(this, arguments);
    };

    // ── 3. fetch interception ───────────────────────────────────────────────────────────────────────────────────────────
    var _origFetch = window.fetch;
    window.fetch = function(input, init) {
        var url = typeof input === 'string' ? input : (input && input.url) || '';
        if (url.includes('/api/stats/ads') ||
            url.includes('/ptracking') ||
            url.includes('imasdk.googleapis.com') ||
            url.includes('doubleclick.net') ||
            url.includes('/api/ads/') ||
            url.includes('googleadservices.com')) {
            return Promise.reject(new Error('zenith-blocked'));
        }
        return _origFetch.apply(this, arguments);
    };

    // ── 4. CSS – ukryj WSZYSTKIE kontenery reklamowe ─────────────────────────────────────────────────
    function injectCSS() {
        if (document.getElementById('__zenith_yt_adcss')) return;
        var style = document.createElement('style');
        style.id = '__zenith_yt_adcss';
        style.textContent = [
            '.video-ads',
            '#player-ads',
            '.ytp-ad-module',
            '.ytp-ad-overlay-container',
            '.ytp-ad-text-overlay',
            '.ytp-ad-image-overlay',
            '.ytp-ad-action-interstitial',
            '.ytp-ad-skip-button-slot',
            '.ytp-ad-player-overlay',
            '.ytp-ad-player-overlay-layout',
            '.ytp-ad-player-overlay-instream-info',
            '.ytp-suggested-action',
            'ytd-display-ad-renderer',
            'ytd-action-companion-ad-renderer',
            'ytd-banner-promo-renderer',
            'ytd-promoted-video-renderer',
            'ytd-compact-promoted-video-renderer',
            '.ytd-ad-slot-renderer',
            '#masthead-ad',
            'ytd-primetime-promo-renderer',
            'ytd-statement-banner-renderer',
            'ytd-ad-slot-renderer',
            'ytd-in-feed-ad-layout-renderer',
            'ytd-promoted-sparkles-web-renderer',
            'tp-yt-paper-dialog[aria-label]',
            '.ytd-mealbar-promo-renderer',
            'ytm-promoted-sparkles-web-renderer',
            'ytm-companion-ad-renderer',
            '.ad-container',
            '.ad-showing .ytp-chrome-bottom',
            '#ad-instream-overlay',
            '.ytp-ad-visit-advertiser-button',
            '.ytp-ce-element',
            '#cinematics'
        ].join(', ') + ' { display: none !important; visibility: hidden !important; height: 0 !important; max-height: 0 !important; pointer-events: none !important; }';
        (document.head || document.documentElement).appendChild(style);
    }
    injectCSS();

    // ── 5. Funkcja skip + seek (failsafe) ──────────────────────────────────────────────────────────────────────────────────────
    function skipAd() {
        injectCSS();
        // Skip button
        var skipSelectors = [
            '.ytp-skip-ad-button',
            '.ytp-ad-skip-button',
            '.ytp-ad-skip-button-modern',
            '.ytp-ad-overlay-close-button',
            '.videoAdUiSkipButton',
            '.ytp-ad-skip-button-slot button'
        ];
        for (var i = 0; i < skipSelectors.length; i++) {
            var btn = document.querySelector(skipSelectors[i]);
            if (btn && btn.offsetParent !== null) { btn.click(); return; }
        }

        // Seek do końca jeśli reklama jest w playerze
        var video = document.querySelector('video.html5-main-video, video');
        var adOverlay = document.querySelector('.ad-showing');
        if (video && adOverlay) {
            if (video.duration && !isNaN(video.duration) && isFinite(video.duration)) {
                video.muted = true;
                video.currentTime = video.duration - 0.01;
                video.muted = false;
                try { video.play(); } catch(e2) {}
            }
        }

        // Ukryj inline overlay reklamy
        var adSelectors = [
            '.ytp-ad-overlay-container',
            '.ytp-ad-text-overlay',
            '.ytp-ad-image-overlay',
            '.ytp-ad-action-interstitial',
            '.ad-container',
            '#masthead-ad',
            'ytd-banner-promo-renderer',
            '.ytd-ad-slot-renderer',
            'ytd-promoted-video-renderer',
            'ytd-compact-promoted-video-renderer',
            '.ytd-compact-promoted-item-renderer',
            'ytd-in-feed-ad-layout-renderer',
            '#player-ads',
            '.video-ads'
        ];
        for (var j = 0; j < adSelectors.length; j++) {
            var els = document.querySelectorAll(adSelectors[j]);
            for (var k = 0; k < els.length; k++) {
                els[k].style.setProperty('display', 'none', 'important');
            }
        }
    }

    // ── 6. MutationObserver + interval ────────────────────────────────────────────────────────────────────────────────────
    skipAd();
    clearInterval(window._zenithAdInterval);
    window._zenithAdInterval = setInterval(skipAd, 250);

    var obs = new MutationObserver(function(mutations) {
        for (var m = 0; m < mutations.length; m++) {
            if (mutations[m].addedNodes.length > 0) { skipAd(); break; }
        }
    });
    obs.observe(document.documentElement || document.body, { childList: true, subtree: true });

    // ── 7. YouTube SPA navigation ─────────────────────────────────────────────────────────────────────────────────────────────────
    window.addEventListener('yt-navigate-finish', function() {
        window.__zenithYTAdBlock = false;
        setTimeout(function() {
            window.__zenithYTAdBlock = true;
            skipAd();
        }, 100);
        setTimeout(skipAd, 400);
        setTimeout(skipAd, 900);
        setTimeout(skipAd, 2000);
    });
    window.addEventListener('yt-page-data-updated', function() {
        setTimeout(skipAd, 300);
    });

    // ── 8. yt-player ad intercept ──────────────────────────────────────────────────────────────────────────────────────────
    document.addEventListener('yt-ad-impression', function(e) {
        e.stopImmediatePropagation();
        setTimeout(skipAd, 50);
    }, true);
})();
";
					e.Frame.ExecuteJavaScriptAsync(ytAdScript);
				}
			}
		};
		chromiumWebBrowser.AddressChanged += (object s, DependencyPropertyChangedEventArgs e) =>
		{
			//IL_000e: Unknown result type (might be due to invalid IL or missing references)
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			((DispatcherObject)this).Dispatcher.InvokeAsync<Task>((Func<Task>)(async () =>
			{
				string newAddress = (e.NewValue as string) ?? "";
				tab.Url = newAddress;
				tab.IsSecure = newAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
				if (tab == ViewModel.SelectedTab)
				{
					AddressBox.Text = tab.DisplayUrl;
					UpdateSecurityIcon(tab);
					UpdateAddressBarVisibility(tab);
					UpdateFavoriteStar(newAddress);
				}
				if (!string.IsNullOrEmpty(newAddress) && !newAddress.StartsWith("zenith://", StringComparison.OrdinalIgnoreCase) && newAddress != "about:blank")
				{
					BitmapImage icon = await FaviconService.GetFaviconAsync(newAddress);
					if (icon != null)
					{
						tab.Favicon = icon;
					}
				}
				else
				{
					tab.Favicon = null;
				}
			}));
		};
		chromiumWebBrowser.TitleChanged += (object s, DependencyPropertyChangedEventArgs e) =>
		{
			//IL_000e: Unknown result type (might be due to invalid IL or missing references)
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			((DispatcherObject)this).Dispatcher.InvokeAsync((Action)(() =>
			{
				string text = (e.NewValue as string) ?? "";
				if (tab.Url.StartsWith("zenith://", StringComparison.OrdinalIgnoreCase))
				{
					tab.Title = LangService.T("tab.newtab");
				}
				else
				{
					tab.Title = (string.IsNullOrWhiteSpace(text) ? LangService.T("tab.newtab") : text);
				}
			}));
		};
		chromiumWebBrowser.LoadingStateChanged += (object? s, LoadingStateChangedEventArgs e) =>
		{
			((DispatcherObject)this).Dispatcher.InvokeAsync<Task>((Func<Task>)(async () =>
			{
				bool isStart = tab.Url.StartsWith("zenith://", StringComparison.OrdinalIgnoreCase);
				tab.IsLoading = !isStart && e.IsLoading;
				if (e.IsLoading)
				{
					tab.IsPlayingAudio = false;
				}
				tab.CanGoBack = e.CanGoBack;
				tab.CanGoForward = e.CanGoForward;
				if (tab == ViewModel.SelectedTab)
				{
					RefreshIcon.Text = (tab.IsLoading ? "\ue711" : "\ue72c");
					BtnRefresh.ToolTip = (tab.IsLoading ? "Stop (Esc)" : "Refresh (F5)");
					UpdateAddressBarVisibility(tab);
				}
				if (!e.IsLoading && !string.IsNullOrEmpty(tab.Url) && !tab.Url.StartsWith("zenith://", StringComparison.OrdinalIgnoreCase) && tab.Url != "about:blank")
				{
					if (tab.Favicon == null)
					{
						BitmapImage icon = await FaviconService.GetFaviconAsync(tab.Url);
						if (icon != null)
						{
							tab.Favicon = icon;
						}
					}
					HistoryService.AddEntry(tab.Title, tab.Url, tab.Favicon);
					UpdateSecurityDetailsAsync(tab.Url);
				}
			}));
		};
		chromiumWebBrowser.StatusMessage += (object? s, StatusMessageEventArgs e) =>
		{
			((DispatcherObject)this).Dispatcher.InvokeAsync((Action)(() =>
			{
				if (tab == ViewModel.SelectedTab)
				{
					tab.StatusText = e.Value;
				}
			}));
		};
		chromiumWebBrowser.PreviewMouseDown += (object s, MouseButtonEventArgs e) =>
		{
			if (!_isHubPinned && HubPane.Visibility == Visibility.Visible)
			{
				CloseHub();
			}
			if (!_isSettingsPinned && SettingsPane.Visibility == Visibility.Visible)
			{
				CloseSettings();
			}
			if (SecurityPopup.IsOpen)
			{
				SecurityPopup.IsOpen = false;
			}
			if (MoreMenuContainer.Visibility == Visibility.Visible)
			{
				CloseMoreMenuAnimated();
			}
			if (_isAddressBarActiveOnStartPage)
			{
				DeactivateAddressBarOnStartPage();
			}
		};
		_browsers[tab.Id] = chromiumWebBrowser;
		return chromiumWebBrowser;
	}

	private void OpenInitialTab()
	{
		string url = SettingsService.OpenWith switch
		{
			"StartPage"     => "zenith://newtab",
			"SpecificPage"  => SettingsService.Homepage,
			"PreviousPages" => "zenith://newtab", // future: restore session
			_               => "zenith://newtab",   // "NewTab"
		};
		var tab = ViewModel.NewTab(url);
		SwitchToBrowser(tab);
	}

	private void SwitchToBrowser(BrowserTab tab)
	{
		BrowserContainer.Children.Clear();
		ChromiumWebBrowser orCreateBrowser = GetOrCreateBrowser(tab);
		BrowserContainer.Children.Add(orCreateBrowser);
		RefreshIcon.Text = (tab.IsLoading ? "\ue711" : "\ue72c");
		BtnRefresh.ToolTip = (tab.IsLoading ? "Stop (Esc)" : "Refresh (F5)");
		AddressBox.Text = tab.DisplayUrl;
		UpdateSecurityIcon(tab);
		UpdateAddressBarVisibility(tab);
		UpdateSecurityDetailsAsync(tab.Url);
		UpdateFavoriteStar(tab.Url);
	}

	private void UpdateAddressBarVisibility(BrowserTab tab)
	{
		if (AddressBarContainer == null)
		{
			return;
		}
		if (string.IsNullOrEmpty(tab.Url) || tab.Url.StartsWith("zenith://", StringComparison.OrdinalIgnoreCase) || tab.Url == "about:blank")
		{
			if (_isAddressBarActiveOnStartPage)
			{
				AddressBarContainer.Visibility = Visibility.Visible;
				if (StartPageAddressBarPlaceholder != null)
				{
					StartPageAddressBarPlaceholder.Visibility = Visibility.Collapsed;
				}
			}
			else
			{
				AddressBarContainer.Visibility = Visibility.Hidden;
				if (StartPageAddressBarPlaceholder != null)
				{
					StartPageAddressBarPlaceholder.Visibility = Visibility.Visible;
				}
			}
		}
		else
		{
			_isAddressBarActiveOnStartPage = false;
			AddressBarContainer.Visibility = Visibility.Visible;
			if (StartPageAddressBarPlaceholder != null)
			{
				StartPageAddressBarPlaceholder.Visibility = Visibility.Collapsed;
			}
		}
	}

	private void StartPageAddressBarPlaceholder_MouseDown(object sender, MouseButtonEventArgs e)
	{
		ActivateAddressBarOnStartPage();
	}

	private void ActivateAddressBarOnStartPage()
	{
		_isAddressBarActiveOnStartPage = true;
		if (StartPageAddressBarPlaceholder != null)
		{
			StartPageAddressBarPlaceholder.Visibility = Visibility.Collapsed;
		}
		AddressBarContainer.Visibility = Visibility.Visible;
		GetActiveWebBrowser()?.ExecuteScriptAsync("window.setStartPageCleared && window.setStartPageCleared(true);");
		AddressBox.Focus();
		AddressBox.SelectAll();
	}

	private void DeactivateAddressBarOnStartPage()
	{
		if (_isAddressBarActiveOnStartPage)
		{
			_isAddressBarActiveOnStartPage = false;
			AddressBarContainer.Visibility = Visibility.Hidden;
			if (StartPageAddressBarPlaceholder != null)
			{
				StartPageAddressBarPlaceholder.Visibility = Visibility.Visible;
			}
			AddressBox.Text = "";
			GetActiveWebBrowser()?.ExecuteScriptAsync("window.setStartPageCleared && window.setStartPageCleared(false);");
		}
	}

	private void UpdateSecurityIcon(BrowserTab tab)
	{
		if (BtnSecurity != null)
		{
			if (string.IsNullOrEmpty(tab.Url) || tab.Url.StartsWith("zenith://", StringComparison.OrdinalIgnoreCase) || tab.Url == "about:blank")
			{
				BtnSecurity.Visibility = Visibility.Collapsed;
			}
			else if (tab.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			{
				BtnSecurity.Visibility = Visibility.Visible;
				SetSecurityIconGlyph("\ue72e", new SolidColorBrush(Color.FromRgb(85, 85, 85)));
				BtnSecurity.ToolTip = "Website identification";
			}
			else
			{
				BtnSecurity.Visibility = Visibility.Visible;
				SetSecurityIconGlyph("\ue946", new SolidColorBrush(Color.FromRgb(118, 118, 118)));
				BtnSecurity.ToolTip = "Website identification (Not secure)";
			}
		}
	}

	private void SetSecurityIconGlyph(string glyph, Brush brush)
	{
		if (BtnSecurity.Template?.FindName("SecurityIcon", BtnSecurity) is TextBlock textBlock)
		{
			textBlock.Text = glyph;
			textBlock.Foreground = brush;
		}
	}

	private void BtnSecurity_Click(object sender, RoutedEventArgs e)
	{
		BrowserTab selectedTab = ViewModel.SelectedTab;
		if (selectedTab == null || string.IsNullOrEmpty(selectedTab.Url))
		{
			return;
		}
		bool flag = selectedTab.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
		string text = "";
		try
		{
			text = new Uri(selectedTab.Url).Host;
		}
		catch
		{
			text = selectedTab.Url;
		}
		SecurityDomainText.Text = text;
		if (flag)
		{
			string text2 = "An authorized Certificate Authority";
			if (_currentCertificate != null)
			{
				text2 = ExtractCaName(_currentCertificate.Issuer);
			}
			SecurityCertAuthorityText.Text = text2 + " has verified that you are connected to";
			SecurityCertAuthorityText.Visibility = Visibility.Visible;
			SecurityEncryptionText.Text = "Your connection to this site is encrypted to prevent snooping.";
			BtnShowCertDetails.Visibility = Visibility.Visible;
		}
		else
		{
			SecurityCertAuthorityText.Visibility = Visibility.Collapsed;
			SecurityEncryptionText.Text = "Your connection to this site is not secure. Any information you submit could be viewed by others.";
			BtnShowCertDetails.Visibility = Visibility.Collapsed;
		}
		// Measure real content height
		SecurityBorder.Height = double.NaN;
		SecurityBorder.Measure(new Size(370.0, double.PositiveInfinity));
		double targetH = SecurityBorder.DesiredSize.Height;
		if (targetH < 50) targetH = 200.0; // fallback

		SecurityBorder.Height = 0.0;
		SecurityPopup.IsOpen = true;

		var openAnim = new DoubleAnimation(0.0, targetH, new Duration(TimeSpan.FromMilliseconds(220)))
		{
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
			FillBehavior = FillBehavior.Stop,
		};
		openAnim.Completed += (_, __) =>
		{
			SecurityBorder.BeginAnimation(FrameworkElement.HeightProperty, null);
			SecurityBorder.Height = double.NaN;
		};
		SecurityBorder.BeginAnimation(FrameworkElement.HeightProperty, openAnim);
	}

	private static string ExtractCaName(string issuer)
	{
		if (string.IsNullOrEmpty(issuer))
		{
			return "An authorized Certificate Authority";
		}
		string[] array = issuer.Split(',');
		foreach (string text in array)
		{
			string text2 = text.Trim();
			if (text2.StartsWith("O=", StringComparison.OrdinalIgnoreCase))
			{
				return text2.Substring(2).Trim().Trim('"');
			}
		}
		string[] array2 = issuer.Split(',');
		foreach (string text3 in array2)
		{
			string text4 = text3.Trim();
			if (text4.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
			{
				return text4.Substring(3).Trim().Trim('"');
			}
		}
		return "An authorized Certificate Authority";
	}

	private async Task UpdateSecurityDetailsAsync(string url)
	{
		if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			_currentCertificate = null;
			return;
		}
		try
		{
			_currentCertificate = await GetCertificateAsync(url);
		}
		catch
		{
			_currentCertificate = null;
		}
	}

	private static async Task<X509Certificate2?> GetCertificateAsync(string url)
	{
		try
		{
			Uri uri = new Uri(url);
			using HttpClientHandler handler = new HttpClientHandler();
			X509Certificate2 cert = null;
			handler.ServerCertificateCustomValidationCallback = (HttpRequestMessage httpRequestMessage, X509Certificate2? c, X509Chain? chain, SslPolicyErrors errors) =>
			{
				if (c != null)
				{
					cert = new X509Certificate2(c);
				}
				return true;
			};
			using HttpClient client = new HttpClient(handler)
			{
				Timeout = TimeSpan.FromSeconds(3L)
			};
			HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Head, uri);
			await client.SendAsync(req);
			return cert;
		}
		catch
		{
			return null;
		}
	}

	private void BtnShowCertDetails_Click(object sender, RoutedEventArgs e)
	{
		SecurityPopup.IsOpen = false;
		if (_currentCertificate != null)
		{
			try
			{
				X509Certificate2UI.DisplayCertificate(_currentCertificate);
				return;
			}
			catch
			{
				MessageBox.Show($"Certificate Details:\n\nSubject: {_currentCertificate.Subject}\nIssuer: {_currentCertificate.Issuer}\nThumbprint: {_currentCertificate.Thumbprint}\nValid: {_currentCertificate.NotBefore:d} - {_currentCertificate.NotAfter:d}", "Certificate", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				return;
			}
		}
		MessageBox.Show("Connection to " + SecurityDomainText.Text + " is encrypted via HTTPS with TLS.", "Website identification", MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	private void Tab_Click(object sender, RoutedEventArgs e)
	{
		if (sender is RadioButton { DataContext: BrowserTab dataContext })
		{
			ViewModel.SelectTab(dataContext);
		}
	}

	public void OpenInNewTab(string url)
	{
		BrowserTab tab = ViewModel.NewTab(url);
		SwitchToBrowser(tab);
		((DispatcherObject)this).Dispatcher.InvokeAsync((Action)(() =>
		{
			AnimateNewTabOpen();
		}), (DispatcherPriority)6);
	}

	private void NewTab_Click(object sender, RoutedEventArgs e)
	{
		BrowserTab tab = ViewModel.NewTab();
		SwitchToBrowser(tab);
		((DispatcherObject)this).Dispatcher.InvokeAsync((Action)(() =>
		{
			AnimateNewTabOpen();
		}), (DispatcherPriority)6);
	}

	private void AnimateNewTabOpen()
	{
		if (TabsControl.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated)
		{
			return;
		}
		int num = ViewModel.Tabs.Count - 1;
		if (num < 0)
		{
			return;
		}
		FrameworkElement container = TabsControl.ItemContainerGenerator.ContainerFromIndex(num) as FrameworkElement;
		if (container != null)
		{
			TranslateTransform translateTransform = new TranslateTransform(0.0, 32.0);
			container.RenderTransform = translateTransform;
			DoubleAnimation doubleAnimation = new DoubleAnimation(32.0, 0.0, new Duration(TimeSpan.FromMilliseconds(350L)))
			{
				EasingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				},
				FillBehavior = FillBehavior.Stop
			};
			doubleAnimation.Completed += (object? s, EventArgs e) =>
			{
				container.RenderTransform = null;
			};
			translateTransform.BeginAnimation(TranslateTransform.YProperty, doubleAnimation);
		}
	}

	private void CloseTabWithAnimation(BrowserTab tab)
	{
		int num = ViewModel.Tabs.IndexOf(tab);
		if (_browsers.TryGetValue(tab.Id, out ChromiumWebBrowser? browserToRemove))
		{
			_browsers.Remove(tab.Id);
			try { browserToRemove.Dispose(); } catch { }
		}
		if (num < 0)
		{
			ViewModel.CloseTab(tab);
			return;
		}
		double num2 = ((TabsControl.ItemContainerGenerator.ContainerFromIndex(num) is FrameworkElement frameworkElement) ? frameworkElement.ActualWidth : 180.0);
		if (num2 <= 0.0)
		{
			num2 = 180.0;
		}
		ViewModel.CloseTab(tab);
		TabsControl.UpdateLayout();
		for (int i = num; i < ViewModel.Tabs.Count; i++)
		{
			if (TabsControl.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement frameworkElement2)
			{
				TranslateTransform translateTransform = (TranslateTransform)(frameworkElement2.RenderTransform = new TranslateTransform(num2, 0.0));
				DoubleAnimation doubleAnimation = new DoubleAnimation(num2, 0.0, new Duration(TimeSpan.FromMilliseconds(350L)))
				{
					EasingFunction = new CubicEase
					{
						EasingMode = EasingMode.EaseOut
					},
					FillBehavior = FillBehavior.Stop
				};
				FrameworkElement captured = frameworkElement2;
				doubleAnimation.Completed += (object? s, EventArgs e) =>
				{
					captured.RenderTransform = null;
				};
				translateTransform.BeginAnimation(TranslateTransform.XProperty, doubleAnimation);
			}
		}
	}

	private ChromiumWebBrowser? GetActiveWebBrowser()
	{
		ChromiumWebBrowser value;
		return (ViewModel.SelectedTab != null && _browsers.TryGetValue(ViewModel.SelectedTab.Id, out value)) ? value : null;
	}

	private void BtnBack_Click(object sender, RoutedEventArgs e)
	{
		GetActiveWebBrowser()?.GetBrowser()?.GoBack();
	}

	private void BtnForward_Click(object sender, RoutedEventArgs e)
	{
		GetActiveWebBrowser()?.GetBrowser()?.GoForward();
	}

	private void BtnRefresh_Click(object sender, RoutedEventArgs e)
	{
		ChromiumWebBrowser activeWebBrowser = GetActiveWebBrowser();
		if (activeWebBrowser == null)
		{
			return;
		}
		BrowserTab? selectedTab = ViewModel.SelectedTab;
		if (selectedTab != null && selectedTab.IsLoading)
		{
			activeWebBrowser.GetBrowser()?.StopLoad();
			return;
		}
		BrowserTab? selectedTab2 = ViewModel.SelectedTab;
		if (selectedTab2 != null && selectedTab2.Url.StartsWith("zenith://", StringComparison.OrdinalIgnoreCase))
		{
			activeWebBrowser.Load("zenith://newtab");
		}
		else
		{
			activeWebBrowser.GetBrowser()?.Reload();
		}
	}

	private void AddressBox_KeyDown(object sender, KeyEventArgs e)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Invalid comparison between Unknown and I4
		if ((int)e.Key != 6)
		{
			return;
		}
		string text = AddressBox.Text.Trim();
		if (ViewModel.SelectedTab != null && _browsers.TryGetValue(ViewModel.SelectedTab.Id, out ChromiumWebBrowser value))
		{
			if (string.IsNullOrEmpty(text) || text.Equals("zenith://newtab", StringComparison.OrdinalIgnoreCase) || text.Equals("about:blank", StringComparison.OrdinalIgnoreCase) || text.Equals("zenith:newtab", StringComparison.OrdinalIgnoreCase))
			{
				value.Load("zenith://newtab");
				ViewModel.SelectedTab.Url = "zenith://newtab";
				ViewModel.SelectedTab.Title = LangService.T("tab.newtab");
				ViewModel.SelectedTab.Favicon = null;
				AddressBox.Text = "";
			}
			else
			{
				string url = NormalizeUrl(text);
				value.Load(url);
			}
			BrowserContainer.Focus();
		}
		e.Handled = true;
	}

	private void AddressBox_GotFocus(object sender, RoutedEventArgs e)
	{
		AddressBox.SelectAll();
	}

	private void AddressBox_LostFocus(object sender, RoutedEventArgs e)
	{
		if (ViewModel.SelectedTab != null)
		{
			AddressBox.Text = ViewModel.SelectedTab.DisplayUrl;
		}
		if (_isAddressBarActiveOnStartPage)
		{
			DeactivateAddressBarOnStartPage();
		}
	}

	private static string NormalizeUrl(string input)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return "zenith://newtab";
		}
		input = input.Trim();
		if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || input.StartsWith("file://", StringComparison.OrdinalIgnoreCase) || input.StartsWith("about:", StringComparison.OrdinalIgnoreCase) || input.StartsWith("zenith:", StringComparison.OrdinalIgnoreCase) || input.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
		{
			return input;
		}
		if (input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(input.Split(':')[0].Split('/')[0], out IPAddress _))
		{
			return "http://" + input;
		}
		if (!input.Contains(' ') && input.Contains('.'))
		{
			string text = input.Split('/')[0].Split('?')[0].Split(':')[0];
			int num = text.LastIndexOf('.');
			if (num > 0 && num < text.Length - 1)
			{
				string text2 = text.Substring(num + 1);
				if (text2.Length >= 2 && text2.All(char.IsLetter))
				{
					return "https://" + input;
				}
			}
		}
		return SettingsService.BuildSearchUrl(input);
	}

	private void BtnMinimize_Click(object sender, RoutedEventArgs e)
	{
		WindowState = WindowState.Minimized;
	}

	private void BtnMaximize_Click(object sender, RoutedEventArgs e)
	{
		WindowState = ((WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
	}

	private void BtnClose_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void Window_StateChanged(object sender, EventArgs e)
	{
		BtnMaximize.Content = ((WindowState == WindowState.Maximized) ? "\ue923" : "\ue922");
		BtnMaximize.ToolTip = ((WindowState == WindowState.Maximized) ? "Restore" : "Maximize");
		if (WindowState == WindowState.Maximized)
		{
			WindowBorder.BorderThickness = new Thickness(0.0);
			WindowBorder.Padding = new Thickness(6.0);
		}
		else
		{
			WindowBorder.BorderThickness = new Thickness(1.0);
			WindowBorder.Padding = new Thickness(0.0);
			WindowBorder.BorderBrush = (IsActive ? ((Brush)FindResource("AccentBrush")) : new SolidColorBrush(Color.FromRgb(112, 112, 112)));
		}
	}

	private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		object originalSource = e.OriginalSource;
		DependencyObject val = (DependencyObject)((originalSource is DependencyObject) ? originalSource : null);
		if (val != null && (FindVisualParent<Button>(val) != null || FindVisualParent<RadioButton>(val) != null))
		{
			return;
		}
		if (e.ClickCount == 2)
		{
			BtnMaximize_Click(sender, e);
		}
		else if (e.ButtonState == MouseButtonState.Pressed)
		{
			if (WindowState == WindowState.Maximized)
			{
				Point val2 = PointToScreen(e.GetPosition(this));
				Point position = e.GetPosition(this);
				double num = position.X / ActualWidth;
				WindowState = WindowState.Normal;
				Left = val2.X - ActualWidth * num;
				double y = val2.Y;
				position = e.GetPosition(this);
				Top = y - position.Y;
			}
			try
			{
				DragMove();
			}
			catch
			{
			}
		}
	}

	private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
	{
		while (child != null)
		{
			T val = (T)(object)((child is T) ? child : null);
			if (val != null)
			{
				return val;
			}
			child = VisualTreeHelper.GetParent(child);
		}
		return default;
	}

	private void BtnHub_Click(object sender, RoutedEventArgs e)
	{
		if (HubPane.Visibility == Visibility.Visible)
		{
			CloseHub();
		}
		else
		{
			OpenHub();
		}
	}

	private void OpenHub()
	{
		CloseSettings();
		_isHubPinned = false;
		UpdateHubPinUI();
		HubPane.Visibility = Visibility.Visible;
		HubActiveIndicator.Visibility = Visibility.Visible;
		HubIconText.Foreground = (Brush)FindResource("AccentBrush");
		DoubleAnimation animation = new DoubleAnimation(360.0, 0.0, new Duration(TimeSpan.FromMilliseconds(350L)))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			},
			FillBehavior = FillBehavior.HoldEnd
		};
		HubPaneTranslate.BeginAnimation(TranslateTransform.XProperty, animation);
	}

	private void CloseHub()
	{
		_isHubPinned = false;
		UpdateHubPinUI();
		HubActiveIndicator.Visibility = Visibility.Collapsed;
		HubIconText.Foreground = (Brush)FindResource("TextPrimary");
		if (HubPane.Visibility == Visibility.Visible)
		{
			DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, 360.0, new Duration(TimeSpan.FromMilliseconds(260L)))
			{
				EasingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseIn
				},
				FillBehavior = FillBehavior.HoldEnd
			};
			doubleAnimation.Completed += (object? s, EventArgs e) =>
			{
				HubPane.Visibility = Visibility.Collapsed;
				HubPaneTranslate.X = 360.0;
				HubPaneTranslate.BeginAnimation(TranslateTransform.XProperty, null);
			};
			HubPaneTranslate.BeginAnimation(TranslateTransform.XProperty, doubleAnimation);
		}
	}

	private void PinHub_Click(object sender, RoutedEventArgs e)
	{
		_isHubPinned = !_isHubPinned;
		UpdateHubPinUI();
	}

	private void UpdateHubPinUI()
	{
		if (_isHubPinned)
		{
			PinHubIcon.Foreground = (Brush)FindResource("AccentBrush");
			BtnPinHub.ToolTip = "Unpin pane";
			HubShadow.Opacity = 0.0;
			Grid.SetColumn(HubPane, 1);
			Grid.SetColumnSpan(HubPane, 1);
			HubPane.HorizontalAlignment = HorizontalAlignment.Stretch;
			SidePaneColumn.Width = GridLength.Auto;
		}
		else
		{
			PinHubIcon.Foreground = new SolidColorBrush(Color.FromRgb(85, 85, 85));
			BtnPinHub.ToolTip = "Pin pane";
			HubShadow.Opacity = 0.2;
			Grid.SetColumn(HubPane, 0);
			Grid.SetColumnSpan(HubPane, 2);
			HubPane.HorizontalAlignment = HorizontalAlignment.Right;
			SidePaneColumn.Width = new GridLength(0.0);
		}
	}

	private void HubTab_Click(object sender, RoutedEventArgs e)
	{
		int newIndex = 2;
		UIElement target = HubHistoryView;
		if (TabHubFavorites.IsChecked == true) { newIndex = 0; target = HubFavoritesView; }
		else if (TabHubReading.IsChecked == true) { newIndex = 1; target = HubReadingView; }
		else if (TabHubHistory.IsChecked == true) { newIndex = 2; target = HubHistoryView; }
		else if (TabHubDownloads.IsChecked == true) { newIndex = 3; target = HubDownloadsView; }
		else if (TabHubExtensions.IsChecked == true) { newIndex = 4; target = HubExtensionsView; }

		if (newIndex == _currentHubTabIndex) return;

		UIElement current = _currentHubTabIndex switch
		{
			0 => HubFavoritesView,
			1 => HubReadingView,
			2 => HubHistoryView,
			3 => HubDownloadsView,
			4 => HubExtensionsView,
			_ => HubHistoryView
		};

		bool movingRight = newIndex > _currentHubTabIndex;
		_currentHubTabIndex = newIndex;

		// Authentic Edge 1507 Hub Tab Slide Animation
		double targetStartX = movingRight ? 360 : -360;
		double currentEndX  = movingRight ? -360 : 360;

		var targetTranslate = target.RenderTransform as TranslateTransform;
		if (targetTranslate == null)
		{
			targetTranslate = new TranslateTransform();
			target.RenderTransform = targetTranslate;
		}

		var currentTranslate = current.RenderTransform as TranslateTransform;
		if (currentTranslate == null)
		{
			currentTranslate = new TranslateTransform();
			current.RenderTransform = currentTranslate;
		}

		target.Visibility = Visibility.Visible;
		target.Opacity = 1;
		current.Opacity = 1;

		targetTranslate.X = targetStartX;
		var animTarget = new DoubleAnimation(targetStartX, 0, new Duration(TimeSpan.FromMilliseconds(300)))
		{
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
			FillBehavior = FillBehavior.HoldEnd
		};
		animTarget.Completed += (s, e2) =>
		{
			targetTranslate.X = 0;
			targetTranslate.BeginAnimation(TranslateTransform.XProperty, null);
		};
		targetTranslate.BeginAnimation(TranslateTransform.XProperty, animTarget);

		var animCurrent = new DoubleAnimation(0, currentEndX, new Duration(TimeSpan.FromMilliseconds(300)))
		{
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
			FillBehavior = FillBehavior.HoldEnd
		};
		var capturedCurrent = current;
		animCurrent.Completed += (s, e2) =>
		{
			capturedCurrent.Visibility = Visibility.Collapsed;
			currentTranslate.X = 0;
			currentTranslate.BeginAnimation(TranslateTransform.XProperty, null);
		};
		currentTranslate.BeginAnimation(TranslateTransform.XProperty, animCurrent);
	}

	private void OpenWebStore_Click(object sender, RoutedEventArgs e)
	{
		CloseHub();
		BrowserTab? tab = ViewModel.SelectedTab;
		if (tab != null)
		{
			tab.Url = "https://chromewebstore.google.com/";
			tab.Title = "Chrome Web Store";
			ChromiumWebBrowser activeWebBrowser = GetActiveWebBrowser();
			activeWebBrowser?.Load("https://chromewebstore.google.com/");
		}
		else
		{
			BrowserTab newTab = ViewModel.NewTab("https://chromewebstore.google.com/");
			SwitchToBrowser(newTab);
		}
	}


	// ══════════════════════════════════════════════════════════════════════
	// Extension toggle handlers (Hub > Extensions tab)
	// ══════════════════════════════════════════════════════════════════════

	private void SyncExtensionCheckboxes()
	{
		if (ExtAdBlockCheck != null) ExtAdBlockCheck.IsChecked = ZenithExtensionService.AdBlockerEnabled;
	}

	private void ExtAdBlock_Changed(object sender, RoutedEventArgs e)
	{
		if (ExtAdBlockCheck == null) return;
		bool enabled = ExtAdBlockCheck.IsChecked == true;
		ZenithExtensionService.AdBlockerEnabled = enabled;
		// Keep settings pane checkbox in sync
		if (AdBlockCheck != null) AdBlockCheck.IsChecked = enabled;
		SettingsService.AdBlockEnabled = enabled;
		SettingsService.Save();
		UpdateAdBlockStatusText();
	}




	private void HistoryItem_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { DataContext: HistoryItem dataContext } && ViewModel.SelectedTab != null && _browsers.TryGetValue(ViewModel.SelectedTab.Id, out ChromiumWebBrowser value))
		{
			value.Load(dataContext.Url);
		}
	}

	// ══════════════════════════════════════════════════════════════════════
	// Favorites handlers
	// ══════════════════════════════════════════════════════════════════════

	private void UpdateFavoriteStar(string? url)
	{
		if (FavoriteStarIcon == null || BtnFavorite == null) return;

		if (string.IsNullOrEmpty(url) || url.StartsWith("zenith://", StringComparison.OrdinalIgnoreCase) || url == "about:blank")
		{
			BtnFavorite.Visibility = Visibility.Collapsed;
			if (BtnReadingView != null) BtnReadingView.Visibility = Visibility.Collapsed;
			return;
		}

		BtnFavorite.Visibility = Visibility.Visible;
		if (BtnReadingView != null) BtnReadingView.Visibility = Visibility.Visible;

		bool isFav = FavoritesService.IsFavorite(url);
		if (isFav)
		{
			FavoriteStarIcon.Text = "\ue735"; // filled star
			FavoriteStarIcon.Foreground = new SolidColorBrush(Color.FromRgb(255, 185, 0)); // Yellow / Gold (#FFB900)
			BtnFavorite.ToolTip = "Remove from favorites (Ctrl+D)";
		}
		else
		{
			FavoriteStarIcon.Text = "\ue734"; // outline star
			FavoriteStarIcon.Foreground = new SolidColorBrush(Color.FromRgb(85, 85, 85)); // Neutral gray (#555555)
			BtnFavorite.ToolTip = "Add to favorites (Ctrl+D)";
		}
	}

	private void BtnFavorite_Click(object sender, RoutedEventArgs e)
	{
		var tab = ViewModel.SelectedTab;
		if (tab == null || string.IsNullOrEmpty(tab.Url) || tab.Url.StartsWith("zenith://", StringComparison.OrdinalIgnoreCase) || tab.Url == "about:blank")
			return;

		FavoritesService.ToggleFavorite(tab.Title, tab.Url, tab.Favicon);
		UpdateFavoriteStar(tab.Url);
		UpdateFavoritesHubList();
	}

	private void BtnReadingView_Click(object sender, RoutedEventArgs e)
	{
		// Reading view toggle
	}

	private void FavoriteItem_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { DataContext: FavoriteItem item } && ViewModel.SelectedTab != null && _browsers.TryGetValue(ViewModel.SelectedTab.Id, out ChromiumWebBrowser browser))
		{
			browser.Load(item.Url);
		}
	}

	private void DeleteFavorite_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { DataContext: FavoriteItem item })
		{
			FavoritesService.RemoveFavorite(item.Url);
			UpdateFavoriteStar(ViewModel.SelectedTab?.Url);
			UpdateFavoritesHubList();
			e.Handled = true;
		}
	}

	private void UpdateFavoritesHubList()
	{
		if (HubFavoritesList == null) return;
		HubFavoritesList.ItemsSource = null;
		HubFavoritesList.ItemsSource = FavoritesService.Items;
		if (HubFavEmptyText != null)
		{
			HubFavEmptyText.Visibility = FavoritesService.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		}
	}


	// ══════════════════════════════════════════════════════════════════════
	// Settings pane handlers
	// ══════════════════════════════════════════════════════════════════════

	/// <summary>Called when SettingsScrollViewer is loaded — populates controls from SettingsService.</summary>
	private void Settings_Loaded(object sender, System.Windows.RoutedEventArgs e)
	{
		// Search engine
		_suppressSearchEngineChanged = true;
		switch (SettingsService.SearchEngine)
		{
			case "Bing":       SearchEngineCombo.SelectedIndex = 1; break;
			case "DuckDuckGo": SearchEngineCombo.SelectedIndex = 2; break;
			default:           SearchEngineCombo.SelectedIndex = 0; break;
		}
		_suppressSearchEngineChanged = false;

		// Open with
		switch (SettingsService.OpenWith)
		{
			case "StartPage":    OpenWithStartPage.IsChecked = true; break;
			case "PreviousPages": OpenWithPrevious.IsChecked = true; break;
			case "SpecificPage":
				OpenWithSpecific.IsChecked = true;
				HomepagePanel.Visibility = System.Windows.Visibility.Visible;
				HomepageBox.Text = SettingsService.Homepage;
				break;
			default:            OpenWithNewTab.IsChecked = true;    break;
		}

		// Favorites bar
		if (FavoritesBarCheck != null) FavoritesBarCheck.IsChecked = SettingsService.ShowFavoritesBar;
		ApplyFavoritesBarVisibility();

		// AdBlock
		if (AdBlockCheck != null) AdBlockCheck.IsChecked = SettingsService.AdBlockEnabled;
		ZenithAdBlockService.IsEnabled = SettingsService.AdBlockEnabled;
		ZenithAdBlockService.BlockedCountChanged -= OnBlockedCountChanged;
		ZenithAdBlockService.BlockedCountChanged += OnBlockedCountChanged;
		UpdateAdBlockStatusText();

		// Sync extension checkboxes
		SyncExtensionCheckboxes();

		// Language combo
		LangService.LanguageChanged -= ApplyLanguage;
		PopulateLanguageCombo();
		LangService.LanguageChanged += ApplyLanguage;
		ApplyLanguage();

		// About: show actual Chromium version
		if (AboutCefText != null)
			AboutCefText.Text = $"Powered by CefSharp {CefSharp.Cef.CefVersion} / Chromium {CefSharp.Cef.ChromiumVersion}";
	}

	private void OpenWith_Changed(object sender, System.Windows.RoutedEventArgs e)
	{
		if (OpenWithStartPage == null) return; // not yet loaded
		if (OpenWithStartPage.IsChecked == true)         SettingsService.OpenWith = "StartPage";
		else if (OpenWithNewTab.IsChecked == true)        SettingsService.OpenWith = "NewTab";
		else if (OpenWithPrevious.IsChecked == true)      SettingsService.OpenWith = "PreviousPages";
		else if (OpenWithSpecific.IsChecked == true)      SettingsService.OpenWith = "SpecificPage";

		bool showHomepage = SettingsService.OpenWith == "SpecificPage";
		if (HomepagePanel != null)
			HomepagePanel.Visibility = showHomepage
				? System.Windows.Visibility.Visible
				: System.Windows.Visibility.Collapsed;

		SettingsService.Save();
	}

	private void HomepageBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
	{
		if (HomepageBox == null) return;
		var text = HomepageBox.Text.Trim();
		if (!string.IsNullOrEmpty(text))
		{
			SettingsService.Homepage = NormalizeUrl(text);
			HomepageBox.Text = SettingsService.Homepage;
			SettingsService.Save();
		}
	}

	private void UseCurrentPage_Click(object sender, System.Windows.RoutedEventArgs e)
	{
		var tab = ViewModel?.SelectedTab;
		if (tab == null || HomepageBox == null) return;
		string url = tab.Url ?? "zenith://newtab";
		SettingsService.Homepage = url;
		HomepageBox.Text = url;
		SettingsService.Save();
	}

	private bool _suppressSearchEngineChanged = false;
	private void SearchEngine_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
	{
		if (_suppressSearchEngineChanged) return;
		if (SearchEngineCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem item)
		{
			SettingsService.SearchEngine = item.Content?.ToString() ?? "DuckDuckGo";
			SettingsService.Save();

			// Reload active startpage if currently open
			var tab = ViewModel?.SelectedTab;
			if (tab != null && tab.Url.StartsWith("zenith://", StringComparison.OrdinalIgnoreCase))
			{
				var browser = GetActiveWebBrowser();
				browser?.Reload();
			}
		}
	}

	private void FavoritesBar_Changed(object sender, System.Windows.RoutedEventArgs e)
	{
		if (FavoritesBarCheck == null) return;
		SettingsService.ShowFavoritesBar = FavoritesBarCheck.IsChecked == true;
		SettingsService.Save();
		ApplyFavoritesBarVisibility();
	}

	private void ApplyFavoritesBarVisibility()
	{
		if (FavoritesBar == null) return;
		bool show = SettingsService.ShowFavoritesBar;
		FavoritesBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
		if (FavoritesBarRow != null)
			FavoritesBarRow.Height = show ? new GridLength(32) : new GridLength(0);
	}

	private void OnBlockedCountChanged()
	{
		Dispatcher.InvokeAsync(UpdateAdBlockStatusText);
	}

	private void AdBlock_Changed(object sender, RoutedEventArgs e)
	{
		if (AdBlockCheck == null) return;
		bool enabled = AdBlockCheck.IsChecked == true;
		SettingsService.AdBlockEnabled = enabled;
		ZenithAdBlockService.IsEnabled = enabled;
		SettingsService.Save();
		UpdateAdBlockStatusText();
	}

	private void UpdateAdBlockStatusText()
	{
		if (AdBlockStatus != null)
			AdBlockStatus.Text = ZenithAdBlockService.IsEnabled
				? LangService.T("settings.adblock.status_on")
				: LangService.T("settings.adblock.status_off");

		if (AdBlockCount != null)
			AdBlockCount.Text = ZenithAdBlockService.BlockedCount > 0
				? string.Format(LangService.T("settings.adblock.count"), ZenithAdBlockService.BlockedCount)
				: "";
	}

	private async void UpdateAdBlockFilters_Click(object sender, RoutedEventArgs e)
	{
		if (BtnUpdateFilters != null) BtnUpdateFilters.IsEnabled = false;
		await ZenithAdBlockService.UpdateFiltersAsync();
		if (BtnUpdateFilters != null)
		{
			BtnUpdateFilters.IsEnabled = true;
			BtnUpdateFilters.Content = LangService.T("settings.adblock.updated");
			var timer = new System.Windows.Threading.DispatcherTimer
				{ Interval = System.TimeSpan.FromSeconds(3) };
			timer.Tick += (s, _) =>
			{
				timer.Stop();
				BtnUpdateFilters.Content = LangService.T("settings.adblock.update");
			};
			timer.Start();
		}
	}

	private void ViewLicense_Click(object sender, System.Windows.RoutedEventArgs e)
	{
		var newTab = ViewModel.NewTab("https://raw.githubusercontent.com/FlatronTech/ZenithBrowser/refs/heads/main/LICENSE");
		SwitchToBrowser(newTab);
	}
	// ─── Language System ────────────────────────────────────────────────────────

	private void PopulateLanguageCombo()
	{
		if (LanguageCombo == null) return;
		_suppressLangChange = true;
		LanguageCombo.Items.Clear();
		var langs = LangService.AvailableLanguages();
		int selIdx = 0;
		for (int i = 0; i < langs.Count; i++)
		{
			var (code, name) = langs[i];
			LanguageCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = name, Tag = code });
			if (code == LangService.CurrentCode) selIdx = i;
		}
		LanguageCombo.SelectedIndex = selIdx;
		_suppressLangChange = false;
	}

	private bool _suppressLangChange = false;

	private void Language_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
	{
		if (_suppressLangChange) return;
		if (LanguageCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string code)
			LangService.Switch(code);
	}

	private void ApplyLanguage()
	{
		// ── Settings header ──────────────────────────────────────────────────────
		if (SettingsTitle != null)              SettingsTitle.Text              = LangService.T("settings.title");
		if (BtnPinSettings != null)             BtnPinSettings.ToolTip          = LangService.T("settings.pin");

		// ── Settings sections — General ───────────────────────────────────────────
		if (SettingsGeneralHeader != null)      SettingsGeneralHeader.Text      = LangService.T("settings.section.general");
		if (SettingsOpenWithLabel != null)      SettingsOpenWithLabel.Text      = LangService.T("settings.open_with");
		if (OpenWithStartPage != null)          OpenWithStartPage.Content       = LangService.T("settings.open_with.start");
		if (OpenWithNewTab != null)             OpenWithNewTab.Content          = LangService.T("settings.open_with.newtab");
		if (OpenWithPrevious != null)           OpenWithPrevious.Content        = LangService.T("settings.open_with.previous");
		if (OpenWithSpecific != null)           OpenWithSpecific.Content        = LangService.T("settings.open_with.specific");
		if (SettingsSearchWithLabel != null)    SettingsSearchWithLabel.Text    = LangService.T("settings.search_with");

		// ── Settings sections — Appearance ───────────────────────────────────────
		if (SettingsAppearanceHeader != null)   SettingsAppearanceHeader.Text   = LangService.T("settings.section.appearance");
		if (SettingsShowFavBarLabel != null)    SettingsShowFavBarLabel.Text    = LangService.T("settings.show_favbar");
		if (FavoritesBarCheck != null)          FavoritesBarCheck.Content       = LangService.T("settings.show_favbar");

		// ── Settings sections — Privacy & Data ───────────────────────────────────
		if (SettingsPrivacyHeader != null)      SettingsPrivacyHeader.Text      = LangService.T("settings.section.privacy");
		if (SettingsClearDataLabel != null)     SettingsClearDataLabel.Text     = LangService.T("settings.clear_data");

		// ── Settings sections — Reading ───────────────────────────────────────────
		if (SettingsReadingHeader != null)      SettingsReadingHeader.Text      = LangService.T("settings.section.reading");
		if (SettingsReadingStyleLabel != null)  SettingsReadingStyleLabel.Text  = LangService.T("settings.reading_style");
		if (SettingsReadingFontLabel != null)   SettingsReadingFontLabel.Text   = LangService.T("settings.reading_font");

		// ── Settings sections — Language ──────────────────────────────────────────
		if (SettingsLanguageHeader != null)     SettingsLanguageHeader.Text     = LangService.T("settings.section.language");
		if (SettingsLanguageLabel != null)      SettingsLanguageLabel.Text      = LangService.T("settings.language");

		// ── Settings sections — AdBlock ───────────────────────────────────────────
		if (SettingsAdBlockHeader != null) SettingsAdBlockHeader.Text = LangService.T("settings.section.adblock");
		if (AdBlockLabel != null)          AdBlockLabel.Text          = LangService.T("settings.adblock.enabled");
		if (BtnUpdateFilters != null)      BtnUpdateFilters.Content   = LangService.T("settings.adblock.update");
		UpdateAdBlockStatusText();

		// ── Settings sections — About (hardcoded in code, not in JSON) ───────────
		bool isPl = string.Equals(LangService.CurrentCode, "pl", StringComparison.OrdinalIgnoreCase);
		if (SettingsAboutHeader != null) SettingsAboutHeader.Text = isPl ? "O tej aplikacji" : "About this app";
		if (AboutAppNameText != null)    AboutAppNameText.Text    = isPl ? "Zenith" : "Zenith";
		if (AboutEditionText != null)    AboutEditionText.Visibility = Visibility.Collapsed;
		if (AboutVersionText != null)    AboutVersionText.Text    = isPl ? "Wersja 1.0 (2026116109)" : "Version 1.0 (2026116109)";
		if (AboutCefText != null)        AboutCefText.Text        = isPl
			? $"Oparte na CefSharp {CefSharp.Cef.CefVersion} / Chromium {CefSharp.Cef.ChromiumVersion}"
			: $"Powered by CefSharp {CefSharp.Cef.CefVersion} / Chromium {CefSharp.Cef.ChromiumVersion}";
		if (BtnViewLicense != null)      BtnViewLicense.Content   = isPl ? "Wyświetl licencję Zenith" : "View Zenith license";

		// ── Hub pane headers ─────────────────────────────────────────────────────
		if (HubFavHeader != null)               HubFavHeader.Text               = LangService.T("hub.favorites");
		if (HubReadingHeader != null)           HubReadingHeader.Text           = LangService.T("hub.reading");
		if (HubHistoryHeader != null)           HubHistoryHeader.Text           = LangService.T("hub.history");
		if (HubDownloadsHeader != null)         HubDownloadsHeader.Text         = LangService.T("hub.downloads");
		if (HubExtensionsHeader != null)        HubExtensionsHeader.Text        = LangService.T("hub.extensions");

		// ── More menu items ──────────────────────────────────────────────────────
		if (MenuNewWindow != null)              MenuNewWindow.Header            = LangService.T("menu.new_window");
		if (MenuNewInPrivate != null)           MenuNewInPrivate.Header         = LangService.T("menu.new_inprivate");
		if (MenuFindOnPage != null)             MenuFindOnPage.Header           = LangService.T("menu.find_on_page");
		if (MenuPrint != null)                  MenuPrint.Header                = LangService.T("menu.print");
		if (MenuPinToStart != null)             MenuPinToStart.Header           = LangService.T("menu.pin_to_start");
		if (MenuDevTools != null)               MenuDevTools.Header             = LangService.T("menu.dev_tools");
		if (MenuFeedback != null)               MenuFeedback.Header             = LangService.T("menu.send_feedback");
		if (MenuSettings != null)               MenuSettings.Header             = LangService.T("menu.settings");

		// Navigation & window controls tooltips
		if (BtnNewTab != null)                  BtnNewTab.ToolTip               = LangService.T("tab.newtab_tooltip");
		if (BtnMinimize != null)                BtnMinimize.ToolTip             = LangService.T("window.minimize");
		if (BtnMaximize != null)                BtnMaximize.ToolTip             = LangService.T("window.maximize");
		if (BtnClose != null)                   BtnClose.ToolTip                = LangService.T("window.close");

		if (BtnBack != null)                    BtnBack.ToolTip                 = LangService.T("nav.back");
		if (BtnForward != null)                 BtnForward.ToolTip              = LangService.T("nav.forward");
		if (BtnRefresh != null)
		{
			var selTab = ViewModel?.SelectedTab;
			BtnRefresh.ToolTip = (selTab != null && selTab.IsLoading)
				? LangService.T("nav.stop")
				: LangService.T("nav.refresh");
		}
		if (BtnSecurity != null)                BtnSecurity.ToolTip             = LangService.T("nav.security_tooltip");
		if (BtnHub != null)                     BtnHub.ToolTip                  = LangService.T("nav.hub_tooltip");
		if (BtnWebNote != null)                 BtnWebNote.ToolTip              = LangService.T("nav.webnote_tooltip");
		if (BtnShare != null)                   BtnShare.ToolTip                = LangService.T("nav.share_tooltip");
		if (BtnMore != null)                    BtnMore.ToolTip                 = LangService.T("nav.more_tooltip");
		if (BtnPinHub != null)                  BtnPinHub.ToolTip               = LangService.T("hub.pin");

		// Hub navigation buttons tooltips
		if (TabHubFavorites != null)            TabHubFavorites.ToolTip         = LangService.T("hub.tab.favorites");
		if (TabHubReading != null)              TabHubReading.ToolTip           = LangService.T("hub.tab.reading");
		if (TabHubHistory != null)              TabHubHistory.ToolTip           = LangService.T("hub.tab.history");
		if (TabHubDownloads != null)            TabHubDownloads.ToolTip         = LangService.T("hub.tab.downloads");
		if (TabHubExtensions != null)           TabHubExtensions.ToolTip        = LangService.T("hub.tab.extensions");

		// Favorites bar placeholder text
		if (FavBarEmpty != null)                FavBarEmpty.Text                = LangService.T("favbar.empty");

		// Address bar placeholder text
		if (AddressPlaceholderText != null)     AddressPlaceholderText.Text     = LangService.T("addressbar.search_placeholder");

		// New tab titles
		foreach (var tab in ViewModel?.Tabs ?? Enumerable.Empty<Zenith.App.Models.BrowserTab>())
		{
			if (tab.Url?.StartsWith("zenith://", StringComparison.OrdinalIgnoreCase) == true || string.IsNullOrEmpty(tab.Url) || tab.Url == "about:blank")
				tab.Title = LangService.T("tab.newtab");
		}
	}



	// ══════════════════════════════════════════════════════════════════════
	// Fullscreen support
	// ══════════════════════════════════════════════════════════════════════

	private WindowStyle _prevWindowStyle;
	private WindowState _prevWindowState;
	private double _prevLeft, _prevTop, _prevWidth, _prevHeight;
	private bool _isFullscreen = false;

	[StructLayout(LayoutKind.Sequential)]
	private struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private class MONITORINFO
	{
		public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
		public RECT rcMonitor;
		public RECT rcWork;
		public int dwFlags;
	}

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

	private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

	public void HandleFullscreen(bool enterFullscreen)
	{
		if (enterFullscreen == _isFullscreen) return;
		_isFullscreen = enterFullscreen;

		if (enterFullscreen)
		{
			// Zapisz stan okna
			_prevWindowStyle = WindowStyle;
			_prevWindowState = WindowState;
			_prevLeft   = Left;
			_prevTop    = Top;
			_prevWidth  = Width;
			_prevHeight = Height;

			IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
			IntPtr monitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
			var mi = new MONITORINFO();
			GetMonitorInfo(monitor, mi);

			// Ukryj pasek zadań i ramki okna
			WindowStyle = WindowStyle.None;
			WindowState = WindowState.Normal;
			ResizeMode  = ResizeMode.NoResize;

			Left   = mi.rcMonitor.Left;
			Top    = mi.rcMonitor.Top;
			Width  = mi.rcMonitor.Right - mi.rcMonitor.Left;
			Height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;

			// Ukryj pasek kart i toolbar
			if (RootGrid != null)
			{
				RootGrid.RowDefinitions[0].Height = new GridLength(0); // tab bar
				RootGrid.RowDefinitions[1].Height = new GridLength(0); // toolbar
				RootGrid.RowDefinitions[2].Height = new GridLength(0); // favorites bar
			}
		}
		else
		{
			// Przywróć stan okna
			WindowStyle = _prevWindowStyle;
			ResizeMode  = ResizeMode.CanResize;
			Left   = _prevLeft;
			Top    = _prevTop;
			Width  = _prevWidth;
			Height = _prevHeight;
			WindowState = _prevWindowState;

			// Przywróć paski
			if (RootGrid != null)
			{
				RootGrid.RowDefinitions[0].Height = new GridLength(32);  // tab bar
				RootGrid.RowDefinitions[1].Height = new GridLength(48);  // toolbar
				RootGrid.RowDefinitions[2].Height = SettingsService.ShowFavoritesBar
					? new GridLength(32)
					: new GridLength(0);
			}
		}
	}

	private void ClearHistory_Click(object sender, RoutedEventArgs e)
	{
		HistoryService.ClearAll();
		if (ClearDataStatus != null)
		{
			ClearDataStatus.Text = "Browsing data cleared.";
			ClearDataStatus.Visibility = System.Windows.Visibility.Visible;
			var timer = new System.Windows.Threading.DispatcherTimer { Interval = System.TimeSpan.FromSeconds(3) };
			timer.Tick += (s, _) => { timer.Stop(); ClearDataStatus.Visibility = System.Windows.Visibility.Collapsed; };
			timer.Start();
		}
	}

	private void OpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
	{
		DownloadsService.OpenDownloadsFolder();
	}

	private void ClearDownloads_Click(object sender, RoutedEventArgs e)
	{
		DownloadsService.ClearPastDownloads();
	}

	private void ActiveDownloadPauseResume_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { DataContext: DownloadItemModel dataContext })
		{
			if (dataContext.IsPaused)
			{
				DownloadsService.ResumeDownload(dataContext.Id);
			}
			else
			{
				DownloadsService.PauseDownload(dataContext.Id);
			}
		}
	}

	private void ActiveDownloadCancel_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { DataContext: DownloadItemModel dataContext })
		{
			DownloadsService.CancelDownload(dataContext.Id);
		}
	}

	private void OpenDownloadedFile_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { DataContext: DownloadItemModel dataContext })
		{
			DownloadsService.OpenFile(dataContext.FullPath);
		}
	}

	private void RemovePastDownload_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { DataContext: DownloadItemModel dataContext })
		{
			DownloadsService.RemovePastDownload(dataContext);
		}
	}

	private void OnDownloadStarted(DownloadItemModel model)
	{
		((DispatcherObject)this).Dispatcher.InvokeAsync((Action)(() =>
		{
			_currentDownloadingModel = model;
			DownloadBar.Visibility = Visibility.Visible;
			DownloadBarTitleText.Text = $"{model.PercentComplete}% of {model.FileName} from {model.Domain}";
			DownloadBarSubText.Text = model.StatusText;
			DownloadBarSubText.Visibility = Visibility.Visible;
			BtnDownloadPause.Visibility = Visibility.Visible;
			BtnDownloadPause.Content = (model.IsPaused ? "Resume" : "Pause");
			BtnDownloadCancel.Visibility = Visibility.Visible;
			BtnDownloadRun.Visibility = Visibility.Collapsed;
			BtnDownloadView.Visibility = Visibility.Collapsed;
		}));
	}

	private void OnDownloadUpdated(DownloadItemModel model)
	{
		((DispatcherObject)this).Dispatcher.InvokeAsync((Action)(() =>
		{
			if (_currentDownloadingModel?.Id == model.Id)
			{
				DownloadBarTitleText.Text = $"{model.PercentComplete}% of {model.FileName} from {model.Domain}";
				DownloadBarSubText.Text = model.StatusText;
				BtnDownloadPause.Content = (model.IsPaused ? "Resume" : "Pause");
			}
		}));
	}

	private void OnDownloadFinished(DownloadItemModel model)
	{
		((DispatcherObject)this).Dispatcher.InvokeAsync((Action)(() =>
		{
			_currentDownloadingModel = model;
			DownloadBar.Visibility = Visibility.Visible;
			DownloadBarTitleText.Text = model.FileName + " finished downloading.";
			DownloadBarSubText.Visibility = Visibility.Collapsed;
			BtnDownloadPause.Visibility = Visibility.Collapsed;
			BtnDownloadCancel.Visibility = Visibility.Collapsed;
			BtnDownloadRun.Visibility = Visibility.Visible;
			BtnDownloadView.Visibility = Visibility.Visible;
		}));
	}

	private void OnDownloadCancelled(DownloadItemModel model)
	{
		((DispatcherObject)this).Dispatcher.InvokeAsync((Action)(() =>
		{
			if (_currentDownloadingModel?.Id == model.Id)
			{
				DownloadBar.Visibility = Visibility.Collapsed;
				_currentDownloadingModel = null;
			}
		}));
	}

	private void BtnDownloadPause_Click(object sender, RoutedEventArgs e)
	{
		if (_currentDownloadingModel != null)
		{
			if (_currentDownloadingModel.IsPaused)
			{
				DownloadsService.ResumeDownload(_currentDownloadingModel.Id);
				BtnDownloadPause.Content = "Pause";
			}
			else
			{
				DownloadsService.PauseDownload(_currentDownloadingModel.Id);
				BtnDownloadPause.Content = "Resume";
			}
		}
	}

	private void BtnDownloadCancel_Click(object sender, RoutedEventArgs e)
	{
		if (_currentDownloadingModel != null)
		{
			DownloadsService.CancelDownload(_currentDownloadingModel.Id);
			DownloadBar.Visibility = Visibility.Collapsed;
			_currentDownloadingModel = null;
		}
	}

	private void BtnDownloadRun_Click(object sender, RoutedEventArgs e)
	{
		if (_currentDownloadingModel != null)
		{
			DownloadsService.OpenFile(_currentDownloadingModel.FullPath);
		}
	}

	private void BtnDownloadView_Click(object sender, RoutedEventArgs e)
	{
		OpenHub();
		TabHubDownloads.IsChecked = true;
		HubTab_Click(sender, e);
	}

	private void BtnCloseDownloadBar_Click(object sender, RoutedEventArgs e)
	{
		DownloadBar.Visibility = Visibility.Collapsed;
	}

	private void Settings_Click(object sender, RoutedEventArgs e)
	{
		if (SettingsPane.Visibility == Visibility.Visible)
		{
			CloseSettings();
		}
		else
		{
			OpenSettings();
		}
	}

	private void OpenSettings()
	{
		CloseHub();
		_isSettingsPinned = false;
		UpdateSettingsPinUI();
		Settings_Loaded(this, null!);
		SettingsPane.Visibility = Visibility.Visible;
		DoubleAnimation animation = new DoubleAnimation(330.0, 0.0, new Duration(TimeSpan.FromMilliseconds(350L)))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			},
			FillBehavior = FillBehavior.HoldEnd
		};
		SettingsPaneTranslate.BeginAnimation(TranslateTransform.XProperty, animation);
	}

	private void CloseSettings()
	{
		_isSettingsPinned = false;
		UpdateSettingsPinUI();
		if (SettingsPane.Visibility == Visibility.Visible)
		{
			DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, 330.0, new Duration(TimeSpan.FromMilliseconds(260L)))
			{
				EasingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseIn
				},
				FillBehavior = FillBehavior.HoldEnd
			};
			doubleAnimation.Completed += (object? s, EventArgs e) =>
			{
				SettingsPane.Visibility = Visibility.Collapsed;
				SettingsPaneTranslate.X = 330.0;
				SettingsPaneTranslate.BeginAnimation(TranslateTransform.XProperty, null);
			};
			SettingsPaneTranslate.BeginAnimation(TranslateTransform.XProperty, doubleAnimation);
		}
	}

	private void PinSettings_Click(object sender, RoutedEventArgs e)
	{
		_isSettingsPinned = !_isSettingsPinned;
		UpdateSettingsPinUI();
	}

	private void UpdateSettingsPinUI()
	{
		if (_isSettingsPinned)
		{
			PinSettingsIcon.Foreground = (Brush)FindResource("AccentBrush");
			BtnPinSettings.ToolTip = "Unpin pane";
			SettingsShadow.Opacity = 0.0;
			Grid.SetColumn(SettingsPane, 1);
			Grid.SetColumnSpan(SettingsPane, 1);
			SettingsPane.HorizontalAlignment = HorizontalAlignment.Stretch;
			SidePaneColumn.Width = GridLength.Auto;
		}
		else
		{
			PinSettingsIcon.Foreground = new SolidColorBrush(Color.FromRgb(85, 85, 85));
			BtnPinSettings.ToolTip = "Pin pane";
			SettingsShadow.Opacity = 0.2;
			Grid.SetColumn(SettingsPane, 0);
			Grid.SetColumnSpan(SettingsPane, 2);
			SettingsPane.HorizontalAlignment = HorizontalAlignment.Right;
			SidePaneColumn.Width = new GridLength(0.0);
		}
	}

	private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (_isAddressBarActiveOnStartPage && !IsMouseOverElement(AddressBarContainer, e))
		{
			DeactivateAddressBarOnStartPage();
		}
		if (!_isHubPinned && HubPane.Visibility == Visibility.Visible && !IsMouseOverElement(HubPane, e) && !IsMouseOverElement(BtnHub, e))
		{
			CloseHub();
		}
		if (!_isSettingsPinned && SettingsPane.Visibility == Visibility.Visible && !IsMouseOverElement(SettingsPane, e) && !IsMouseOverElement(BtnMore, e))
		{
			CloseSettings();
		}
		if (MoreMenuContainer.Visibility == Visibility.Visible && !IsMouseOverElement(MoreMenuContainer, e) && !IsMouseOverElement(BtnMore, e))
		{
			CloseMoreMenuAnimated();
		}
	}

	private static bool IsMouseOverElement(FrameworkElement element, MouseButtonEventArgs e)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		if (element == null || !element.IsVisible)
		{
			return false;
		}
		Point position = e.GetPosition(element);
		return position.X >= 0.0 && position.X <= element.ActualWidth && position.Y >= 0.0 && position.Y <= element.ActualHeight;
	}

	private void BtnMore_Click(object sender, RoutedEventArgs e)
	{
		if (MoreMenuContainer.Visibility == Visibility.Visible)
		{
			CloseMoreMenuAnimated();
			return;
		}
		CloseHub();
		CloseSettings();
		OpenMoreMenuAnimated();
	}

	private void OpenMoreMenuAnimated()
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		MoreMenuBorder.Measure(new Size(320.0, double.PositiveInfinity));
		Size desiredSize = MoreMenuBorder.DesiredSize;
		double num = desiredSize.Height;
		if (num <= 0.0)
		{
			num = 375.0;
		}
		MoreMenuContainer.Height = 0.0;
		MoreMenuContainer.Visibility = Visibility.Visible;
		BtnMore.Background = new SolidColorBrush(Color.FromRgb(229, 229, 229));
		DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, num, new Duration(TimeSpan.FromMilliseconds(320L)))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			},
			FillBehavior = FillBehavior.Stop
		};
		doubleAnimation.Completed += (object? s, EventArgs e) =>
		{
			MoreMenuContainer.Height = double.NaN;
		};
		MoreMenuContainer.BeginAnimation(FrameworkElement.HeightProperty, doubleAnimation);
	}

	private void CloseMoreMenuAnimated()
	{
		if (MoreMenuContainer.Visibility != Visibility.Visible) return;

		double currentHeight = MoreMenuContainer.ActualHeight;
		if (currentHeight <= 0) currentHeight = MoreMenuContainer.Height;
		if (double.IsNaN(currentHeight) || currentHeight <= 0) currentHeight = 375.0;

		var anim = new DoubleAnimation(currentHeight, 0.0, new Duration(TimeSpan.FromMilliseconds(200)))
		{
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
			FillBehavior = FillBehavior.Stop,
		};
		anim.Completed += (s, _) =>
		{
			MoreMenuContainer.BeginAnimation(FrameworkElement.HeightProperty, null);
			MoreMenuContainer.Height = 0.0;
			MoreMenuContainer.Visibility = Visibility.Collapsed;
			BtnMore.Background = Brushes.Transparent;
		};
		MoreMenuContainer.BeginAnimation(FrameworkElement.HeightProperty, anim);
	}

	private void OnMoreMenuItemClick(object sender, RoutedEventArgs e)
	{
		CloseMoreMenuAnimated();
		if (sender is FrameworkElement { Tag: string tag })
		{
			switch (tag)
			{
			case "NewWindow":
				NewWindow_Click(sender, e);
				break;
			case "NewInPrivate":
				NewInPrivate_Click(sender, e);
				break;
			case "FindOnPage":
				FindOnPage_Click(sender, e);
				break;
			case "Print":
				Print_Click(sender, e);
				break;
			case "PinToStart":
				PinToStart_Click(sender, e);
				break;
			case "DevTools":
				DevTools_Click(sender, e);
				break;
			case "Feedback":
				Feedback_Click(sender, e);
				break;
			case "Settings":
				Settings_Click(sender, e);
				break;
			}
		}
	}

	private void NewWindow_Click(object sender, RoutedEventArgs e)
	{
		new MainWindow().Show();
	}

	private void NewInPrivate_Click(object sender, RoutedEventArgs e)
	{
		MainWindow mainWindow = new MainWindow();
		mainWindow.Title = "InPrivate - Zenith";
		mainWindow.Show();
	}

	private void ZoomIn_Click(object sender, RoutedEventArgs e)
	{
		ChromiumWebBrowser activeWebBrowser = GetActiveWebBrowser();
		if (activeWebBrowser != null)
		{
			_currentZoom = Math.Min(3.0, _currentZoom + 0.25);
			activeWebBrowser.ZoomLevel = (_currentZoom - 1.0) * 4.0;
			ZoomText.Text = $"{(int)(_currentZoom * 100.0)}%";
		}
	}

	private void ZoomOut_Click(object sender, RoutedEventArgs e)
	{
		ChromiumWebBrowser activeWebBrowser = GetActiveWebBrowser();
		if (activeWebBrowser != null)
		{
			_currentZoom = Math.Max(0.5, _currentZoom - 0.25);
			activeWebBrowser.ZoomLevel = (_currentZoom - 1.0) * 4.0;
			ZoomText.Text = $"{(int)(_currentZoom * 100.0)}%";
		}
	}

	private void Print_Click(object sender, RoutedEventArgs e)
	{
		if (ViewModel.SelectedTab != null && _browsers.TryGetValue(ViewModel.SelectedTab.Id, out ChromiumWebBrowser value))
		{
			value.Print();
		}
	}

	private void FindOnPage_Click(object sender, RoutedEventArgs e)
	{
		FindDialog findDialog = new FindDialog
		{
			Owner = this
		};
		if (findDialog.ShowDialog() == true && !string.IsNullOrEmpty(findDialog.SearchText) && ViewModel.SelectedTab != null && _browsers.TryGetValue(ViewModel.SelectedTab.Id, out ChromiumWebBrowser value))
		{
			value.GetBrowser()?.Find(findDialog.SearchText, forward: true, matchCase: false, findNext: false);
		}
	}

	private void PinToStart_Click(object sender, RoutedEventArgs e)
	{
		MessageBox.Show("Page pinned to Start.", "Pin to Start", MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	private void DevTools_Click(object sender, RoutedEventArgs e)
	{
		GetActiveWebBrowser()?.ShowDevTools();
	}

	private void Feedback_Click(object sender, RoutedEventArgs e)
	{
		MessageBox.Show("Thank you for testing Zenith!", "Feedback", MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	private void BtnWebNote_Click(object sender, RoutedEventArgs e)
	{
		MessageBox.Show("Web Notes (Make a Web Note)\nDraw, highlight, and write notes on webpages.", "Web Notes", MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	private void BtnShare_Click(object sender, RoutedEventArgs e)
	{
		MessageBox.Show("Share this page with your contacts and apps.", "Share", MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	private void Window_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
		{
			BtnFavorite_Click(sender, e);
			e.Handled = true;
		}
		else if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control)
		{
			NewTab_Click(sender, e);
			e.Handled = true;
		}
		else if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
		{
			if (ViewModel.SelectedTab != null && ViewModel.Tabs.Count > 1)
			{
				CloseTabWithAnimation(ViewModel.SelectedTab);
			}
			e.Handled = true;
		}
		else if ((e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) || (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Alt))
		{
			AddressBarContainer.Visibility = Visibility.Visible;
			AddressBox.Focus();
			AddressBox.SelectAll();
			e.Handled = true;
		}
		else if (e.Key == Key.F5 || (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control))
		{
			BtnRefresh_Click(sender, e);
			e.Handled = true;
		}
		else if (e.Key == Key.Escape)
		{
			if (_isAddressBarActiveOnStartPage)
			{
				DeactivateAddressBarOnStartPage();
				e.Handled = true;
				return;
			}
			if (MoreMenuContainer.Visibility == Visibility.Visible)
			{
				CloseMoreMenuAnimated();
				e.Handled = true;
				return;
			}
			if (SecurityPopup.IsOpen)
			{
				SecurityPopup.IsOpen = false;
				e.Handled = true;
				return;
			}
			if (!_isHubPinned && HubPane.Visibility == Visibility.Visible)
			{
				CloseHub();
				e.Handled = true;
				return;
			}
			if (!_isSettingsPinned && SettingsPane.Visibility == Visibility.Visible)
			{
				CloseSettings();
				e.Handled = true;
				return;
			}
			if (ViewModel.SelectedTab?.IsLoading == true)
			{
				GetActiveWebBrowser()?.GetBrowser()?.StopLoad();
				e.Handled = true;
			}
		}
		else if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt)
		{
			BtnBack_Click(sender, e);
			e.Handled = true;
		}
		else if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.Alt)
		{
			BtnForward_Click(sender, e);
			e.Handled = true;
		}
		else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
		{
			FindOnPage_Click(sender, e);
			e.Handled = true;
		}
		else if (e.Key == Key.F12)
		{
			DevTools_Click(sender, e);
			e.Handled = true;
		}
		else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
		{
			OpenHub();
			TabHubHistory.IsChecked = true;
			HubTab_Click(sender, e);
			e.Handled = true;
		}
		else if (e.Key == Key.J && Keyboard.Modifiers == ModifierKeys.Control)
		{
			OpenHub();
			TabHubDownloads.IsChecked = true;
			HubTab_Click(sender, e);
			e.Handled = true;
		}
		else if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.Control)
		{
			CycleTab(forward: true);
			e.Handled = true;
		}
		else if (e.Key == Key.Tab && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
		{
			CycleTab(forward: false);
			e.Handled = true;
		}
	}

	private void CycleTab(bool forward)
	{
		if (ViewModel.Tabs.Count >= 2 && ViewModel.SelectedTab != null)
		{
			int num = ViewModel.Tabs.IndexOf(ViewModel.SelectedTab);
			int index = (forward ? ((num + 1) % ViewModel.Tabs.Count) : ((num - 1 + ViewModel.Tabs.Count) % ViewModel.Tabs.Count));
			ViewModel.SelectTab(ViewModel.Tabs[index]);
		}
	}

	protected override void OnClosed(EventArgs e)
	{
		HistoryService.Save();
		foreach (ChromiumWebBrowser value in _browsers.Values)
		{
			value.Dispose();
		}
		_browsers.Clear();
		base.OnClosed(e);
	}
}

internal sealed class ActionCommand<T> : ICommand
{
	private readonly Action<T> _execute;

	private readonly Func<T, bool>? _canExecute;

	public event EventHandler? CanExecuteChanged
	{
		add
		{
			CommandManager.RequerySuggested += value;
		}
		remove
		{
			CommandManager.RequerySuggested -= value;
		}
	}

	public ActionCommand(Action<T> execute, Func<T, bool>? canExecute = null)
	{
		_execute = execute;
		_canExecute = canExecute;
	}

	public bool CanExecute(object? parameter)
	{
		return !(parameter is T arg) || (_canExecute?.Invoke(arg) ?? true);
	}

	public void Execute(object? parameter)
	{
		if (parameter is T obj)
		{
			_execute(obj);
		}
	}
}

