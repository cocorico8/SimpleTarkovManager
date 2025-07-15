using Newtonsoft.Json;

namespace SimpleTarkovManager.Models
{
    public class GameInstallInfo
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("hash")]
        public string Hash { get; set; }

        [JsonProperty("downloadUri")]
        public string DownloadUri { get; set; }
        
        [JsonProperty("unpackedUri")]
        public string UnpackedUri { get; set; }

        [JsonProperty("requiredFreeSpace")]
        public double RequiredFreeSpace { get; set; }
    }
}