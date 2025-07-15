using Newtonsoft.Json;
using System.Collections.Generic;

namespace SimpleTarkovManager.Models
{
    // Represents a single patch available on the server
    public class GameUpdate
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("fromVersion")]
        public string FromVersion { get; set; }

        [JsonProperty("downloadUri")]
        public string DownloadUri { get; set; }
    }

    // Represents the chain of patches we need to apply
    public class UpdateSet
    {
        public List<GameUpdate> Patches { get; set; } = new();
        public EftVersion TargetVersion { get; set; }
    }
}