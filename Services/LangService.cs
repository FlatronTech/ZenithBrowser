using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Zenith.App.Services;

/// <summary>
/// Modular language pack system. Language packs are JSON files in the /lang/ folder
/// next to the executable. Falls back to English for any missing key.
/// </summary>
public static class LangService
{
    // ─── Event fired when language changes ────────────────────────────────────
    public static event Action? LanguageChanged;

    // ─── State ───────────────────────────────────────────────────────────────
    private static Dictionary<string, string> _current  = new();
    private static Dictionary<string, string> _fallback = new();

    public static string CurrentCode { get; private set; } = "en";

    // ─── Lang folder path (next to exe, or in project dir for dev) ───────────
    private static string LangDir =>
        Path.Combine(AppContext.BaseDirectory, "lang");

    // ─── Available languages ─────────────────────────────────────────────────
    public static List<(string Code, string Name)> AvailableLanguages()
    {
        var list = new List<(string, string)>();
        if (!Directory.Exists(LangDir)) return list;

        foreach (var file in Directory.GetFiles(LangDir, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file, System.Text.Encoding.UTF8);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict == null) continue;
                dict.TryGetValue("lang.code", out string? code);
                dict.TryGetValue("lang.name", out string? name);
                if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(name))
                    list.Add((code, name));
            }
            catch { }
        }
        return list;
    }

    // ─── Load ─────────────────────────────────────────────────────────────────
    public static void Load(string langCode)
    {
        // Load fallback (English) first
        _fallback = LoadFile("en") ?? new Dictionary<string, string>();

        var target = LoadFile(langCode);
        if (target != null)
        {
            _current  = target;
            CurrentCode = langCode;
        }
        else
        {
            _current  = _fallback;
            CurrentCode = "en";
        }
    }

    private static Dictionary<string, string>? LoadFile(string code)
    {
        string path = Path.Combine(LangDir, code + ".json");
        if (!File.Exists(path)) return null;
        try
        {
            string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch { return null; }
    }

    // ─── Translate ────────────────────────────────────────────────────────────
    public static string T(string key)
    {
        if (_current.TryGetValue(key, out string? val)) return val;
        if (_fallback.TryGetValue(key, out string? fb)) return fb;
        return key; // return key itself as last resort
    }

    // ─── Hot-reload without restart ──────────────────────────────────────────
    public static void Switch(string langCode)
    {
        if (langCode == CurrentCode) return;
        Load(langCode);
        SettingsService.Language = langCode;
        SettingsService.Save();
        LanguageChanged?.Invoke();
    }
}
