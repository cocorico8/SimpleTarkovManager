using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SimpleTarkovManager.Models;

namespace SimpleTarkovManager.Services
{
    public class RepairProgress
    {
        public int TotalFiles { get; set; }
        public int FilesProcessed { get; set; }
        public TimeSpan ETA { get; set; }
        public string CurrentAction { get; set; } = "";
        public double Percentage => TotalFiles > 0 ? (double)FilesProcessed / TotalFiles * 100 : 0;
    }

    public class GameRepairService
    {
        private readonly IDownloadService _downloadService;

        public GameRepairService(IDownloadService downloadService)
        {
            _downloadService = downloadService;
        }

        public async Task RestoreConsistencyInfoAsync(string gamePath, GameInstallInfo latestInstallInfo, IEnumerable<string> downloadChannels, int workerLimit, CancellationToken cancellationToken)
        {
            var manifestPath = Path.Combine(gamePath, "ConsistencyInfo");
            var consistencyInfoUri = Path.Combine(latestInstallInfo.UnpackedUri, "ConsistencyInfo").Replace('\\', '/');
            await _downloadService.DownloadFileAsync(downloadChannels, consistencyInfoUri, manifestPath, workerLimit, cancellationToken);
        }
        
        public async Task<(EftVersion? Version, VersionStatus Status)> GetInstalledVersionAsync(string gameDirectory)
        {
            var exePath = Path.Combine(gameDirectory, "EscapeFromTarkov.exe");
            if (!File.Exists(exePath))
            {
                return (null, VersionStatus.ExecutableMissing);
            }

            // Attempt to read both versions using EftVersion.
            EftVersion.TryFromFile(exePath, out EftVersion? exeVersion);

            EftVersion? manifestVersion = null;
            var manifestPath = Path.Combine(gameDirectory, "ConsistencyInfo");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var manifestJson = await File.ReadAllTextAsync(manifestPath);
                    var consistencyInfo = JsonConvert.DeserializeObject<ConsistencyInfo>(manifestJson);
                    EftVersion.TryParse(consistencyInfo?.Version, out manifestVersion);
                }
                catch { /* Manifest is corrupt, version remains null */ }
            }


            // Case 1: Both versions were read successfully.
            if (manifestVersion.HasValue && exeVersion.HasValue)
            {
                if (manifestVersion.Value == exeVersion.Value)
                {
                    // The versions match. This is the only perfect state.
                    return (manifestVersion.Value, VersionStatus.OK);
                }
                else
                {
                    // The versions do not match. This is a clear mismatch.
                    return (manifestVersion, VersionStatus.VersionMismatch);
                }
            }

            // Case 2: Manifest is readable, but EXE is not.
            if (manifestVersion.HasValue)
            {
                return (manifestVersion, VersionStatus.VersionMismatch);
            }

            // Case 3: EXE is readable, but manifest is not.
            if (exeVersion.HasValue)
            {
                return (exeVersion, VersionStatus.ManifestIsCorrupt);
            }

            // Case 4: Neither is readable.
            return (null, VersionStatus.CriticallyCorrupt);
        }

        public async Task<List<ConsistencyEntry>> CheckConsistencyAsync(string gameDirectory, Action<RepairProgress> progressAction, CancellationToken cancellationToken)
        {
            return await Task.Run(async () =>
            {
                var brokenFiles = new List<ConsistencyEntry>();
                var manifestPath = Path.Combine(gameDirectory, "ConsistencyInfo");
                if (!File.Exists(manifestPath)) return brokenFiles;

                var manifest = JsonConvert.DeserializeObject<ConsistencyInfo>(File.ReadAllText(manifestPath));
                if (manifest == null || !manifest.Entries.Any()) return brokenFiles;

                using var md5 = MD5.Create();

                var totalFiles = manifest.Entries.Count;
                var filesProcessed = 0;
                var stopwatch = Stopwatch.StartNew();

                foreach (var entry in manifest.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var filePath = Path.Combine(gameDirectory, entry.Path);
                    bool isBroken = false;

                    if (!File.Exists(filePath) || new FileInfo(filePath).Length != entry.Size)
                    {
                        isBroken = true;
                    }
                    else
                    {
                        try
                        {
                            using var fileStream = File.OpenRead(filePath);
                            var hashBytes = await md5.ComputeHashAsync(fileStream, cancellationToken);
                            var localHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                            
                            if (!string.Equals(localHash, entry.Hash, StringComparison.OrdinalIgnoreCase))                            {
                                isBroken = true;
                            }
                        }
                        catch
                        {
                            isBroken = true; 
                        }
                    }

                    if (isBroken)
                    {
                        brokenFiles.Add(entry);
                    }

                    filesProcessed++;
                    var eta = TimeSpan.Zero;
                    if (filesProcessed > 10 && stopwatch.Elapsed.TotalSeconds > 0.5)
                    {
                        var filesPerSecond = filesProcessed / stopwatch.Elapsed.TotalSeconds;
                        if (filesPerSecond > 0) eta = TimeSpan.FromSeconds((totalFiles - filesProcessed) / filesPerSecond);
                    }
                    
                    var report = new RepairProgress 
                    { 
                        TotalFiles = totalFiles, 
                        FilesProcessed = filesProcessed, 
                        CurrentAction = $"Checking file {filesProcessed} of {totalFiles}: {entry.Path}",
                        ETA = eta 
                    };
                    Dispatcher.UIThread.Invoke(() => progressAction(report));
                }
                return brokenFiles;
            }, cancellationToken);
        }


        public async Task RepairFilesAsync(string gameDirectory, List<ConsistencyEntry> brokenFiles, IEnumerable<string> downloadChannels, string unpackedUri, int workerLimit, Action<RepairProgress> progressAction, CancellationToken cancellationToken)
        {
            var totalFiles = brokenFiles.Count;
            var filesProcessed = 0;

            foreach (var fileToRepair in brokenFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                filesProcessed++;
                
                var relativePath = fileToRepair.Path;
                var destinationPath = Path.Combine(gameDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                
                var downloadRelativeUri = Path.Combine(unpackedUri, relativePath).Replace('\\', '/');

                // Create a progress handler specifically for this file download.
                var singleFileProgress = new Progress<DownloadProgress>(p =>
                {
                    // This code will run every time the DownloadService reports progress for this specific file.
                    var report = new RepairProgress
                    {
                        TotalFiles = totalFiles,
                        FilesProcessed = filesProcessed - 1, // Overall progress is based on *completed* files
                        CurrentAction = $"Downloading file {filesProcessed} of {totalFiles}: {relativePath} ({p.Percentage:F0}%)"
                    };
                    // Use the main progress action to send the update to the ViewModel.
                    Dispatcher.UIThread.Invoke(() => progressAction(report));
                });

                // Pass the handler to the download service.
                await _downloadService.DownloadFileAsync(downloadChannels, downloadRelativeUri, destinationPath, workerLimit, cancellationToken);

                // After the download is complete, update the overall progress to reflect that one more file is done.
                var finalFileReport = new RepairProgress { TotalFiles = totalFiles, FilesProcessed = filesProcessed, CurrentAction = $"Finished downloading file {filesProcessed} of {totalFiles}." };
                progressAction(finalFileReport);
            }
            
            var finalReport = new RepairProgress { TotalFiles = totalFiles, FilesProcessed = totalFiles, CurrentAction = "Repair complete." };
            Dispatcher.UIThread.Invoke(() => progressAction(finalReport));
        }
    }
}