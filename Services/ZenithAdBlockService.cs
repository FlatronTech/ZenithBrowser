using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Zenith.App.Services;

/// <summary>
/// Built-in ad and tracker blocker.
/// Uses a static seed list + optional remote EasyList filter.
/// </summary>
public static class ZenithAdBlockService
{
    // ─── State ──────────────────────────────────────────────────────────────
    public static bool IsEnabled { get; set; } = true;
    private static int _blockedCount = 0;
    public static int BlockedCount => _blockedCount;
    public static event Action? BlockedCountChanged;

    // ─── Filter storage ─────────────────────────────────────────────────────
    private static readonly HashSet<string> _blockedDomains = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<Regex> _blockedPatterns   = new();
    private static readonly object _lock = new();

    // ─── Settings path ───────────────────────────────────────────────────────
    private static string FilterPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Zenith", "adblock_filters.txt");

    // ─── Seed list (common ad/tracking domains) ───────────────────────────────
    private static readonly string[] _seedDomains =
    {
        // Google Ads
        "googleadservices.com", "googlesyndication.com", "doubleclick.net",
        "google-analytics.com", "googletagmanager.com", "googletagservices.com",
        "adservice.google.com", "pagead2.googlesyndication.com",
        "ad.doubleclick.net",
        "static.doubleclick.net",
        "googleads.g.doubleclick.net",
        "imasdk.googleapis.com",     // YouTube IMA SDK (reklamy)
        "www.youtube.com/api/stats/ads",  // (obsłużone przez URL pattern)
        // Facebook/Meta tracking
        "connect.facebook.net", "graph.facebook.com", "analytics.facebook.com",
        "pixel.facebook.com",
        // Ad networks
        "ads.yahoo.com", "advertising.com", "adnxs.com",
        "adsrvr.org", "adtechus.com", "adtech.de",
        "openx.net", "rubiconproject.com", "pubmatic.com",
        "smartadserver.com", "criteo.com", "criteo.net",
        "quantserve.com", "scorecardresearch.com",
        "outbrain.com", "taboola.com", "revcontent.com",
        "media.net", "propellerads.com", "popads.net",
        "trafficjunky.net", "trafficholder.com",
        "bidswitch.net", "bidswitch.com",
        "casalemedia.com", "contextweb.com",
        "appnexus.com", "liveintent.com",
        "sharethrough.com", "triplelift.com",
        "sovrn.com", "lijit.com", "improvedigital.com",
        "indexworm.com", "indexexchange.com",
        // Trackers
        "mixpanel.com", "amplitude.com", "segment.io", "segment.com",
        "hotjar.com", "mouseflow.com", "fullstory.com", "logrocket.com",
        "heap.io", "heapanalytics.com",
        "optimizely.com", "vwo.com", "convert.com",
        "newrelic.com", "nr-data.net",
        "rollbar.com", "sentry.io",
        "chartbeat.com", "parsely.com",
        "addthis.com", "sharethis.com",
        "disqus.com",
        // Click fraud / malvertising
        "ads.exoclick.com", "exoclick.com",
        "juicyads.com", "clicksor.com",
        "adf.ly", "adfoc.us",
        // Social trackers
        "platform.twitter.com", "syndication.twitter.com",
        "cdn.syndication.twimg.com",
        "widgets.pinterest.com", "log.pinterest.com",
        // Misc
        "stats.wp.com", "pixel.wp.com",
        "amazon-adsystem.com", "assoc-amazon.com",
        "serving-sys.com", "bluekai.com",
        "demdex.net", "omtrdc.net",
        "mopub.com", "flurry.com",
        "moatads.com", "moat.com",
        "idsync.rlcdn.com", "rlcdn.com",
    };

    // ─── Init ────────────────────────────────────────────────────────────────
    public static void Initialize()
    {
        lock (_lock)
        {
            // Add seed domains
            foreach (var d in _seedDomains)
                _blockedDomains.Add(d);

            // Load cached filter file
            if (File.Exists(FilterPath))
                LoadFilterFile(FilterPath);
        }
    }

