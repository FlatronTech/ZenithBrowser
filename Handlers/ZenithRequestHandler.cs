using CefSharp;
using CefSharp.Handler;
using Zenith.App.Services;

namespace Zenith.App.Handlers;

/// <summary>
/// CefSharp request handler — intercepts network requests for ad blocking.
/// </summary>
public class ZenithRequestHandler : RequestHandler
{
    protected override IResourceRequestHandler? GetResourceRequestHandler(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IRequest request,
        bool isNavigation,
        bool isDownload,
        string requestInitiator,
        ref bool disableDefaultHandling)
    {
        if (ZenithAdBlockService.ShouldBlock(request.Url))
            return new BlockingResourceHandler();

        return null; // default handling
    }
}

/// <summary>
/// Returns an empty 200 response for blocked requests (silent block).
/// </summary>
internal class BlockingResourceHandler : ResourceRequestHandler
{
    protected override CefReturnValue OnBeforeResourceLoad(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IRequest request,
        IRequestCallback callback)
    {
        callback.Dispose();
        return CefReturnValue.Cancel;
    }
}
