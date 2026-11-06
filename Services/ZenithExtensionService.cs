using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zenith.App.Services;

/// <summary>
/// Manages built-in browser "extensions" (feature toggles).
/// Currently managing Zenith AdBlocker.
/// State is persisted in %AppData%\Zenith\extensions.json.
/// </summary>
public static class ZenithExtensionService
{
    // ─── Events ──────────────────────────────────────────────────────────────
    public static event Action? ExtensionsChanged;

    // ─── State ───────────────────────────────────────────────────────────────
    private static bool _adBlockerEnabled = true;

    public static bool AdBlockerEnabled
    {
        get => _adBlockerEnabled;
        set
        {
            _adBlockerEnabled = value;
            ZenithAdBlockService.IsEnabled = value;
            Save();
            ExtensionsChanged?.Invoke();
        }
    }

    // ─── Persistence ─────────────────────────────────────────────────────────
    private static readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Zenith", "extensions.json");

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var dto = JsonSerializer.Deserialize<ExtDto>(File.ReadAllText(_filePath), _jsonOpts);
            if (dto == null) return;

            _adBlockerEnabled = dto.AdBlocker;

            // Sync adblock service
            ZenithAdBlockService.IsEnabled = _adBlockerEnabled;
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var dto = new ExtDto
            {
                AdBlocker = _adBlockerEnabled,
            };
            File.WriteAllText(_filePath, JsonSerializer.Serialize(dto, _jsonOpts));
        }
        catch { }
    }

    private sealed class ExtDto
    {
        [JsonPropertyName("adBlocker")] public bool AdBlocker { get; set; } = true;
    }
}
