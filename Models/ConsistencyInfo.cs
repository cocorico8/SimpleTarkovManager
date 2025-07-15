using System.Collections.Generic;
using Newtonsoft.Json;

namespace SimpleTarkovManager.Models
{
    public class ConsistencyInfo
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("entries")]
        public List<ConsistencyEntry> Entries { get; set; } = new();
    }

    public class ConsistencyEntry
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("hash")]
        public string Hash { get; set; }
    }
}