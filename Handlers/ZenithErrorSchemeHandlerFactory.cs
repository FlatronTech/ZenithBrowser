using System;
using System.IO;
using System.Text;
using CefSharp;

namespace Zenith.App.Handlers;

/// <summary>
/// Scheme handler for "zenith-error://" and assets requested by Edge 1507 ErrorPages.
/// Also serves "zenith://error?..." if requested.
/// </summary>
public class ZenithErrorSchemeHandlerFactory : ISchemeHandlerFactory
{
    public const string SchemeName = "zenith-error";

    public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
    {
        try
        {
            var uri = new Uri(request.Url);
            string path = uri.AbsolutePath.TrimStart('/');

            string baseDir = Path.Combine(AppContext.BaseDirectory, "Things");

            // Check if asking for font
            if (path.EndsWith("BrowserMDL.ttf", StringComparison.OrdinalIgnoreCase))
            {
                string fontPath = Path.Combine(baseDir, "BrowserMDL.ttf");
                if (File.Exists(fontPath))
                {
                    return ResourceHandler.FromFilePath(fontPath, mimeType: "font/ttf");
                }
            }

            // Normal error pages directory
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

            // Fallback to dnserror.html if file not found
            string fallbackPath = Path.Combine(errorPagesDir, "dnserror.html");
            if (File.Exists(fallbackPath))
            {
                return ResourceHandler.FromFilePath(fallbackPath, mimeType: "text/html");
            }
        }
        catch { }

        return ResourceHandler.FromString("<html><body>Error loading error page</body></html>", Encoding.UTF8, mimeType: "text/html");
    }
}
