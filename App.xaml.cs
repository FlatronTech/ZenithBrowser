using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;
using CefSharp;
using CefSharp.Wpf;
using Zenith.App.Handlers;
using Zenith.App.Services;

namespace Zenith.App;

public partial class App : Application
{
    private const string MutexName = "ZenithBrowser_SingleInstance_Mutex";
    private const string PipeName  = "ZenithBrowser_SingleInstance_Pipe";

    private static Mutex? _mutex;
    private static Thread? _pipeServer;
    private static bool _isFirstInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // ── Single instance check ─────────────────────────────────────
        _mutex = new Mutex(true, MutexName, out _isFirstInstance);

        if (!_isFirstInstance)
        {
            // Another instance is running — tell it to come to foreground
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(1000); // 1s timeout
                using var writer = new StreamWriter(client);
                writer.Write("BRING_TO_FRONT");
                writer.Flush();
            }
            catch { /* pipe not available yet, ignore */ }

            // Exit this instance
            _mutex.Dispose();
            Shutdown();
            return;
        }

        // ── Start pipe server to listen for activation requests ───────
        StartPipeServer();

        // ── Normal startup ────────────────────────────────────────────
        SettingsService.Load();
        LangService.Load(SettingsService.Language);
        ZenithAdBlockService.Initialize();
        ZenithExtensionService.Load();
        InitializeCef();
        base.OnStartup(e);
        new MainWindow().Show();
    }

    private void StartPipeServer()
    {
        _pipeServer = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    string? msg = reader.ReadToEnd();

                    if (msg?.Contains("BRING_TO_FRONT") == true)
                    {
                        // Marshal to UI thread
                        Dispatcher.InvokeAsync(() =>
                        {
                            var win = MainWindow;
                            if (win != null)
                            {
                                if (win.WindowState == WindowState.Minimized)
                                    win.WindowState = WindowState.Normal;
                                win.Activate();
                                win.Focus();
                                win.BringIntoView();
                                // Force to front
                                win.Topmost = true;
                                win.Topmost = false;
                            }
                        });
                    }
                }
                catch { /* server error, restart loop */ }
            }
        })
        {
            IsBackground = true,
            Name = "ZenithPipeServer"
        };
        _pipeServer.Start();
    }

    private static void InitializeCef()
    {
        var settings = new CefSettings
        {
            CachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Zenith", "Cache"),
            LogFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Zenith", "cef.log"),
            LogSeverity = LogSeverity.Warning,
            Locale = "en-US",
            UserAgent = $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{Cef.ChromiumVersion} Safari/537.36 Zenith/1.0",
        };

        var schemeFactory = new ZenithSchemeHandlerFactory();

        settings.RegisterScheme(new CefCustomScheme
        {
            SchemeName = "zenith",
            SchemeHandlerFactory = schemeFactory,
            IsStandard = true,
            IsSecure = true,
            IsCorsEnabled = true
        });

        Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null);
        Cef.GetGlobalRequestContext().RegisterSchemeHandlerFactory("zenith", string.Empty, schemeFactory);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SettingsService.Save();
        if (_isFirstInstance && _mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch { }
            _mutex.Dispose();
        }
        Cef.Shutdown();
        base.OnExit(e);
    }
}
