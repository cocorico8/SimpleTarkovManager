using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimpleTarkovManager.Models;

namespace SimpleTarkovManager.Services
{
    public interface IDownloadService
    {
        event Action<DownloadProgress> ProgressChanged;

        Task DownloadFileAsync(IEnumerable<string> baseUris, string relativeUri, string destinationPath, int workerLimit, CancellationToken cancellationToken);
    }
}