using System.Threading.Tasks;
using SimpleTarkovManager.Models;

namespace SimpleTarkovManager.Services
{
    /// <summary>
    /// Manages the application's startup sequence and holds shared application state.
    /// </summary>
    public class AppManager
    {
        private readonly AuthService _authService;
        private readonly EftApiService _eftApiService;

        public LauncherConfig? LauncherConfig { get; private set; }
        public string? StartupError { get; private set; }

        public AppManager(AuthService authService, EftApiService eftApiService)
        {
            _authService = authService;
            _eftApiService = eftApiService;
        }

        /// <summary>
        /// Executes the full, sequential startup logic for the application.
        /// </summary>
        public async Task InitializeAsync()
        {
            // Step 1: Ensure we are logged in and our tokens are fresh.
            bool isLoggedIn = await _authService.LoginWithRefreshTokenAsync();
            if (!isLoggedIn)
            {
                // This is not an error. It just means we need to show the login screen.
                return;
            }

            // Step 2: Now that tokens are guaranteed to be fresh, get the launcher config.
            var (config, error) = await _eftApiService.GetLauncherConfigAsync();
            if (config == null)
            {
                StartupError = $"Failed to load launcher configuration: {error}";
                return;
            }

            LauncherConfig = config;
        }
    }
}