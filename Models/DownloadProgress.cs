using System;

namespace SimpleTarkovManager.Models
{
    /// <summary>
    /// Represents the state of a file download operation at a specific moment in time.
    /// </summary>
    public class DownloadProgress
    {
        public long TotalBytes { get; set; }
        public long BytesDownloaded { get; set; }
        public double SpeedBytesPerSecond { get; set; }
        public TimeSpan ETA { get; set; }
        public double Percentage => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100 : 0;
    }
}