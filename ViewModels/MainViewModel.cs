using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimpleEFTLauncher.Services;
using SimpleTarkovManager.Models;
using SimpleTarkovManager.Services;

namespace SimpleTarkovManager.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        #region Services
        private readonly DialogService _dialogService;
        private readonly RegistryService _registryService;
        private readonly AuthService _authService;
        private readonly EftApiService _eftApiService;
        private readonly DownloadService _downloadService;
        private readonly CompressionService _compressionService;
        private readonly GameRepairService _gameRepairService;
        private readonly UpdateManagerService _updateManagerService;
        private readonly PatchingService _patchingService;
        #endregion

        #region Properties
        [ObservableProperty] private LauncherConfig? _launcherConfig;
        [ObservableProperty] private GameInstallInfo? _installInfo;
        [ObservableProperty] private string _gameDirectory;
        [ObservableProperty] private string _gameStatusText = "Initializing...";
        [ObservableProperty] private string _statusMessage = "Ready.";
        [ObservableProperty] private bool _isActionRequired;
        [ObservableProperty] private string _actionButtonText = "DOWNLOAD & INSTALL";

        // State Flags
        [ObservableProperty] private bool _isCheckingStatus;
        [ObservableProperty] private bool _isDownloadingAndInstalling;
        [ObservableProperty] private bool _isRepairing;
        [ObservableProperty] private bool _isCancelling;
        public bool IsBusy => IsCheckingStatus || IsDownloadingAndInstalling || IsRepairing;

        // Progress Properties
        [ObservableProperty] private double _downloadPercentage;
        [ObservableProperty] private string _downloadProgressText = "0%";
        [ObservableProperty] private string _downloadSpeed = "0 MB/s";
        [ObservableProperty] private string _downloadEtaText = "ETA: --";
        [ObservableProperty] private double _extractionPercentage;
        [ObservableProperty] private string _extractionProgressText = "0%";
        [ObservableProperty] private string _extractionEtaText = "ETA: --";
        [ObservableProperty] private double _repairPercentage;
        [ObservableProperty] private string _repairProgressText = "0%";
        [ObservableProperty] private string _repairEtaText = "ETA: --";
        #endregion

        private CancellationTokenSource? _pathCheckCts;
        private CancellationTokenSource? _cancellationTokenSource;
        public event Action OnLogout;

        public MainViewModel(DialogService dialogService, RegistryService registryService, AuthService authService, EftApiService eftApiService, DownloadService downloadService, CompressionService compressionService, GameRepairService gameRepairService, UpdateManagerService updateManagerService, PatchingService patchingService)
        {
            _dialogService = dialogService;
            _registryService = registryService;
            _authService = authService;
            _eftApiService = eftApiService;
            _downloadService = downloadService;
            _compressionService = compressionService;
            _gameRepairService = gameRepairService;
            _updateManagerService = updateManagerService;
            _patchingService = patchingService;

            var initialGamePath = LoadGameDirectoryFromRegistry();
            _ = InitializeAsync(initialGamePath);
        }

        #region Commands
        [RelayCommand(CanExecute = nameof(IsNotBusy))]
        private async Task ManualCheckForUpdateAsync()
        {
            await CheckGameStatusAsync(GameDirectory);
        }

        [RelayCommand(CanExecute = nameof(IsNotBusy))]
        private async Task BrowseForGameFolderAsync()
        {
            var selectedPath = await _dialogService.OpenFolderAsync();
            if (!string.IsNullOrEmpty(selectedPath))
            {
                GameDirectory = selectedPath;
            }
        }

        [RelayCommand(CanExecute = nameof(IsNotBusy))]
        private async Task RegisterGameAsync()
        {
            var (installedVersion, status) = await _gameRepairService.GetInstalledVersionAsync(GameDirectory);
            if (installedVersion == null)
            {
                StatusMessage = "Cannot register an invalid installation. Please install or repair the game first.";
                return;
            }

            if (IsRunningAsAdmin())
            {
                try
                {
                    StatusMessage = "Applying registry key...";
                    _registryService.WriteOfficialRegistryKey(GameDirectory, installedVersion.Value);
                    StatusMessage = "Game successfully registered in Windows!";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Failed to register game: {ex.Message}";
                }
            }
            else
            {
                StatusMessage = "Admin rights required. Restarting launcher for permission...";
                await Task.Delay(1500);
                try
                {
                    var exeName = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exeName))
                    {
                        StatusMessage = "Error: Could not determine launcher path for restart.";
                        return;
                    }
                    var startInfo = new ProcessStartInfo(exeName) { Verb = "runas" };
                    Process.Start(startInfo);
                    (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    StatusMessage = "Admin elevation was cancelled by the user.";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Failed to restart as admin: {ex.Message}";
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanCancel))]
        private void CancelCurrentOperation()
        {
            if (_cancellationTokenSource != null)
            {
                IsCancelling = true;
                StatusMessage = "Cancelling, please wait...";
                _cancellationTokenSource.Cancel();
            }
        }

        [RelayCommand(CanExecute = nameof(IsNotBusy))]
        private async Task StartUpdateOrInstallAsync()
        {
            var (installedVersion, _) = await _gameRepairService.GetInstalledVersionAsync(GameDirectory);
            if (installedVersion == null)
            {
                await PerformFullInstallAsync();
            }
            else
            {
                await PerformDifferentialUpdateAsync(installedVersion.Value);
            }
        }

        [RelayCommand(CanExecute = nameof(IsNotBusy))]
        private async Task RepairGameAsync()
        {
            if (string.IsNullOrEmpty(GameDirectory) || !Directory.Exists(GameDirectory))
            {
                StatusMessage = "Game directory is invalid.";
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            IsRepairing = true;
            try
            {
                var (installedVersion, status) = await _gameRepairService.GetInstalledVersionAsync(GameDirectory);
                bool manifestIsCorrupt = installedVersion == null && (status.Contains("corrupt") || status.Contains("unknown"));

                if (!File.Exists(Path.Combine(GameDirectory, "ConsistencyInfo")) || manifestIsCorrupt)
                {
                    StatusMessage = "Manifest is missing or corrupt. Downloading a fresh copy...";
                    if (LauncherConfig == null) { StatusMessage = "Cannot repair without config."; return; }
                    var (installInfo, errorMsg) = await _eftApiService.GetGameInstallInfoAsync();
                    if (installInfo == null) { StatusMessage = $"Cannot get install info: {errorMsg}"; return; }
                    
                    var tempZipPath = Path.Combine(Path.GetTempPath(), "eft_client_repair.zip");
                    Action<DownloadProgress> downloadProgressAction = p =>
                    {
                        Dispatcher.UIThread.Invoke(() =>
                        {
                            RepairPercentage = p.Percentage;
                            RepairProgressText = $"{p.Percentage:F0}%";
                            StatusMessage = $"Downloading manifest... ({p.SpeedBytesPerSecond / 1024 / 1024:F2} MB/s)";
                        });
                    };
                    await _downloadService.DownloadFileAsync(LauncherConfig.Channels.Instances.Select(i => i.Endpoint), installInfo.DownloadUri, tempZipPath, downloadProgressAction, _cancellationTokenSource.Token);

                    StatusMessage = "Extracting manifest...";
                    await _compressionService.ExtractSingleFileAsync(tempZipPath, "ConsistencyInfo", Path.Combine(GameDirectory, "ConsistencyInfo"));
                    File.Delete(tempZipPath);
                }

                StatusMessage = "Checking game files for corruption...";
                
                // THIS IS THE CORRECTED PROGRESS HANDLER
                Action<RepairProgress> repairProgressAction = p =>
                {
                    RepairPercentage = p.Percentage;
                    RepairProgressText = $"{p.Percentage:F0}%";
                    RepairEtaText = p.ETA > TimeSpan.Zero ? $"ETA: {p.ETA:mm\\:ss}" : "ETA: Calculating...";
                    StatusMessage = p.CurrentAction;
                };
                var brokenFiles = await _gameRepairService.CheckConsistencyAsync(GameDirectory, repairProgressAction, _cancellationTokenSource.Token);
                
                if (!brokenFiles.Any())
                {
                    StatusMessage = "Game integrity verified. No errors found.";
                    await CheckGameStatusAsync(GameDirectory);
                    return;
                }

                StatusMessage = $"Found {brokenFiles.Count} broken files. Repairing...";
                await _gameRepairService.RepairFilesAsync(GameDirectory, brokenFiles, LauncherConfig.Channels.Instances.Select(i => i.Endpoint), InstallInfo.UnpackedUri, repairProgressAction, _cancellationTokenSource.Token);

                StatusMessage = "Repair complete!";
                await CheckGameStatusAsync(GameDirectory);
            }
            catch (OperationCanceledException) { StatusMessage = "Repair cancelled."; }
            catch (Exception ex) { StatusMessage = $"An error occurred during repair: {ex.Message}"; }
            finally
            {
                IsRepairing = false;
                IsCancelling = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }
        
        [RelayCommand]
        private void Logout()
        {
            if (OnLogout != null)
            {
                OnLogout();
            }
        }
        #endregion
        
        #region Private Methods & Helpers
        private string LoadGameDirectoryFromRegistry()
        {
            var savedPath = _registryService.GetInstallPath();
            var defaultPath = @"C:\Battlestate Games\EFT";
            if (!string.IsNullOrEmpty(savedPath) && Directory.Exists(savedPath))
            {
                GameDirectory = savedPath;
                return savedPath;
            }
            GameDirectory = defaultPath;
            return defaultPath;
        }

        private async Task InitializeAsync(string gamePath)
        {
            var (config, errorMessage) = await _eftApiService.GetLauncherConfigAsync();
            if (config != null)
            {
                LauncherConfig = config;
                StatusMessage = "Configuration loaded. Checking game status...";
                await CheckGameStatusAsync(gamePath);
            }
            else
            {
                GameStatusText = $"Failed to load configuration: {errorMessage}";
            }
        }

        private async Task CheckGameStatusAsync(string gamePath)
        {
            IsCheckingStatus = true;
            try
            {
                var (installedVersion, status) = await _gameRepairService.GetInstalledVersionAsync(gamePath);
                if (installedVersion == null)
                {
                    GameStatusText = status;
                    ActionButtonText = "DOWNLOAD & INSTALL";
                    IsActionRequired = true;
                    return;
                }
                
                StatusMessage = "Checking for updates...";
                var updateSet = await _updateManagerService.FindUpdatePathAsync(installedVersion.Value);

                if (updateSet == null || !updateSet.Patches.Any())
                {
                    GameStatusText = $"Game is up to date (Version {installedVersion.Value.ToString()})";
                    IsActionRequired = false;
                }
                else
                {
                    GameStatusText = updateSet.Patches.Count > 1
                        ? $"{updateSet.Patches.Count} updates are available! {installedVersion.Value.ToString()} -> {updateSet.TargetVersion.ToString()}"
                        : $"Update available! {installedVersion.Value.ToString()} -> {updateSet.TargetVersion.ToString()}";
                    ActionButtonText = "UPDATE GAME";
                    IsActionRequired = true;
                }
                StatusMessage = "Ready.";
            }
            catch (Exception ex)
            {
                GameStatusText = $"An unexpected error occurred: {ex.Message}";
            }
            finally
            {
                IsCheckingStatus = false;
            }
        }

        private async Task PerformFullInstallAsync()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            IsDownloadingAndInstalling = true;
            var tempDownloadPath = Path.Combine(GameDirectory, "Client.zip.tmp");
            try
            {
                StatusMessage = "Fetching latest version info...";
                var (installInfo, errorMsg) = await _eftApiService.GetGameInstallInfoAsync();
                if (installInfo == null)
                {
                    StatusMessage = $"Failed to get install info: {errorMsg}";
                    return;
                }
                InstallInfo = installInfo;
                
                StatusMessage = "Downloading game client...";
                var downloadServers = LauncherConfig.Channels.Instances.Select(i => i.Endpoint);
                Action<DownloadProgress> downloadProgressAction = p =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        DownloadPercentage = p.Percentage;
                        DownloadProgressText = $"{p.Percentage:F0}%";
                        DownloadSpeed = $"{p.SpeedBytesPerSecond / (1024 * 1024):F2} MB/s";
                        DownloadEtaText = p.ETA > TimeSpan.Zero ? $"ETA: {p.ETA:mm\\:ss}" : "ETA: Calculating...";
                        StatusMessage = $"Downloading... {DownloadPercentage:F0}%";
                    });
                };
                Directory.CreateDirectory(GameDirectory);
                await _downloadService.DownloadFileAsync(downloadServers, InstallInfo.DownloadUri, tempDownloadPath, downloadProgressAction, _cancellationTokenSource.Token);

                StatusMessage = "Download complete. Extracting files...";
                await Task.Run(() => ZipFile.ExtractToDirectory(tempDownloadPath, GameDirectory, true), _cancellationTokenSource.Token);

                _registryService.SetInstallPath(GameDirectory, InstallInfo.Version);
                StatusMessage = "Installation Complete!";
                await CheckGameStatusAsync(GameDirectory);
            }
            catch (OperationCanceledException) { StatusMessage = "Installation cancelled."; }
            catch (Exception ex) { StatusMessage = $"Installation failed: {ex.Message}"; }
            finally
            {
                if (File.Exists(tempDownloadPath)) File.Delete(tempDownloadPath);
                IsDownloadingAndInstalling = false;
                IsCancelling = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private async Task PerformDifferentialUpdateAsync(EftVersion installedVersion)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            IsDownloadingAndInstalling = true;
            var tempUpdateDir = Path.Combine(Path.GetTempPath(), "EFT_Updates");
            try
            {
                StatusMessage = "Finding update path...";
                var updateSet = await _updateManagerService.FindUpdatePathAsync(installedVersion);
                if (updateSet == null || !updateSet.Patches.Any())
                {
                    StatusMessage = "Could not find a valid update path.";
                    await CheckGameStatusAsync(GameDirectory);
                    return;
                }
                
                var downloadedPatchPaths = new List<string>();
                var downloadServers = LauncherConfig.Channels.Instances.Select(i => i.Endpoint);
                Directory.CreateDirectory(tempUpdateDir);
                foreach (var patchInfo in updateSet.Patches)
                {
                    var destinationPath = Path.Combine(tempUpdateDir, Path.GetFileName(patchInfo.DownloadUri));
                    Action<DownloadProgress> downloadProgressAction = p =>
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            DownloadPercentage = p.Percentage;
                            DownloadProgressText = $"{p.Percentage:F0}%";
                            StatusMessage = $"Downloading patch: {Path.GetFileName(patchInfo.DownloadUri)} ({p.Percentage:F0}%)";
                        });
                    };
                    await _downloadService.DownloadFileAsync(downloadServers, patchInfo.DownloadUri, destinationPath, downloadProgressAction, _cancellationTokenSource.Token);
                    downloadedPatchPaths.Add(destinationPath);
                }

                Action<UpdateProgressReport> updateProgressAction = report =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ExtractionPercentage = report.Percentage;
                        ExtractionProgressText = $"{report.Percentage:F0}%";
                        StatusMessage = report.Message ?? "Applying patches...";
                    });
                };
                await _patchingService.ApplyUpdateAsync(downloadedPatchPaths.ToArray(), GameDirectory, updateProgressAction, _cancellationTokenSource.Token);
                
                _registryService.SetInstallPath(GameDirectory, updateSet.TargetVersion.ToString());
                StatusMessage = "Update complete!";
                await CheckGameStatusAsync(GameDirectory);
            }
            catch (OperationCanceledException) { StatusMessage = "Update cancelled."; }
            catch (Exception ex) { StatusMessage = $"Update failed: {ex.Message}"; }
            finally
            {
                if (Directory.Exists(tempUpdateDir)) Directory.Delete(tempUpdateDir, true);
                IsDownloadingAndInstalling = false;
                IsCancelling = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }
        
        private bool IsRunningAsAdmin()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        
        partial void OnGameDirectoryChanged(string value)
        {
            _pathCheckCts?.Cancel();
            _pathCheckCts = new CancellationTokenSource();
            Task.Delay(500, _pathCheckCts.Token)
                .ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        Dispatcher.UIThread.InvokeAsync(() => CheckGameStatusAsync(value));
                    }
                }, TaskScheduler.Default);
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.PropertyName == nameof(IsBusy) || e.PropertyName == nameof(IsCancelling))
            {
                CancelCurrentOperationCommand.NotifyCanExecuteChanged();
            }
            if (e.PropertyName == nameof(IsBusy))
            {
                ManualCheckForUpdateCommand.NotifyCanExecuteChanged();
                BrowseForGameFolderCommand.NotifyCanExecuteChanged();
                RegisterGameCommand.NotifyCanExecuteChanged();
                StartUpdateOrInstallCommand.NotifyCanExecuteChanged();
                RepairGameCommand.NotifyCanExecuteChanged();
                LogoutCommand.NotifyCanExecuteChanged();
            }
            if (e.PropertyName == nameof(LauncherConfig))
            {
                OnPropertyChanged(nameof(WelcomeMessage));
            }
        }
        
        public bool IsNotBusy => !IsBusy;
        private bool CanCancel() => IsBusy && !IsCancelling;
        public string WelcomeMessage => $"Welcome, {LauncherConfig?.Account?.Nickname ?? "User"}!";
        #endregion
    }
}