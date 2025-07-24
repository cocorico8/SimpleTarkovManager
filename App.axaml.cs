using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System.Net;
using System.Net.Http;
using SimpleTarkovManager.Services;
using SimpleTarkovManager.ViewModels;
using SimpleTarkovManager.Views;

namespace SimpleTarkovManager
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        // --- THIS IS THE FINAL, STABLE DEPENDENCY INJECTION SETUP ---

        // 1. Create all services at startup.
        var hardwareService = new HardwareService();
        var baseHttpHandler = new HttpClientHandler { CookieContainer = new CookieContainer() };
        HttpMessageHandler finalHttpHandler;
#if DEBUG
        System.Console.WriteLine("--- LAUNCHER STARTED IN DEBUG MODE (Using Simple Downloader) ---");
        finalHttpHandler = new DebuggingHttpHandler(baseHttpHandler);
#else
        finalHttpHandler = baseHttpHandler;
#endif
        var authService = new AuthService(hardwareService, finalHttpHandler);
        var eftApiService = new EftApiService(authService, finalHttpHandler);
        
        // Use our simple, reliable downloader as the implementation for the interface.
        IDownloadService downloadService = new DownloadService();

        var compressionService = new CompressionService();
        var registryService = new RegistryService();
        var patchingService = new PatchingService(compressionService);
        var updateManagerService = new UpdateManagerService(eftApiService);
        var gameRepairService = new GameRepairService(downloadService);
        var appManager = new AppManager(authService, eftApiService);
        var dialogService = new DialogService();
        
        var mainWindow = new MainWindow();
        var launchArgs = desktop.Args?.ToArray() ?? Array.Empty<string>();
        
        // 2. Create the MainWindowViewModel, passing it all the required services.
        var mainWindowViewModel = new MainWindowViewModel(
            authService,
            appManager,
            dialogService,
            eftApiService,
            registryService,
            downloadService,
            compressionService,
            gameRepairService,
            updateManagerService,
            patchingService,
            launchArgs
        );
        
        mainWindow.DataContext = mainWindowViewModel;
        desktop.MainWindow = mainWindow;
    }
    base.OnFrameworkInitializationCompleted();
}
    }
}