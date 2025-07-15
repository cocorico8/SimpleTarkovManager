using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private readonly DownloadService _downloadService;

        public GameRepairService(DownloadService downloadService)
        {
            _downloadService = downloadService;
        }

        public async Task<(EftVersion? Version, string Status)> GetInstalledVersionAsync(string gameDirectory)
        {
            var exePath = Path.Combine(gameDirectory, "EscapeFromTarkov.exe");
            if (!File.Exists(exePath))
            {
                return (null, "Game not installed (executable missing).");
            }

            var manifestPath = Path.Combine(gameDirectory, "ConsistencyInfo");
            if (!File.Exists(manifestPath))
            {
                return (null, "Game files found, but version is unknown. Please Repair.");
            }

            try
            {
                var manifestJson = await File.ReadAllTextAsync(manifestPath);
                if (string.IsNullOrWhiteSpace(manifestJson))
                {
                    return (null, "Version file is empty. Please Repair.");
                }

                var consistencyInfo = JsonConvert.DeserializeObject<ConsistencyInfo>(manifestJson);
                if (consistencyInfo == null)
                {
                    return (null, "Failed to parse version file. Please Repair.");
                }

                if (EftVersion.TryParse(consistencyInfo.Version, out var parsedVersion))
                {
                    return (parsedVersion, $"Version {parsedVersion} detected.");
                }

                return (null, "Version information is missing or invalid in ConsistencyInfo. Please Repair.");
            }
            catch (JsonReaderException ex)
            {
                Console.WriteLine($"JSON parsing error in ConsistencyInfo: {ex.Message}");
                return (null, "Version file is corrupt (invalid JSON). Please Repair.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading installed game version: {ex.Message}");
                return (null, "An unexpected error occurred while reading the version file. Please Repair.");
            }
        }

        // THE SIGNATURE IS NOW CORRECTLY 'Action<RepairProgress>'
        public async Task<List<ConsistencyEntry>> CheckConsistencyAsync(string gameDirectory, Action<RepairProgress> progressAction, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                var brokenFiles = new List<ConsistencyEntry>();
                var manifestPath = Path.Combine(gameDirectory, "ConsistencyInfo");
                if (!File.Exists(manifestPath)) return brokenFiles;

                var manifest = JsonConvert.DeserializeObject<ConsistencyInfo>(File.ReadAllText(manifestPath));
                if (manifest == null || !manifest.Entries.Any()) return brokenFiles;

                var totalFiles = manifest.Entries.Count;
                var filesProcessed = 0;
                var stopwatch = Stopwatch.StartNew();

                foreach (var entry in manifest.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var filePath = Path.Combine(gameDirectory, entry.Path);
                    
                    if (!File.Exists(filePath) || new FileInfo(filePath).Length != entry.Size)
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
                    
                    var report = new RepairProgress { TotalFiles = totalFiles, FilesProcessed = filesProcessed, CurrentAction = $"Checking: {entry.Path}", ETA = eta };
                    Dispatcher.UIThread.Invoke(() => progressAction(report));
                }
                return brokenFiles;
            }, cancellationToken);
        }

        // THE SIGNATURE IS NOW CORRECTLY 'Action<RepairProgress>'
        public async Task RepairFilesAsync(string gameDirectory, List<ConsistencyEntry> brokenFiles, IEnumerable<string> downloadChannels, string unpackedUri, Action<RepairProgress> progressAction, CancellationToken cancellationToken)
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

                Action<DownloadProgress> fileProgressAction = p =>
                {
                    var report = new RepairProgress
                    {
                        TotalFiles = totalFiles,
                        FilesProcessed = filesProcessed - 1, // Show progress on the current file
                        CurrentAction = $"Downloading ({p.Percentage:F0}%): {relativePath}"
                    };
                    Dispatcher.UIThread.Invoke(() => progressAction(report));
                };

                await _downloadService.DownloadFileAsync(downloadChannels, downloadRelativeUri, destinationPath, fileProgressAction, cancellationToken);
            }
            
            var finalReport = new RepairProgress { TotalFiles = totalFiles, FilesProcessed = totalFiles, CurrentAction = "Repair complete." };
            Dispatcher.UIThread.Invoke(() => progressAction(finalReport));
        }
    }
}