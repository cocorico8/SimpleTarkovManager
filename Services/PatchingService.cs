using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Diagnostics;
using Newtonsoft.Json;
using SimpleTarkovManager.Models;
using SimpleTarkovManager.Services;

namespace SimpleEFTLauncher.Services
{
    public class PatchingService
    {
        // THE SIGNATURE IS NOW CORRECTLY 'Action<UpdateProgressReport>'
        public Task ApplyUpdateAsync(string[] downloadedUpdatePaths, string gamePath, Action<UpdateProgressReport> progressAction, CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                var unpackedPatchesDir = Path.Combine(Path.GetTempPath(), "EFT_Patches_" + Path.GetRandomFileName());
                var stagingDir = gamePath + "_staging";

                try
                {
                    Dispatcher.UIThread.Invoke(() => progressAction(new UpdateProgressReport { Stage = UpdateStage.Preparing, Message = "Analyzing update packages..." }));
                    
                    Directory.CreateDirectory(unpackedPatchesDir);
                    foreach (var updateFile in downloadedUpdatePaths)
                    {
                        var patchName = Path.GetFileNameWithoutExtension(updateFile);
                        var specificUnpackDir = Path.Combine(unpackedPatchesDir, patchName);
                        Directory.CreateDirectory(specificUnpackDir);
                        ZipFile.ExtractToDirectory(updateFile, specificUnpackDir, true);
                    }

                    var filesToPatch = new Dictionary<string, (string patchPath, int algorithmId)>();
                    var filesToAdd = new Dictionary<string, string>();
                    var filesToDelete = new HashSet<string>();

                    var manifestPaths = Directory.GetFiles(unpackedPatchesDir, "UpdateInfo", SearchOption.AllDirectories);
                    foreach (var manifestPath in manifestPaths)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var manifest = JsonConvert.DeserializeObject<UpdateManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken));
                        var patchDir = Path.GetDirectoryName(manifestPath);

                        foreach (var fileEntry in manifest.Files)
                        {
                            switch (fileEntry.State)
                            {
                                case 1:
                                    filesToPatch[fileEntry.Path] = (Path.Combine(patchDir, fileEntry.Path + ".patch"), fileEntry.PatchAlgorithmId);
                                    break;
                                case 2:
                                    filesToAdd[fileEntry.Path] = Path.Combine(patchDir, fileEntry.Path);
                                    break;
                                case 3:
                                    filesToDelete.Add(fileEntry.Path);
                                    break;
                            }
                        }
                    }
                    
                    Dispatcher.UIThread.Invoke(() => progressAction(new UpdateProgressReport { Stage = UpdateStage.Copying, Message = "Preparing files for patching..." }));
                    Directory.CreateDirectory(stagingDir);
                    foreach (var relativePath in filesToPatch.Keys)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
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
                        var relativePath = entry.Key;
                        var patchPath = entry.Value.patchPath;
                        var algorithmId = entry.Value.algorithmId;
                        var basisFilePath = Path.Combine(stagingDir, relativePath);

                        var report = new UpdateProgressReport { Stage = UpdateStage.Patching, Message = $"Applying patch to: {relativePath}" };
                        Dispatcher.UIThread.Invoke(() => progressAction(report));

                        if (algorithmId == 0) ApplyFastRsyncPatch(basisFilePath, patchPath, deltaApplier);
                        else if (algorithmId == 1) ApplyBsDiffPatch(basisFilePath, patchPath);
                    }
                    foreach (var entry in filesToAdd)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var report = new UpdateProgressReport { Stage = UpdateStage.Patching, Message = $"Adding new file: {entry.Key}" };
                        Dispatcher.UIThread.Invoke(() => progressAction(report));
                        
                        var destPath = Path.Combine(stagingDir, entry.Key);
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                        File.Copy(entry.Value, destPath, true);
                    }

                    Dispatcher.UIThread.Invoke(() => progressAction(new UpdateProgressReport { Stage = UpdateStage.Finalizing, Message = "Finalizing update..." }));
                    foreach (var relativePath in filesToDelete)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var pathToDelete = Path.Combine(gamePath, relativePath);
                        if (File.Exists(pathToDelete)) File.Delete(pathToDelete);
                    }
                    foreach (var stagedFile in Directory.GetFiles(stagingDir, "*.*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relativePath = stagedFile.Substring(stagingDir.Length + 1);
                        var finalPath = Path.Combine(gamePath, relativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(finalPath));
                        File.Move(stagedFile, finalPath, true);
                    }
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
                File.Copy(tempNewFilePath, basisFilePath, true);
            }
            finally
            {
                if (File.Exists(tempNewFilePath)) File.Delete(tempNewFilePath);
            }
        }

        private void ApplyBsDiffPatch(string basisFilePath, string patchFilePath)
        {
            if (!File.Exists(basisFilePath) || !File.Exists(patchFilePath)) return;
            var tempNewFilePath = Path.GetTempFileName();
            try
            {
                using (var basisStream = new FileStream(basisFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var patchStream = new FileStream(patchFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var newFileStream = new FileStream(tempNewFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    using var seekablePatchStream = new MemoryStream();
                    patchStream.CopyTo(seekablePatchStream);
                    seekablePatchStream.Position = 0;
                    Func<Stream> openPatchStreamFunc = () => new MemoryStream(seekablePatchStream.ToArray());
                    BinaryPatch.Apply(basisStream, openPatchStreamFunc, newFileStream);
                }
                File.Copy(tempNewFilePath, basisFilePath, true);
            }
            finally
            {
                if (File.Exists(tempNewFilePath)) File.Delete(tempNewFilePath);
            }
        }
    }
}