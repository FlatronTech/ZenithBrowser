using System.Windows;
using CefSharp;
using CefSharp.Enums;
using CefSharp.Structs;
using Zenith.App.Models;
using Zenith.App.Services;

namespace Zenith.App.Handlers;

/// <summary>
/// Custom display handler to capture Favicon URL changes and other display events.
/// </summary>
public class ZenithDisplayHandler : IDisplayHandler
{
    private readonly BrowserTab _tab;

    public ZenithDisplayHandler(BrowserTab tab)
    {
        _tab = tab;
    }

    public void OnFaviconUrlChange(IWebBrowser chromiumWebBrowser, IBrowser browser, IList<string> iconUrls)
    {
        if (iconUrls != null && iconUrls.Count > 0)
        {
            _ = App.Current.Dispatcher.InvokeAsync(async () =>
            {
                var icon = await FaviconService.GetFaviconAsync(_tab.Url, iconUrls);
                if (icon != null)
                {
                    _tab.Favicon = icon;
                }
            });
        }
    }

    public void OnAddressChanged(IWebBrowser chromiumWebBrowser, AddressChangedEventArgs addressChangedArgs) { }

    public bool OnAutoResize(IWebBrowser chromiumWebBrowser, IBrowser browser, CefSharp.Structs.Size newSize) => false;

    public bool OnCursorChange(IWebBrowser chromiumWebBrowser, IBrowser browser, nint cursor, CursorType type, CursorInfo customCursorInfo) => false;

    public void OnTitleChanged(IWebBrowser chromiumWebBrowser, TitleChangedEventArgs titleChangedArgs) { }

    public void OnFullscreenModeChange(IWebBrowser chromiumWebBrowser, IBrowser browser, bool fullscreen)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (Application.Current.MainWindow is not MainWindow win) return;
            win.HandleFullscreen(fullscreen);
        });
    }

    public void OnLoadingProgressChange(IWebBrowser chromiumWebBrowser, IBrowser browser, double progress) { }

    public bool OnTooltipChanged(IWebBrowser chromiumWebBrowser, ref string text) => false;

    public void OnStatusMessage(IWebBrowser chromiumWebBrowser, StatusMessageEventArgs statusMessageArgs) { }

    public bool OnConsoleMessage(IWebBrowser chromiumWebBrowser, ConsoleMessageEventArgs consoleMessageArgs) => false;
}