    // ─── Check ───────────────────────────────────────────────────────────────
    public static bool ShouldBlock(string url)
    {
        if (!IsEnabled) return false;
        if (string.IsNullOrEmpty(url)) return false;

        // YouTube-specific: block ad-related URL patterns
        if (url.Contains("youtube.com") || url.Contains("googlevideo.com"))
        {
            return ShouldBlockYouTubeRequest(url);
        }

        try
        {
            var uri = new Uri(url);
            string host = uri.Host.ToLowerInvariant();

            // Remove www. prefix for matching
            string bare = host.StartsWith("www.") ? host[4..] : host;

            lock (_lock)
            {
                // Exact match
                if (_blockedDomains.Contains(host) || _blockedDomains.Contains(bare))
                {
                    Interlocked.Increment(ref _blockedCount);
                    BlockedCountChanged?.Invoke();
                    return true;
                }

                // Subdomain match — check if host ends with .blockeddomain
                foreach (var blocked in _blockedDomains)
                {
                    if (host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase))
                    {
                        Interlocked.Increment(ref _blockedCount);
                        BlockedCountChanged?.Invoke();
                        return true;
                    }
                }

                // Pattern match
                foreach (var pattern in _blockedPatterns)
                {
                    if (pattern.IsMatch(url))
                    {
                        Interlocked.Increment(ref _blockedCount);
                        BlockedCountChanged?.Invoke();
                        return true;
                    }
                }
            }
        }
        catch { /* invalid URL — don't block */ }

        return false;
    }


    private static bool ShouldBlockYouTubeRequest(string url)
    {
        if (!IsEnabled) return false;

        // Konkretne ścieżki/parametry związane z reklamami YT
        var ytAdPatterns = new[]
        {
            "/api/stats/ads",
            "/ptracking",
            "/pagead/",
            "/youtubei/v1/log_event",
            "/get_video_info",
            "ad_simple_compiled",
            "/api/stats/qoe?adformat=",
        };

        foreach (var pattern in ytAdPatterns)
        {
            if (url.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _blockedCount);
                BlockedCountChanged?.Invoke();
                return true;
            }
        }

        // googlevideo.com – blokuj tylko strumienie reklamowe (ctier=A lub oad=)
        if (url.Contains("googlevideo.com", StringComparison.OrdinalIgnoreCase))
        {
            if (url.Contains("ctier=A") || url.Contains("&oad=") || url.Contains("oad=1"))
            {
                Interlocked.Increment(ref _blockedCount);
                BlockedCountChanged?.Invoke();
                return true;
            }
        }

        return false;
    }

    // ─── Load EasyList-style filter file ─────────────────────────────────────
    private static void LoadFilterFile(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                string l = line.Trim();
                if (l.StartsWith("!") || l.StartsWith("[") || string.IsNullOrEmpty(l))
                    continue;

                // Domain rule: ||domain.com^
                if (l.StartsWith("||") && l.EndsWith("^"))
                {
                    string domain = l[2..^1];
                    if (!domain.Contains('/') && !domain.Contains('*'))
                        _blockedDomains.Add(domain);
                }
                // Plain domain
                else if (!l.Contains('/') && !l.Contains('*') && l.Contains('.') && !l.StartsWith("@@"))
                {
                    _blockedDomains.Add(l.TrimStart('|'));
                }
            }
        }
        catch { }
    }

    // ─── Remote update (downloads EasyList) ─────────────────────────────────
    public static async Task UpdateFiltersAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            // Use EasyList mirror
            string content = await http.GetStringAsync(
                "https://easylist.to/easylist/easylist.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(FilterPath)!);
            await File.WriteAllTextAsync(FilterPath, content);

            lock (_lock)
            {
                // Reload
                LoadFilterFile(FilterPath);
            }
        }
        catch { /* silent fail */ }
    }

    public static void Reset() => _blockedCount = 0;
}
