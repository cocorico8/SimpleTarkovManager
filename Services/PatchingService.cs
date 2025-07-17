using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FastRsync.Delta;
using FastRsync.Diagnostics;
using Newtonsoft.Json;
using SimpleTarkovManager.Models;

namespace SimpleTarkovManager.Services
{
    public class PatchingService
    {
        private readonly CompressionService _compressionService;
        
        public PatchingService(CompressionService compressionService)
        {
            _compressionService = compressionService;
        }
        
        public Task ApplyUpdateAsync(string[] downloadedUpdatePaths, string gamePath, Action<UpdateProgressReport> progressAction, CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                var unpackedPatchesDir = Path.Combine(Path.GetTempPath(), "EFT_Patches_" + Path.GetRandomFileName());
                var stagingDir = gamePath + "_staging";

                try
                {
                    var filesToPatch = new Dictionary<string, (string patchPath, int algorithmId)>();
                    var filesToAdd = new Dictionary<string, string>();
                    var filesToDelete = new HashSet<string>();

                    var totalUpdates = downloadedUpdatePaths.Length;
                    progressAction(new UpdateProgressReport { Stage = UpdateStage.Preparing, Message = $"Preparing to process {totalUpdates} update packages..." });
                    Directory.CreateDirectory(unpackedPatchesDir);

                    for (int i = 0; i < totalUpdates; i++)
                    {
                        var updateFile = downloadedUpdatePaths[i];
                        var patchName = Path.GetFileNameWithoutExtension(updateFile);
                        var specificUnpackDir = Path.Combine(unpackedPatchesDir, patchName);
                        Directory.CreateDirectory(specificUnpackDir);
                        
                        Action<double> unpackProgressAction = percentage =>
                        {
                            // Create a report where the percentage is driven by the sub-task.
                            var report = new UpdateProgressReport
                            {
                                Stage = UpdateStage.Preparing,
                                Message = $"Unpacking update {i + 1} of {totalUpdates} ({percentage:F0}%)",
                                TotalSteps = 100,
                                CurrentStep = (int)percentage
                            };
                            progressAction(report);
                        };
                
                        await _compressionService.ExtractArchiveWithProgressAsync(updateFile, specificUnpackDir, unpackProgressAction, cancellationToken);
                        
                        var manifestPath = Path.Combine(specificUnpackDir, "UpdateInfo");
                        if (File.Exists(manifestPath))
                        {
                            var manifest = JsonConvert.DeserializeObject<UpdateManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken));
                            var patchDir = Path.GetDirectoryName(manifestPath);

                            foreach (var fileEntry in manifest.Files)
                            {
                                switch (fileEntry.State)
                                {
                                    case 1: filesToPatch[fileEntry.Path] = (Path.Combine(patchDir, fileEntry.Path + ".patch"), fileEntry.PatchAlgorithmId); break;
                                    case 2: filesToAdd[fileEntry.Path] = Path.Combine(patchDir, fileEntry.Path); break;
                                    case 3: filesToDelete.Add(fileEntry.Path); break;
                                }
                            }
                        }
                    }
                    
                    var totalSteps = filesToPatch.Count + filesToPatch.Count + filesToAdd.Count + filesToDelete.Count + filesToPatch.Count + filesToAdd.Count;
                    var currentStep = 0;
                    
                    progressAction(new UpdateProgressReport { Stage = UpdateStage.Copying, Message = "Preparing files for patching...", TotalSteps = totalSteps, CurrentStep = currentStep });
                    Directory.CreateDirectory(stagingDir);
                    foreach (var relativePath in filesToPatch.Keys)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        currentStep++;
                        progressAction(new UpdateProgressReport { Stage = UpdateStage.Copying, Message = $"Copying to staging ({currentStep}/{totalSteps}): {relativePath}", TotalSteps = totalSteps, CurrentStep = currentStep });
                        
