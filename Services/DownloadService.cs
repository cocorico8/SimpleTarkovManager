using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SimpleTarkovManager.Models;

namespace SimpleTarkovManager.Services
{
    /// <summary>
    /// The original, simple, single-threaded and reliable download service.
    /// </summary>
    public class DownloadService : IDownloadService
    {
        // Use the Microsoft-recommended best practice of a single, shared HttpClient.
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        
        public event Action<DownloadProgress> ProgressChanged;

        // The 'workerLimit' parameter is ignored here, as this is a single-threaded downloader.
        // It exists only to satisfy the IDownloadService interface.
        public async Task DownloadFileAsync(IEnumerable<string> baseUris, string relativeUri, string destinationPath, int workerLimit, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

            foreach (var baseUri in baseUris)
            {
                var fullUri = new Uri(new Uri(baseUri), relativeUri);
                try
                {
                    using var response = await _httpClient.GetAsync(fullUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? 0L;
                    using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                    
                    var totalRead = 0L;
                    var buffer = new byte[8192];
                    var stopwatch = Stopwatch.StartNew();
                    long lastReportedBytes = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                        totalRead += bytesRead;

                        if (stopwatch.Elapsed.TotalSeconds >= 1 || totalRead == totalBytes)
                        {
                            var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                            var speed = elapsedSeconds > 0 ? (totalRead - lastReportedBytes) / elapsedSeconds : 0;
                            var eta = speed > 0 ? TimeSpan.FromSeconds((totalBytes - totalRead) / speed) : TimeSpan.Zero;
                            
                            ProgressChanged?.Invoke(new DownloadProgress
                            {
                                BytesDownloaded = totalRead,
                                TotalBytes = totalBytes,
                                SpeedBytesPerSecond = speed,
                                ETA = eta
                            });
                            
                            stopwatch.Restart();
                            lastReportedBytes = totalRead;
                        }
                    }
                    return; // Success!
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
#if DEBUG
                    Console.WriteLine($"[SimpleDownloader] Failed to download from {fullUri}: {ex.Message}. Trying next server.");
#endif
                }
            }
            throw new Exception("Failed to download file from all available servers.");
        }
    }
}