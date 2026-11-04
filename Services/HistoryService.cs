using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using Zenith.App.Models;

namespace Zenith.App.Services;

public static class HistoryService
{
    private static readonly ObservableCollection<HistoryItem> s_items = new();
    private static readonly string s_historyDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Zenith");
    private static readonly string s_historyPath = Path.Combine(s_historyDir, "history.json");

    public static ObservableCollection<HistoryItem> Items => s_items;

    public static event Action? HistoryChanged;

    static HistoryService()
    {
        Load();
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(s_historyPath)) return;
            string json = File.ReadAllText(s_historyPath);
            var entries = JsonSerializer.Deserialize<List<HistoryEntryDto>>(json);
            if (entries == null) return;

            s_items.Clear();
            foreach (var e in entries)
            {
                s_items.Add(new HistoryItem
                {
                    Title = e.Title,
                    Url = e.Url,
                    Timestamp = e.Timestamp
                });
            }
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            if (!Directory.Exists(s_historyDir))
                Directory.CreateDirectory(s_historyDir);

            var dtos = s_items.Select(i => new HistoryEntryDto
            {
                Title = i.Title,
                Url = i.Url,
                Timestamp = i.Timestamp
            }).ToList();

            string json = JsonSerializer.Serialize(dtos, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(s_historyPath, json);
        }
        catch { }
    }

    public static void AddEntry(string title, string url, ImageSource? favicon = null)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            url.StartsWith("zenith://", StringComparison.OrdinalIgnoreCase) ||
            url.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            return;

        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            // Remove previous duplicate if visited recently
            var existing = s_items.FirstOrDefault(i => i.Url.Equals(url, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                s_items.Remove(existing);
            }

            s_items.Insert(0, new HistoryItem
            {
                Title = string.IsNullOrWhiteSpace(title) ? url : title,
                Url = url,
                Timestamp = DateTime.Now,
                Favicon = favicon
            });

            // Keep up to 500 items
            while (s_items.Count > 500)
            {
                s_items.RemoveAt(s_items.Count - 1);
            }

            Save();
            HistoryChanged?.Invoke();
        });
    }

    public static void ClearAll()
    {
        s_items.Clear();
        Save();
        HistoryChanged?.Invoke();
    }
}

public class HistoryEntryDto
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
