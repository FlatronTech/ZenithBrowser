using System;
using CefSharp;
using Zenith.App.Models;

namespace Zenith.App.Handlers;

/// <summary>
/// Intercepts popups (target="_blank", window.open) to open in a new tab instead of a separate OS window,
/// and handles window.close() requests by closing the corresponding tab.
/// </summary>
public class ZenithLifeSpanHandler : ILifeSpanHandler
{
    private readonly BrowserTab _tab;
    private readonly Action<string> _onOpenNewTab;
    private readonly Action<BrowserTab> _onCloseTab;

    public ZenithLifeSpanHandler(BrowserTab tab, Action<string> onOpenNewTab, Action<BrowserTab> onCloseTab)
    {
        _tab = tab;
        _onOpenNewTab = onOpenNewTab;
        _onCloseTab = onCloseTab;
    }

    public bool OnBeforePopup(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        string targetUrl,
        string targetFrameName,
        WindowOpenDisposition targetDisposition,
        bool userGesture,
        IPopupFeatures popupFeatures,
        IWindowInfo windowInfo,
        IBrowserSettings browserSettings,
        ref bool noJavascriptAccess,
        out IWebBrowser newBrowser)
    {
        newBrowser = null!;

        if (!string.IsNullOrEmpty(targetUrl))
        {
            _onOpenNewTab?.Invoke(targetUrl);
        }

        return true; // Cancel default popup window creation
    }

    public void OnAfterCreated(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
    }

    public bool DoClose(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
        _onCloseTab?.Invoke(_tab);
        return false;
    }

    public void OnBeforeClose(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
    }
}
