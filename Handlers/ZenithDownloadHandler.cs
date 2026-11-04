using System.IO;
using CefSharp;
using Zenith.App.Models;
using Zenith.App.Services;

namespace Zenith.App.Handlers;

public class ZenithDownloadHandler : IDownloadHandler
{
    public bool CanDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, string url, string requestMethod) => true;

    public bool OnBeforeDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem, IBeforeDownloadCallback callback)
    {
        if (callback.IsDisposed) return false;

        string targetFolder = DownloadsService.DefaultDownloadsFolder;
        if (!Directory.Exists(targetFolder))
        {
            Directory.CreateDirectory(targetFolder);
        }

        string fullPath = Path.Combine(targetFolder, downloadItem.SuggestedFileName);

        var model = new DownloadItemModel
        {
            Id = downloadItem.Id,
            FileName = downloadItem.SuggestedFileName,
            FullPath = fullPath,
            Url = downloadItem.Url,
            Domain = GetDomain(downloadItem.Url),
            TotalBytes = downloadItem.TotalBytes,
            ReceivedBytes = downloadItem.ReceivedBytes,
            PercentComplete = downloadItem.PercentComplete
        };

        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            DownloadsService.ActiveDownloads.Insert(0, model);
            DownloadsService.NotifyStarted(model);
        });

        callback.Continue(fullPath, showDialog: false);
        return true;
    }

    public void OnDownloadUpdated(IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem, IDownloadItemCallback callback)
    {
        if (!callback.IsDisposed)
        {
            DownloadsService.RegisterCallback(downloadItem.Id, callback);
        }

        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            var model = DownloadsService.ActiveDownloads.FirstOrDefault(d => d.Id == downloadItem.Id);
            if (model == null) return;

            model.ReceivedBytes = downloadItem.ReceivedBytes;
            model.TotalBytes = downloadItem.TotalBytes;
            model.PercentComplete = downloadItem.PercentComplete;
            model.IsPaused = downloadItem.IsPaused;

            if (downloadItem.IsCancelled)
            {
                model.IsCancelled = true;
                DownloadsService.ActiveDownloads.Remove(model);
                DownloadsService.PastDownloads.Insert(0, model);
                DownloadsService.NotifyCancelled(model);
            }
            else if (downloadItem.IsComplete)
            {
                model.IsComplete = true;
                DownloadsService.ActiveDownloads.Remove(model);
                DownloadsService.PastDownloads.Insert(0, model);
                DownloadsService.NotifyFinished(model);
            }
            else
            {
                DownloadsService.NotifyUpdated(model);
            }
        });
    }

    private static string GetDomain(string url)
    {
        try
        {
            return new Uri(url).Host;
        }
        catch
        {
            return url;
        }
    }
}
