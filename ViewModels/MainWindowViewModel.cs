using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading.Tasks;
using SimpleTarkovManager.Services;

namespace SimpleTarkovManager.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        // We hold all the services that need to be passed down
        private readonly AuthService _authService;
        private readonly DialogService _dialogService;
        private readonly RegistryService _registryService;
        private readonly EftApiService _eftApiService;
        private readonly DownloadService _downloadService;
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
            DialogService dialogService,
            RegistryService registryService,
            EftApiService eftApiService,
            DownloadService downloadService,
            CompressionService compressionService,
            GameRepairService gameRepairService,
            UpdateManagerService updateManagerService,
            PatchingService patchingService,
            string[] launchArgs)
        {
            _authService = authService;
            _dialogService = dialogService;
            _registryService = registryService;
            _eftApiService = eftApiService;
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
            bool loggedIn = await _authService.LoginWithRefreshTokenAsync();
            if (loggedIn)
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
            if (CurrentViewModel is MainViewModel oldMainVm)
            {
                oldMainVm.OnLogout -= ShowMainView;
                oldMainVm.Cleanup();
            }

            var loginVm = new LoginViewModel(_authService);
            loginVm.OnLoginSuccess += ShowMainView;
            CurrentViewModel = loginVm;
        }

        private void ShowMainView()
        {
            if (CurrentViewModel is LoginViewModel oldLoginVm)
            {
                oldLoginVm.OnLoginSuccess -= ShowMainView;
            }

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
                _launchArgs
            );
            mainVm.OnLogout += ShowLoginView;
            CurrentViewModel = mainVm;
        }
    }
}