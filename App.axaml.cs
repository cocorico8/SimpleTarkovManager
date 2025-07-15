using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http;
using SimpleEFTLauncher.Services;
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
            var services = new ServiceCollection();
            ConfigureServices(services);
            var serviceProvider = services.BuildServiceProvider();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    // Get the fully constructed ViewModel from our container.
                    DataContext = serviceProvider.GetRequiredService<MainWindowViewModel>()
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void ConfigureServices(IServiceCollection services)
        {
#if DEBUG
            // This entire block will be compiled out in a Release build.
            var baseHttpHandler = new HttpClientHandler { CookieContainer = new CookieContainer() };
            var debuggingHttpHandler = new DebuggingHttpHandler(baseHttpHandler);
            services.AddSingleton<HttpMessageHandler>(debuggingHttpHandler);
#else
            // In a Release build, we use a simple handler with a cookie container.
            services.AddSingleton<HttpMessageHandler>(new HttpClientHandler { CookieContainer = new CookieContainer() });
#endif

            // Register all our services as Singletons (one instance for the app's lifetime).
            services.AddSingleton<HardwareService>();
            services.AddSingleton<AuthService>();
            services.AddSingleton<EftApiService>();
            services.AddSingleton<DownloadService>();
            services.AddSingleton<CompressionService>();
            services.AddSingleton<GameRepairService>();
            services.AddSingleton<RegistryService>();
            services.AddSingleton<PatchingService>();
            services.AddSingleton<UpdateManagerService>();
            services.AddSingleton<DialogService>();

            // Register ViewModels. Transient means a new one is created every time it's requested.
            services.AddTransient<MainWindowViewModel>();
            services.AddTransient<LoginViewModel>();
            services.AddTransient<MainViewModel>();
        }
    }
}