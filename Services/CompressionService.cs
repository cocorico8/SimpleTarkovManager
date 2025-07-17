using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleTarkovManager.Services
{
    public class CompressionService
    {
        public Task ExtractSingleFileAsync(string zipPath, string fileEntryName, string destinationPath)
        {
            return Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.GetEntry(fileEntryName);
                if (entry == null)
                {
                    throw new FileNotFoundException($"Entry '{fileEntryName}' not found in zip archive.");
                }
                entry.ExtractToFile(destinationPath, true);
            });
        }

        /// <summary>
        /// Extracts a zip archive to a specified directory while reporting progress.
        /// </summary>
        /// <param name="zipPath">The path to the zip archive.</param>
        /// <param name="destinationDirectory">The directory to extract the files to.</param>
        /// <param name="progressAction">An action to be called with the progress percentage (0-100).</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        public Task ExtractArchiveWithProgressAsync(string zipPath, string destinationDirectory, Action<double> progressAction, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var totalEntries = (double)archive.Entries.Count;
                if (totalEntries == 0)
                {
                    progressAction?.Invoke(100);
                    return;
                }

                var entriesProcessed = 0;

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));

                    // Prevent "Zip Slip" vulnerability
                    if (!destinationPath.StartsWith(Path.GetFullPath(destinationDirectory), StringComparison.Ordinal))
                    {
                        throw new IOException("Attempting to extract file outside of destination directory.");
                    }

                    if (string.IsNullOrEmpty(entry.Name)) // This is a directory entry
                    {
                        Directory.CreateDirectory(destinationPath);
                    }
                    else // This is a file entry
                    {
                        // Ensure the directory for the file exists
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                        entry.ExtractToFile(destinationPath, true);
                    }

                    entriesProcessed++;
                    var percentage = (entriesProcessed / totalEntries) * 100.0;
                    progressAction?.Invoke(percentage);
                }
            }, cancellationToken);
        }
    }
}