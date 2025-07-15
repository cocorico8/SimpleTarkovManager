using System.IO;
using System.IO.Compression;
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
    }
}