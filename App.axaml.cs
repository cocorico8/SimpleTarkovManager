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
                // --- MANUAL DEPENDENCY INJECTION WITH CONDITIONAL DEBUGGING ---

                // 1. Create the base handler that is always used.
                var baseHttpHandler = new HttpClientHandler { CookieContainer = new CookieContainer() };

                // 2. Declare a variable for the final handler we will inject.
                HttpMessageHandler finalHttpHandler;

#if DEBUG
                // 3. In DEBUG mode, we wrap the base handler with our logger.
                //    This entire block will be completely ignored and compiled out in a Release build.
                System.Console.WriteLine("--- LAUNCHER STARTED IN DEBUG MODE ---");
                finalHttpHandler = new DebuggingHttpHandler(baseHttpHandler);
#else
                // 4. In RELEASE mode, we just use the normal, non-logging handler.
                finalHttpHandler = baseHttpHandler;
#endif

                // 5. Create all services, injecting the 'finalHttpHandler'.
                var hardwareService = new HardwareService();
                var authService = new AuthService(hardwareService, finalHttpHandler);
                var eftApiService = new EftApiService(authService, finalHttpHandler);
                var downloadService = new DownloadService(finalHttpHandler);
                var compressionService = new CompressionService();
                var registryService = new RegistryService();
                var patchingService = new PatchingService(compressionService);
                var updateManagerService = new UpdateManagerService(eftApiService);
                var gameRepairService = new GameRepairService(downloadService, updateManagerService, compressionService);
                
                var mainWindow = new MainWindow();
                var dialogService = new DialogService();

                var launchArgs = desktop.Args?.ToArray() ?? Array.Empty<string>();
                var mainWindowViewModel = new MainWindowViewModel(
                    authService,
                    dialogService,
                    registryService,
                    eftApiService,
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