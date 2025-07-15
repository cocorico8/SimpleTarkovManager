using System.Collections.Generic;
using Newtonsoft.Json;

namespace SimpleTarkovManager.Models
{
    public class UpdateManifest
    {
        [JsonProperty("Files")]
        public List<UpdateFileEntry> Files { get; set; } = new();
    }

    public class UpdateFileEntry
    {
        [JsonProperty("Path")]
        public string Path { get; set; }

        [JsonProperty("State")]
        public int State { get; set; } // 1: Modified, 2: New, 3: Deleted

        [JsonProperty("PatchAlgorithmId")]
        public int PatchAlgorithmId { get; set; } // 1 for BsDiff, 0 for fastrsync
    }
}