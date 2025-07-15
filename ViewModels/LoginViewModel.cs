using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimpleTarkovManager.Services;

namespace SimpleTarkovManager.ViewModels
{
    public partial class LoginViewModel : ViewModelBase
    {
        private readonly AuthService _authService;

        [ObservableProperty]
        private string _email;

        [ObservableProperty]
        private string _password;

        [ObservableProperty]
        private string _statusMessage;

        [ObservableProperty]
        private bool _isBusy;

        // This event will be raised on successful login to notify the main window.
        public event Action OnLoginSuccess;

        public LoginViewModel(AuthService authService)
        {
            _authService = authService;
        }

        [RelayCommand]
        private async Task LoginAsync()
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                StatusMessage = "Email and password cannot be empty.";
                return;
            }

            IsBusy = true;
            StatusMessage = "Logging in...";

            try
            {
                var (success, errorMessage) = await _authService.LoginAsync(Email, Password);
                if (success)
                {
                    StatusMessage = "Login successful!";
                    OnLoginSuccess?.Invoke();
                }
                else
                {
                    StatusMessage = $"Login failed: {errorMessage}";
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}