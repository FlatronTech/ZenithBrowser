using System.IO;
using System.Windows;
using CefSharp;
using CefSharp.Wpf;
using Zenith.App.Handlers;
using Zenith.App.Services;

namespace Zenith.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        SettingsService.Load();
        LangService.Load(SettingsService.Language);
        ZenithAdBlockService.Initialize();
        InitializeCef();
        base.OnStartup(e);
        new MainWindow().Show();
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
        Cef.Shutdown();
        base.OnExit(e);
    }
}
