using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Linq;
using System.Threading.Tasks;
using SimpleTarkovManager.Services;
using SimpleTarkovManager.Models;

namespace SimpleTarkovManager.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        // These are all the services the application might need, held here to be
        // passed down to the MainViewModel when it's created.
        private readonly AuthService _authService;
        private readonly AppManager _appManager;
        private readonly DialogService _dialogService;
        private readonly EftApiService _eftApiService;
        private readonly RegistryService _registryService;
        private readonly IDownloadService _downloadService;
        private readonly CompressionService _compressionService;
        private readonly GameRepairService _gameRepairService;
        private readonly UpdateManagerService _updateManagerService;
        private readonly PatchingService _patchingService;
        private readonly string[] _launchArgs;
        
        [ObservableProperty]
        private ViewModelBase _currentViewModel;

        public event Action<bool>? OnViewChanged;

        partial void OnCurrentViewModelChanged(ViewModelBase? value)
        {
            OnViewChanged?.Invoke(value is MainViewModel);
        }

        public MainWindowViewModel(
            AuthService authService,
            AppManager appManager,
            DialogService dialogService,
            EftApiService eftApiService,
            RegistryService registryService,
            IDownloadService downloadService,
            CompressionService compressionService,
            GameRepairService gameRepairService,
            UpdateManagerService updateManagerService,
            PatchingService patchingService,
            string[] launchArgs)
        {
            _authService = authService;
            _appManager = appManager;
            _dialogService = dialogService;
            _eftApiService = eftApiService;
            _registryService = registryService;
            _downloadService = downloadService;
            _compressionService = compressionService;
            _gameRepairService = gameRepairService;
            _updateManagerService = updateManagerService;
            _patchingService = patchingService;
            _launchArgs = launchArgs;
            
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            // Run the AppManager to check login state and fetch the config.
            await _appManager.InitializeAsync();

            // Now, based on the result, show the correct view.
            if (_appManager.LauncherConfig != null)
            {
                ShowMainView();
            }
            else
            {
                ShowLoginView();
            }
        }

        private void ShowLoginView()
        {
            var loginVm = new LoginViewModel(_authService);
            loginVm.OnLoginSuccess += () =>
            {
                // When login succeeds, we must re-run the initialization to get the config.
                _ = InitializeAsync();
            };
            CurrentViewModel = loginVm;
        }

        private void ShowMainView()
        {
            // The AppManager now holds the config, which is guaranteed to be non-null here.
            var config = _appManager.LauncherConfig;
            
            // Create the MainViewModel, passing it the full, final set of services it needs to do its job.
            var mainVm = new MainViewModel(
                _dialogService,
                _registryService,
                _authService,
                _eftApiService,
                _downloadService,
                _compressionService,
                _gameRepairService,
                _updateManagerService,
                _patchingService,
                config,
                _launchArgs
            );
            mainVm.OnLogout += ShowLoginView;
            CurrentViewModel = mainVm;
        }
    }
}