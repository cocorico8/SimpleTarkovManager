using System.Collections.Generic;
using Newtonsoft.Json;

namespace SimpleTarkovManager.Models
{
    public class ConsistencyInfo
    {
        [JsonProperty("Version")]
        public string Version { get; set; }

        [JsonProperty("Entries")]
        public List<ConsistencyEntry> Entries { get; set; } = new();
    }

    public class ConsistencyEntry
    {
        [JsonProperty("Path")]
        public string Path { get; set; }

        [JsonProperty("Size")]
        public long Size { get; set; }

        [JsonProperty("Hash")]
        public string Hash { get; set; }
    }
}