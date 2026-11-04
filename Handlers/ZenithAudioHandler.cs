
using System;
using CefSharp;
using CefSharp.Structs;
using Zenith.App.Models;

namespace Zenith.App.Handlers;

public class ZenithAudioHandler : IAudioHandler
{
    private readonly BrowserTab _tab;

    public ZenithAudioHandler(BrowserTab tab)
    {
        _tab = tab;
    }

    public bool GetAudioParameters(IWebBrowser chromiumWebBrowser, IBrowser browser, ref AudioParameters parameters)
    {
        return false;
    }

    public void OnAudioStreamStarted(IWebBrowser chromiumWebBrowser, IBrowser browser, AudioParameters parameters, int channels)
    {
        _tab.IsPlayingAudio = true;
    }

    public void OnAudioStreamPacket(IWebBrowser chromiumWebBrowser, IBrowser browser, IntPtr data, int noOfFrames, long pts)
    {
    }

    public void OnAudioStreamStopped(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
        _tab.IsPlayingAudio = false;
    }

    public void OnAudioStreamError(IWebBrowser chromiumWebBrowser, IBrowser browser, string errorMessage)
    {
        _tab.IsPlayingAudio = false;
    }

    public void Dispose()
    {
    }
}
