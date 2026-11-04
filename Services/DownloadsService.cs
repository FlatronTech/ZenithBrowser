using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CefSharp;
using Zenith.App.Models;

namespace Zenith.App.Services;

public static class DownloadsService
{
    private static readonly Dictionary<int, IDownloadItemCallback> s_callbacks = new();

    public static ObservableCollection<DownloadItemModel> ActiveDownloads { get; } = new();
    public static ObservableCollection<DownloadItemModel> PastDownloads { get; } = new();

    public static event Action<DownloadItemModel>? DownloadStarted;
    public static event Action<DownloadItemModel>? DownloadUpdated;
    public static event Action<DownloadItemModel>? DownloadFinished;
    public static event Action<DownloadItemModel>? DownloadCancelled;

    public static string DefaultDownloadsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public static void RegisterCallback(int id, IDownloadItemCallback callback)
    {
        s_callbacks[id] = callback;
    }

    public static void PauseDownload(int id)
    {
        if (s_callbacks.TryGetValue(id, out var cb) && !cb.IsDisposed)
        {
            cb.Pause();
        }
        var item = ActiveDownloads.FirstOrDefault(d => d.Id == id);
        if (item != null)
        {
            item.IsPaused = true;
            DownloadUpdated?.Invoke(item);
        }
    }

    public static void ResumeDownload(int id)
    {
        if (s_callbacks.TryGetValue(id, out var cb) && !cb.IsDisposed)
        {
            cb.Resume();
        }
        var item = ActiveDownloads.FirstOrDefault(d => d.Id == id);
        if (item != null)
        {
            item.IsPaused = false;
            DownloadUpdated?.Invoke(item);
        }
    }

    public static void CancelDownload(int id)
    {
        if (s_callbacks.TryGetValue(id, out var cb) && !cb.IsDisposed)
        {
            cb.Cancel();
        }
        s_callbacks.Remove(id);

        var item = ActiveDownloads.FirstOrDefault(d => d.Id == id);
        if (item != null)
        {
            item.IsCancelled = true;
            ActiveDownloads.Remove(item);
            PastDownloads.Insert(0, item);
            DownloadCancelled?.Invoke(item);
        }
    }

    public static void OpenFile(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return;
        if (!File.Exists(fullPath))
        {
            MessageBox.Show($"File does not exist:\n{fullPath}", "Open file", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open file: {ex.Message}", "Open file", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void RemovePastDownload(DownloadItemModel item)
    {
        PastDownloads.Remove(item);
    }

    public static void OpenDownloadsFolder()
    {
        try
        {
            var folder = DefaultDownloadsFolder;
            if (Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
            }
        }
        catch { }
    }

    public static void ClearPastDownloads()
    {
        PastDownloads.Clear();
    }

    public static void NotifyStarted(DownloadItemModel model) => DownloadStarted?.Invoke(model);
    public static void NotifyUpdated(DownloadItemModel model) => DownloadUpdated?.Invoke(model);
    public static void NotifyFinished(DownloadItemModel model) => DownloadFinished?.Invoke(model);
    public static void NotifyCancelled(DownloadItemModel model) => DownloadCancelled?.Invoke(model);
}
