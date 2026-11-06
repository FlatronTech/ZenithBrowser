using System;
using System.IO;
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

            // Handle favicon requests
            if (uri.AbsolutePath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                return ResourceHandler.FromString("", Encoding.UTF8, mimeType: "image/x-icon");
            }

            // Handle error pages requested via zenith://error/...
            if (string.Equals(uri.Host, "error", StringComparison.OrdinalIgnoreCase))
            {
                string path = uri.AbsolutePath.TrimStart('/');
                if (string.IsNullOrEmpty(path))
                {
                    path = "dnserror.html";
                }

                string baseDir = Path.Combine(AppContext.BaseDirectory, "Things");

                if (path.EndsWith("BrowserMDL.ttf", StringComparison.OrdinalIgnoreCase))
                {
                    string fontPath = Path.Combine(baseDir, "BrowserMDL.ttf");
                    if (File.Exists(fontPath))
                    {
                        return ResourceHandler.FromFilePath(fontPath, mimeType: "font/ttf");
                    }
                }

                string errorPagesDir = Path.Combine(baseDir, "ErrorPages");
                string filePath = Path.Combine(errorPagesDir, path);

                if (File.Exists(filePath))
                {
                    string ext = Path.GetExtension(filePath).ToLowerInvariant();
                    string mime = ext switch
                    {
                        ".htm" or ".html" => "text/html",
                        ".css" => "text/css",
                        ".js" => "application/javascript",
                        ".svg" => "image/svg+xml",
                        ".ttf" => "font/ttf",
                        _ => "application/octet-stream"
                    };

                    return ResourceHandler.FromFilePath(filePath, mimeType: mime);
                }
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