                        var sourcePath = Path.Combine(gamePath, relativePath);
                        var destPath = Path.Combine(stagingDir, relativePath);
                        if (File.Exists(sourcePath))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                            File.Copy(sourcePath, destPath, true);
                        }
                    }
                    
                    var deltaApplier = new DeltaApplier();
                    foreach (var entry in filesToPatch)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        currentStep++;
                        var relativePath = entry.Key;
                        progressAction(new UpdateProgressReport { Stage = UpdateStage.Patching, Message = $"Applying patch ({currentStep}/{totalSteps}): {relativePath}", TotalSteps = totalSteps, CurrentStep = currentStep });
                        
                        var patchPath = entry.Value.patchPath;
                        var algorithmId = entry.Value.algorithmId;
                        var basisFilePath = Path.Combine(stagingDir, relativePath);

                        try
                        {
                            if (algorithmId == 0) ApplyFastRsyncPatch(basisFilePath, patchPath, deltaApplier);
                            else if (algorithmId == 1) ApplyBsDiffPatch(basisFilePath, patchPath);
                        }
                        catch (Exception ex)
                        {
                            var algorithmName = algorithmId == 0 ? "FastRsync (ID: 0)" : "BsDiff (ID: 1)";
                            var detailedMessage = $"Failed to apply patch to file: '{relativePath}'.\n\n" +
                                                  $"Algorithm Attempted: {algorithmName}\n" +
                                                  $"Underlying Library Error: {ex.Message}";
                    
                            throw new PatchingException(detailedMessage, relativePath, ex);
                        }
                    }
                    
                    foreach (var entry in filesToAdd)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        currentStep++;
                        progressAction(new UpdateProgressReport { Stage = UpdateStage.Patching, Message = $"Adding new file ({currentStep}/{totalSteps}): {entry.Key}", TotalSteps = totalSteps, CurrentStep = currentStep });
                        
                        var destPath = Path.Combine(stagingDir, entry.Key);
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                        File.Copy(entry.Value, destPath, true);
                    }

                    progressAction(new UpdateProgressReport { Stage = UpdateStage.Finalizing, Message = "Finalizing update...", TotalSteps = totalSteps, CurrentStep = currentStep });
                    foreach (var relativePath in filesToDelete)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        currentStep++;
                        progressAction(new UpdateProgressReport { Stage = UpdateStage.Finalizing, Message = $"Deleting file ({currentStep}/{totalSteps}): {relativePath}", TotalSteps = totalSteps, CurrentStep = currentStep });
                        
                        var pathToDelete = Path.Combine(gamePath, relativePath);
                        if (File.Exists(pathToDelete)) File.Delete(pathToDelete);
                    }

                    var stagedFiles = Directory.GetFiles(stagingDir, "*.*", SearchOption.AllDirectories);
                    foreach (var stagedFile in stagedFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        currentStep++;
                        var relativePath = stagedFile.Substring(stagingDir.Length + 1);
                        progressAction(new UpdateProgressReport { Stage = UpdateStage.Finalizing, Message = $"Moving file ({currentStep}/{totalSteps}): {relativePath}", TotalSteps = totalSteps, CurrentStep = currentStep });

                        var finalPath = Path.Combine(gamePath, relativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(finalPath));
                        File.Move(stagedFile, finalPath, true);
                    }

                    progressAction(new UpdateProgressReport { Stage = UpdateStage.Finalizing, Message = "Update complete!", TotalSteps = totalSteps, CurrentStep = totalSteps });
                }
                finally
                {
                    if (Directory.Exists(unpackedPatchesDir)) Directory.Delete(unpackedPatchesDir, true);
                    if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
                }
            }, cancellationToken);
        }

        private void ApplyFastRsyncPatch(string basisFilePath, string deltaFilePath, DeltaApplier deltaApplier)
        {
            if (!File.Exists(basisFilePath) || !File.Exists(deltaFilePath)) return;
            var tempNewFilePath = Path.GetTempFileName();
            try
            {
                using (var basisStream = new FileStream(basisFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var deltaStream = new FileStream(deltaFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var newFileStream = new FileStream(tempNewFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    var deltaReader = new BinaryDeltaReader(deltaStream, new Progress<ProgressReport>());
                    deltaApplier.Apply(basisStream, deltaReader, newFileStream);
                }
                // If patching was successful, replace the original file with the newly patched version.
                File.Copy(tempNewFilePath, basisFilePath, true);
            }
            finally
            {
                // Ensure all temporary files are deleted, even if an error occurs.
                if (File.Exists(tempNewFilePath)) File.Delete(tempNewFilePath);
            }
        }

        private void ApplyBsDiffPatch(string basisFilePath, string patchFilePath)
        {
            if (!File.Exists(basisFilePath) || !File.Exists(patchFilePath)) return;

            var tempNewFilePath = Path.GetTempFileName();
            var tempPatchFilePath = Path.GetTempFileName();

            try
            {
                // Copy the original patch to a temporary location. This avoids any potential file locking issues
                // and provides a clean file to work with.
                File.Copy(patchFilePath, tempPatchFilePath, true);

                using (var basisStream = new FileStream(basisFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var newFileStream = new FileStream(tempNewFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    Func<Stream> openPatchStreamFunc = () => new FileStream(tempPatchFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    
                    BinaryPatch.Apply(basisStream, openPatchStreamFunc, newFileStream);
                }

                // If patching was successful, replace the original file with the newly patched version.
                File.Copy(tempNewFilePath, basisFilePath, true);
            }
            finally
            {
                // Ensure all temporary files are deleted, even if an error occurs.
                if (File.Exists(tempNewFilePath)) File.Delete(tempNewFilePath);
                if (File.Exists(tempPatchFilePath)) File.Delete(tempPatchFilePath);
            }
        }
    }
}