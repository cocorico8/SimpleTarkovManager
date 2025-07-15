using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleTarkovManager.Services
{
    public class DownloadProgress
    {
        public long TotalBytes { get; set; }
        public long BytesDownloaded { get; set; }
        public double SpeedBytesPerSecond { get; set; }
        public TimeSpan ETA { get; set; }
        public double Percentage => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100 : 0;
    }

public class DownloadService
    {
        private readonly HttpClient _httpClient;

        public DownloadService() { _httpClient = new HttpClient(); }
        
        public async Task DownloadFileAsync(IEnumerable<string> baseUris, string relativeUri, string destinationPath, Action<DownloadProgress> progressAction, CancellationToken cancellationToken)
        {
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

                        if (stopwatch.Elapsed.TotalSeconds >= 1)
                        {
                            var speed = (totalRead - lastReportedBytes) / stopwatch.Elapsed.TotalSeconds;
                            var eta = speed > 0 ? TimeSpan.FromSeconds((totalBytes - totalRead) / speed) : TimeSpan.Zero;
                            var report = new DownloadProgress { TotalBytes = totalBytes, BytesDownloaded = totalRead, SpeedBytesPerSecond = speed, ETA = eta };
                            
                            // The service is now responsible for using the dispatcher
                            Dispatcher.UIThread.Invoke(() => progressAction(report));
                            
                            stopwatch.Restart();
                            lastReportedBytes = totalRead;
                        }
                    }

                    var finalReport = new DownloadProgress { TotalBytes = totalBytes, BytesDownloaded = totalBytes };
                    Dispatcher.UIThread.Invoke(() => progressAction(finalReport));
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Console.WriteLine($"Failed to download from {fullUri}: {ex.Message}. Trying next server."); }
            }
            throw new Exception("Failed to download file from all available servers.");
        }
    }
}