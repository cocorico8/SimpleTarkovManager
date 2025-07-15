using System;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SimpleTarkovManager.Services;

namespace SimpleTarkovManager.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly AuthService _authService;
        public event Action<bool>? OnViewChanged;
        
        partial void OnCurrentViewModelChanged(ViewModelBase? value)
        {
            OnViewChanged?.Invoke(value is MainViewModel);
        }

        [ObservableProperty]
        private ViewModelBase _currentViewModel;
        

        public MainWindowViewModel(IServiceProvider serviceProvider, AuthService authService)
        {
            _serviceProvider = serviceProvider;
            _authService = authService;
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
            // Ask the DI container for a new LoginViewModel
            var loginVm = _serviceProvider.GetRequiredService<LoginViewModel>();
            loginVm.OnLoginSuccess += ShowMainView;
            CurrentViewModel = loginVm;
        }

        private void ShowMainView()
        {
            // Ask the DI container for a new MainViewModel
            var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
            mainVm.OnLogout += ShowLoginView;
            CurrentViewModel = mainVm;
        }
    }
}