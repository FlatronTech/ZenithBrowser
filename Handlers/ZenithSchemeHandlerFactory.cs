using System.Text;
using CefSharp;
using Zenith.App.Services;

namespace Zenith.App.Handlers;

public class ZenithSchemeHandlerFactory : ISchemeHandlerFactory
{
    public const string SchemeName = "zenith";
    public const string HostName = "newtab";

    public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
    {
        try
        {
            var uri = new Uri(request.Url);
            if (uri.AbsolutePath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                return ResourceHandler.FromString("", Encoding.UTF8, mimeType: "image/x-icon");
            }

            var html = StartPageService.GetStartPageHtml();
            return ResourceHandler.FromString(html, Encoding.UTF8, mimeType: "text/html");
        }
        catch
        {
            var html = StartPageService.GetStartPageHtml();
            return ResourceHandler.FromString(html, Encoding.UTF8, mimeType: "text/html");
        }
    }
}
