using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zenith.App.Services;

/// <summary>
/// Persistent browser settings — saved as JSON in %AppData%\Zenith\settings.json.
/// </summary>
public static class SettingsService
{
    // ─── Storage path ────────────────────────────────────────────────────────
    private static readonly string _settingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zenith");

    private static readonly string _settingsPath = Path.Combine(_settingsDir, "settings.json");

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    // ─── Properties (defaults match Edge 1507 behaviour) ─────────────────────

    /// <summary>Homepage URL loaded when "Open with: A specific page" is chosen.</summary>
    public static string Homepage { get; set; } = "zenith://newtab";

    /// <summary>What to open on startup: "StartPage" | "NewTab" | "PreviousPages" | "SpecificPage"</summary>
    public static string OpenWith { get; set; } = "NewTab";

    /// <summary>Default search engine: "Google" | "Bing" | "DuckDuckGo"</summary>
    public static string SearchEngine { get; set; } = "DuckDuckGo";

    /// <summary>Whether the Favorites bar is visible.</summary>
    public static bool ShowFavoritesBar { get; set; } = false;

    /// <summary>UI language code (e.g. "en" or "pl").</summary>
    public static string Language { get; set; } = "en";

    /// <summary>Whether built-in ad blocker is enabled.</summary>
    public static bool AdBlockEnabled { get; set; } = true;

    // ─── Persistence ─────────────────────────────────────────────────────────

    public static void Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;

            string json = File.ReadAllText(_settingsPath);
            var dto = JsonSerializer.Deserialize<SettingsDto>(json, _jsonOpts);
            if (dto == null) return;

            Homepage       = dto.Homepage       ?? Homepage;
            OpenWith       = dto.OpenWith        ?? OpenWith;
            SearchEngine   = dto.SearchEngine    ?? SearchEngine;
            ShowFavoritesBar = dto.ShowFavoritesBar;
            Language       = dto.Language       ?? Language;
            AdBlockEnabled = dto.AdBlockEnabled;
            ZenithAdBlockService.IsEnabled = AdBlockEnabled;
        }
        catch
        {
            // If settings file is corrupt, just use defaults.
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(_settingsDir);
            var dto = new SettingsDto
            {
                Homepage        = Homepage,
                OpenWith        = OpenWith,
                SearchEngine    = SearchEngine,
                ShowFavoritesBar = ShowFavoritesBar,
                Language        = Language,
                AdBlockEnabled  = AdBlockEnabled,
            };
            string json = JsonSerializer.Serialize(dto, _jsonOpts);
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Non-fatal — settings not saved.
        }
    }

    // ─── Search URL builder ───────────────────────────────────────────────────

    public static string BuildSearchUrl(string query)
    {
        string encoded = Uri.EscapeDataString(query);
        return SearchEngine switch
        {
            "Bing"       => "https://www.bing.com/search?q=" + encoded,
            "DuckDuckGo" => "https://duckduckgo.com/?q=" + encoded,
            _            => "https://www.google.com/search?q=" + encoded,
        };
    }

    // ─── Private DTO ─────────────────────────────────────────────────────────

    private sealed class SettingsDto
    {
        [JsonPropertyName("homepage")]        public string? Homepage        { get; set; }
        [JsonPropertyName("openWith")]        public string? OpenWith        { get; set; }
        [JsonPropertyName("searchEngine")]    public string? SearchEngine    { get; set; }
        [JsonPropertyName("showFavoritesBar")] public bool  ShowFavoritesBar { get; set; }
        [JsonPropertyName("language")]        public string? Language        { get; set; }
        [JsonPropertyName("adBlockEnabled")]  public bool    AdBlockEnabled  { get; set; } = true;
    }
}
