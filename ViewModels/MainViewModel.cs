using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        [ObservableProperty]
        private LauncherConfig? _launcherConfig;
        [ObservableProperty]
        private GameInstallInfo? _installInfo;
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartUpdateOrInstallCommand))]
        [NotifyCanExecuteChangedFor(nameof(RepairGameCommand))]
        private string _gameDirectory;
        [ObservableProperty]
        private string _gameStatusText = "Initializing...";
        [ObservableProperty]
        private string _statusMessage = "Ready.";
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartUpdateOrInstallCommand))]
        private bool _isActionRequired;
        [ObservableProperty]
        private string _actionButtonText = "DOWNLOAD & INSTALL";

        // State Flags
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBusy))]
        [NotifyPropertyChangedFor(nameof(IsNotBusy))]
        private bool _isCheckingStatus;
    
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBusy))]
        [NotifyPropertyChangedFor(nameof(IsNotBusy))]
        private bool _isDownloadingAndInstalling;
    
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBusy))]
        [NotifyPropertyChangedFor(nameof(IsNotBusy))]
        private bool _isRepairing;
    
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBusy))]
        [NotifyPropertyChangedFor(nameof(IsNotBusy))]
        [NotifyCanExecuteChangedFor(nameof(CancelCurrentOperationCommand))]
        private bool _isCancelling;
        
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ManualCheckForUpdateCommand))]
        private bool _isManuallyChecking;
        public bool IsBusy => IsCheckingStatus || IsDownloadingAndInstalling || IsRepairing;

        // Progress Properties
        [ObservableProperty]
        private double _downloadPercentage;
        [ObservableProperty]
        private string _downloadProgressText = "0%";
        [ObservableProperty]
        private string _downloadSpeed = "0 MB/s";
        [ObservableProperty]
        private string _downloadEtaText = "ETA: --";
        [ObservableProperty]
        private string _downloadSizeText = "0.0 MB / 0.0 MB";
        [ObservableProperty]
        private double _extractionPercentage;
        [ObservableProperty]
        private string _extractionProgressText = "0%";
        [ObservableProperty]
        private string _extractionEtaText = "ETA: --";
        [ObservableProperty]
        private double _repairPercentage;
        [ObservableProperty]
        private string _repairProgressText = "0%";
        [ObservableProperty]
        private string _repairEtaText = "ETA: --";

        public bool IsNotBusy => !IsBusy;
        [ObservableProperty]
        private string _welcomeMessage;
        #endregion
        
        private UpdateSet? _availableUpdateSet;
        private CancellationTokenSource? _pathCheckCts;
        private CancellationTokenSource? _cancellationTokenSource;
        public event Action OnLogout;
        
        public void Cleanup()
        {
            // Unsubscribe from any long-lived service events here
            _downloadService.ProgressChanged -= OnDownloadProgressChanged;
        }

        public MainViewModel(DialogService dialogService, RegistryService registryService, AuthService authService, EftApiService eftApiService, DownloadService downloadService, CompressionService compressionService, GameRepairService gameRepairService, UpdateManagerService updateManagerService, PatchingService patchingService, string[] launchArgs)
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
            
            _downloadService.ProgressChanged += OnDownloadProgressChanged;
            
            var initialGamePath = LoadGameDirectoryFromRegistry();
            _gameDirectory = initialGamePath;
            
            _ = InitializeAsync(initialGamePath);
            
            if (launchArgs.Contains("--register-game"))
            {
                // Use fire-and-forget because this is a constructor
                _ = RegisterGameAsync();
            }
        }

        #region Event Handlers
        private void OnDownloadProgressChanged(DownloadProgress p)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                DownloadPercentage = p.Percentage;
                DownloadProgressText = $"{p.Percentage:F0}%";
                DownloadSpeed = $"{p.SpeedBytesPerSecond / (1024 * 1024):F2} MB/s";
                DownloadEtaText = p.ETA > TimeSpan.Zero ? $"ETA: {p.ETA:mm\\:ss}" : "ETA: Calculating...";

                var downloadedSize = FormatBytes(p.BytesDownloaded);
                var totalSize = FormatBytes(p.TotalBytes);
                DownloadSizeText = $"{downloadedSize} / {totalSize}";
            });
        }
        #endregion

        #region Commands
        
        [RelayCommand(CanExecute = nameof(IsNotBusy))]
        private async Task RunConnectionTestAsync()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            IsDownloadingAndInstalling = true; 
            StatusMessage = "Running final UI connection test...";
            try
            {
                await _downloadService.RunUiConnectionTest(_cancellationTokenSource.Token);
                StatusMessage = "Test complete.";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Test cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Test failed: {ex.Message}";
            }
            finally
            {
                IsDownloadingAndInstalling = false;
                IsCancelling = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }
        
        [RelayCommand(CanExecute = nameof(CanManualCheck))]
        private async Task ManualCheckForUpdateAsync()
        {
            try
            {
                IsManuallyChecking = true;
                await CheckGameStatusAsync(GameDirectory);
            }
            finally
            {
                IsManuallyChecking = false;
            }
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
            if (status != VersionStatus.OK || installedVersion == null)
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
                    
                    var startInfo = new ProcessStartInfo(exeName, "--register-game")
                    {
                        Verb = "runas",
                        // We must explicitly set this to true to use OS-level features like the 'runas' verb.
                        UseShellExecute = true
                    };

                    Process.Start(startInfo);
                    (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // The user canceled UAC prompt.
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

        [RelayCommand(CanExecute = nameof(CanStartUpdateOrInstall))]
        private async Task StartUpdateOrInstallAsync()
        {
            var (installedVersion, status) = await _gameRepairService.GetInstalledVersionAsync(GameDirectory);

            switch (status)
            {
                case VersionStatus.OK:
                    await PerformDifferentialUpdateAsync();
                    break;
                case VersionStatus.ExecutableMissing:
                case VersionStatus.CriticallyCorrupt:
                    await PerformFullInstallAsync();
                    break;
                case VersionStatus.ManifestIsCorrupt:
                case VersionStatus.VersionMismatch:
                    await RepairGameAsync();
                    break;
            }
        }
        
        [RelayCommand(CanExecute = nameof(CanRepairGame))]
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
                if (InstallInfo == null)
                {
                    StatusMessage = "Fetching latest version info for repair...";
                    var (installInfo, errorMsg) = await _eftApiService.GetGameInstallInfoAsync();
                    if (installInfo == null)
                    {
                        StatusMessage = $"Cannot get install info: {errorMsg}";
                        return;
                    }
                    InstallInfo = installInfo;
                }

                var (installedVersion, status) = await _gameRepairService.GetInstalledVersionAsync(GameDirectory);
                bool manifestIsCorrupt = status == VersionStatus.ManifestIsCorrupt || status == VersionStatus.VersionMismatch;

                if (!File.Exists(Path.Combine(GameDirectory, "ConsistencyInfo")) || manifestIsCorrupt)
                {
                    StatusMessage = "Version corrupt or mismatched. Attempting to restore manifest...";
                    await _gameRepairService.RestoreConsistencyInfoAsync(GameDirectory, InstallInfo, LauncherConfig.Channels.Instances.Select(i => i.Endpoint), _cancellationTokenSource.Token);
                    StatusMessage = "Manifest restored. Now checking game files...";
                }

                StatusMessage = "Checking game files for corruption...";
                Action<RepairProgress> repairProgressAction = p =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        RepairPercentage = p.Percentage;
                        RepairProgressText = $"{p.Percentage:F0}%";
                        RepairEtaText = p.ETA > TimeSpan.Zero ? $"ETA: {p.ETA:mm\\:ss}" : "ETA: Calculating...";
                        StatusMessage = p.CurrentAction;
                    });
                };
                var brokenFiles = await _gameRepairService.CheckConsistencyAsync(GameDirectory, repairProgressAction, _cancellationTokenSource.Token);
                
                if (!brokenFiles.Any())
                {
                    StatusMessage = "Game integrity verified. No errors found.";
                    await CheckGameStatusAsync(GameDirectory);
                    return;
                }

                StatusMessage = $"Found {brokenFiles.Count} broken files. Repairing...";
                // Now this call is guaranteed to have the correct UnpackedUri from the InstallInfo we fetched.
                await _gameRepairService.RepairFilesAsync(GameDirectory, brokenFiles, LauncherConfig.Channels.Instances.Select(i => i.Endpoint), InstallInfo.UnpackedUri, repairProgressAction, _cancellationTokenSource.Token);

                StatusMessage = "Repair complete!";
                // Check the status again to confirm everything is fixed.
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
            OnLogout?.Invoke();
            File.Delete("auth.json");
        }
        

        #endregion
        
        #region Private Methods & Helpers

        private string FormatBytes(long bytes)
        {
            const int scale = 1024;
            string[] orders = { "GB", "MB", "KB", "Bytes" };
            long max = (long)Math.Pow(scale, orders.Length - 1);

            if (bytes == 0) return "0.0 MB";

            for (int i = 0; i < orders.Length; i++)
            {
                if (bytes > max)
                {
                    return $"{(double)bytes / max:F2} {orders[i]}";
                }
                max /= scale;
            }
            return "0 Bytes";
        }
        
        private bool CanManualCheck()
        {
            // The user can only start a manual check if:
            // 1. No other major operation is running (IsNotBusy).
            // 2. A manual check isn't ALREADY running (IsManuallyChecking is false).
            return IsNotBusy && !IsManuallyChecking;
        }
        
        private void UpdateAllCommandStates()
        {
            StartUpdateOrInstallCommand.NotifyCanExecuteChanged();
            RepairGameCommand.NotifyCanExecuteChanged();
            CancelCurrentOperationCommand.NotifyCanExecuteChanged();
            ManualCheckForUpdateCommand.NotifyCanExecuteChanged();
            RunConnectionTestCommand.NotifyCanExecuteChanged();
            BrowseForGameFolderCommand.NotifyCanExecuteChanged();
            RegisterGameCommand.NotifyCanExecuteChanged();
        }
        
        partial void OnIsCheckingStatusChanged(bool value) => UpdateAllCommandStates();
        partial void OnIsDownloadingAndInstallingChanged(bool value) => UpdateAllCommandStates();
        partial void OnIsRepairingChanged(bool value) => UpdateAllCommandStates();
        partial void OnIsCancellingChanged(bool value) => UpdateAllCommandStates();

        private string LoadGameDirectoryFromRegistry()
        {
            var savedPath = _registryService.GetInstallPath();
            const string defaultPath = @"C:\Battlestate Games\EFT";
        
            if (!string.IsNullOrEmpty(savedPath) && Directory.Exists(savedPath))
            {
                return savedPath;
            }
            return defaultPath;
        }

        private async Task InitializeAsync(string gamePath)
        {
            var (config, errorMessage) = await _eftApiService.GetLauncherConfigAsync();
            if (config != null)
            {
                LauncherConfig = config;
                WelcomeMessage = $"Welcome, {config.Account?.Nickname ?? "User"}!";
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
                _availableUpdateSet = null;
                var (installedVersion, status) = await _gameRepairService.GetInstalledVersionAsync(gamePath);

                GameStatusText = status switch
                {
                    VersionStatus.ExecutableMissing => "Game not installed (executable missing).",
                    VersionStatus.CriticallyCorrupt => "Installation is critically corrupt.",
                    VersionStatus.ManifestIsCorrupt => "Game files found, but version is unknown. Please Repair.",
                    VersionStatus.VersionMismatch => "Version mismatch between EXE and game files. Please Repair.", // <-- NEW
                    _ => GameStatusText
                };

                if (status != VersionStatus.OK)
                {
                    // For any non-OK status, we need user action.
                    ActionButtonText = (status == VersionStatus.ExecutableMissing || status == VersionStatus.CriticallyCorrupt) 
                        ? "DOWNLOAD & INSTALL" 
                        : "REPAIR"; // Repair is the correct action for both ManifestIsCorrupt and VersionMismatch
                    IsActionRequired = true;
                    IsCheckingStatus = false; 
                    return;
                }
                
                StatusMessage = "Checking for updates...";
                var updateSet = await _updateManagerService.FindUpdatePathAsync(installedVersion.Value);
                _availableUpdateSet = updateSet;

                if (_availableUpdateSet == null || !_availableUpdateSet.Patches.Any())
                {
                    GameStatusText = $"Game is up to date (Version {installedVersion.Value.ToString()})";
                    IsActionRequired = false;
                }
                else
                {
                    GameStatusText = _availableUpdateSet.Patches.Count > 1
                        ? $"{_availableUpdateSet.Patches.Count} updates are available! {installedVersion.Value.ToString()} -> {_availableUpdateSet.TargetVersion.ToString()}"
                        : $"Update available! {installedVersion.Value.ToString()} -> {_availableUpdateSet.TargetVersion.ToString()}";
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
                    // On a startup failure, we should stop being busy.
                    IsDownloadingAndInstalling = false;
                    return;
                }

                InstallInfo = installInfo;

                StatusMessage = "Downloading game client...";
                var downloadServers = LauncherConfig.Channels.Instances.Select(i => i.Endpoint);

                Directory.CreateDirectory(GameDirectory);
                await _downloadService.DownloadFileAsync(downloadServers, InstallInfo.DownloadUri, tempDownloadPath,
                    _cancellationTokenSource.Token);

                StatusMessage = "Download complete. Extracting files...";
                Action<double> extractionProgressAction = percentage =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ExtractionPercentage = percentage;
                        ExtractionProgressText = $"{percentage:F0}%";
                        StatusMessage = $"Extracting... {percentage:F0}%";
                    });
                };
                await _compressionService.ExtractArchiveWithProgressAsync(tempDownloadPath, GameDirectory,
                    extractionProgressAction, _cancellationTokenSource.Token);

                _registryService.SetInstallPath(GameDirectory, InstallInfo.Version);
                StatusMessage = "Installation Complete!";

                await CheckGameStatusAsync(GameDirectory);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Installation cancelled.";
            }
            catch (Exception ex)
            {
                // On ANY other exception, set the error message.
                // DO NOT change the IsDownloadingAndInstalling flag here. This leaves the UI visible.
                StatusMessage = $"Installation failed: {ex.Message}";
            }
            finally
            {
                IsDownloadingAndInstalling = false;
                IsCancelling = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                if (File.Exists(tempDownloadPath)) File.Delete(tempDownloadPath);
            }
        }

        private async Task PerformDifferentialUpdateAsync()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            IsDownloadingAndInstalling = true;
            var tempUpdateDir = Path.Combine(Path.GetTempPath(), "EFT_Updates");

            try
            {
                var updateSet = _availableUpdateSet;
                if (updateSet == null || !updateSet.Patches.Any())
                {
                    StatusMessage = "Update information is missing or stale. Please check again.";
                    IsDownloadingAndInstalling = false;
                    return;
                }
                
                var downloadedPatchPaths = new List<string>();
                var downloadServers = LauncherConfig.Channels.Instances.Select(i => i.Endpoint);
                Directory.CreateDirectory(tempUpdateDir);

                var totalPatches = updateSet.Patches.Count;
                var currentPatchIndex = 0;
                foreach (var patchInfo in updateSet.Patches)
                {
                    currentPatchIndex++;
                    StatusMessage = $"Downloading patch {currentPatchIndex} of {totalPatches} ({patchInfo.Version})...";
                    var destinationPath = Path.Combine(tempUpdateDir, Path.GetFileName(patchInfo.DownloadUri));
                    await _downloadService.DownloadFileAsync(downloadServers, patchInfo.DownloadUri, destinationPath, _cancellationTokenSource.Token);
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
        
                if (Directory.Exists(tempUpdateDir)) Directory.Delete(tempUpdateDir, true);
                IsDownloadingAndInstalling = false;
                await CheckGameStatusAsync(GameDirectory);
            }
            catch (PatchingException ex)
            {
                StatusMessage = $"FATAL PATCH ERROR:\n{ex.Message}\n\n" +
                                $"Downloaded patch files are preserved for inspection in:\n{tempUpdateDir}";
            }
            catch (OperationCanceledException) 
            { 
                StatusMessage = "Update cancelled.";
                if (Directory.Exists(tempUpdateDir)) Directory.Delete(tempUpdateDir, true);
                IsDownloadingAndInstalling = false;
            }
            catch (Exception ex) 
            { 
                StatusMessage = $"Update failed: {ex.Message}";
                if (Directory.Exists(tempUpdateDir)) Directory.Delete(tempUpdateDir, true);
            }
            finally
            {
                IsDownloadingAndInstalling = false;
                _availableUpdateSet = null; // Always clear the plan after an attempt.
                IsCancelling = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                
                if (Directory.Exists(tempUpdateDir)) Directory.Delete(tempUpdateDir, true);
            }
        }
        
        private static bool IsRunningAsAdmin()
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
        
        private bool CanStartUpdateOrInstall()
        {
            return IsActionRequired && IsNotBusy;
        }

        private bool CanRepairGame()
        {
            return !string.IsNullOrEmpty(GameDirectory) && IsNotBusy;
        }

        private bool CanCancel() => IsBusy && !IsCancelling;
        #endregion
    }
}