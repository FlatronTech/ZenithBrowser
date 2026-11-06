using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using Zenith.App.Models;

namespace Zenith.App.Services;

public static class FavoritesService
{
    private static readonly ObservableCollection<FavoriteItem> s_items = new();
    private static readonly string s_favoritesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Zenith");
    private static readonly string s_favoritesPath = Path.Combine(s_favoritesDir, "favorites.json");

    public static ObservableCollection<FavoriteItem> Items => s_items;

    public static event Action? FavoritesChanged;

    static FavoritesService()
    {
        Load();
    }

    public static bool IsFavorite(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        string normalized = NormalizeUrl(url);
        return s_items.Any(f => NormalizeUrl(f.Url) == normalized);
    }

    public static bool ToggleFavorite(string title, string url, ImageSource? favicon = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        string normalized = NormalizeUrl(url);
        var existing = s_items.FirstOrDefault(f => NormalizeUrl(f.Url) == normalized);
        if (existing != null)
        {
            s_items.Remove(existing);
            Save();
            FavoritesChanged?.Invoke();
            return false; // Removed
        }
        else
        {
            s_items.Insert(0, new FavoriteItem
            {
                Title = string.IsNullOrWhiteSpace(title) ? url : title,
                Url = url,
                DateAdded = DateTime.Now,
                Favicon = favicon
            });
            Save();
            FavoritesChanged?.Invoke();
            return true; // Added
        }
    }

    public static void AddFavorite(string title, string url, ImageSource? favicon = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        string normalized = NormalizeUrl(url);
        if (s_items.Any(f => NormalizeUrl(f.Url) == normalized)) return;

        s_items.Insert(0, new FavoriteItem
        {
            Title = string.IsNullOrWhiteSpace(title) ? url : title,
            Url = url,
            DateAdded = DateTime.Now,
            Favicon = favicon
        });
        Save();
        FavoritesChanged?.Invoke();
    }

    public static void RemoveFavorite(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        string normalized = NormalizeUrl(url);
        var item = s_items.FirstOrDefault(f => NormalizeUrl(f.Url) == normalized);
        if (item != null)
        {
            s_items.Remove(item);
            Save();
            FavoritesChanged?.Invoke();
        }
    }

    private static string NormalizeUrl(string url)
    {
        url = url.Trim();
        if (url.EndsWith("/")) url = url.Substring(0, url.Length - 1);
        return url.ToLowerInvariant();
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(s_favoritesPath)) return;
            string json = File.ReadAllText(s_favoritesPath);
            var entries = JsonSerializer.Deserialize<List<FavoriteEntryDto>>(json);
            if (entries == null) return;

            s_items.Clear();
            foreach (var e in entries)
            {
                s_items.Add(new FavoriteItem
                {
                    Title = e.Title,
                    Url = e.Url,
                    DateAdded = e.DateAdded
                });
            }
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            if (!Directory.Exists(s_favoritesDir))
                Directory.CreateDirectory(s_favoritesDir);

            var dtos = s_items.Select(i => new FavoriteEntryDto
            {
                Title = i.Title,
                Url = i.Url,
                DateAdded = i.DateAdded
            }).ToList();

            string json = JsonSerializer.Serialize(dtos, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(s_favoritesPath, json);
        }
        catch { }
    }

    private class FavoriteEntryDto
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; }
    }
}
