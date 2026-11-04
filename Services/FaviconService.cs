using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace Zenith.App.Services;

/// <summary>
/// Asynchronously downloads and caches favicons for browser tabs.
/// </summary>
public static class FaviconService
{
    private static readonly HttpClient s_httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static readonly ConcurrentDictionary<string, BitmapImage?> s_cache = new();

    public static async Task<BitmapImage?> GetFaviconAsync(string pageUrl, IList<string>? candidates = null)
    {
        if (string.IsNullOrWhiteSpace(pageUrl)) return null;

        if (pageUrl.StartsWith("zenith://", StringComparison.OrdinalIgnoreCase) ||
            pageUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            return null; // Uses the default Zenith / Globe icon
        }

        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri))
            return null;

        string domain = uri.Host.ToLowerInvariant();
        if (s_cache.TryGetValue(domain, out var cached))
            return cached;

        // 1. Try candidate URLs from CEF if provided
        if (candidates != null && candidates.Count > 0)
        {
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                var img = await DownloadImageAsync(candidate);
                if (img != null)
                {
                    s_cache[domain] = img;
                    return img;
                }
            }
        }

        // 2. Try direct /favicon.ico
        if (uri.Scheme == "http" || uri.Scheme == "https")
        {
            string directUrl = $"{uri.Scheme}://{uri.Host}/favicon.ico";
            var img = await DownloadImageAsync(directUrl);
            if (img != null)
            {
                s_cache[domain] = img;
                return img;
            }

            // 3. Fallback to Google S2 favicon service (reliable across all popular sites)
            string googleFaviconUrl = $"https://www.google.com/s2/favicons?domain={uri.Host}&sz=32";
            var googleImg = await DownloadImageAsync(googleFaviconUrl);
            if (googleImg != null)
            {
                s_cache[domain] = googleImg;
                return googleImg;
            }
        }

        s_cache[domain] = null;
        return null;
    }

    private static async Task<BitmapImage?> DownloadImageAsync(string url)
    {
        try
        {
            using var response = await s_httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes == null || bytes.Length == 0) return null;

            var bmp = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
