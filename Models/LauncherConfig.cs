using System.Collections.Generic;
using Newtonsoft.Json;

namespace SimpleTarkovManager.Models
{
    public class LauncherConfig
    {
        [JsonProperty("account")]
        public Account Account { get; set; }

        [JsonProperty("games")]
        public List<Game> Games { get; set; }

        [JsonProperty("channels")]
        public Channels Channels { get; set; }
    }

    public class Account
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("nickname")]
        public string Nickname { get; set; }
    }

    public class Game
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("branches")]
        public List<Branch> Branches { get; set; }
    }

    public class Branch
    {
        [JsonProperty("isDefault")]
        public bool IsDefault { get; set; }

        [JsonProperty("isActive")]
        public bool IsActive { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("backendUri")]
        public string BackendUri { get; set; }
    }

    public class Channels
    {
        [JsonProperty("settings")]
        public ChannelsSettings Settings { get; set; }
        
        [JsonProperty("instances")]
        public List<ChannelInstance> Instances { get; set; }
    }
    
    public class ChannelsSettings
    {
        [JsonProperty("spareNodeActivationThreshold")]
        public int SpareNodeActivationThreshold { get; set; }

        [JsonProperty("spareNodeThresholdTimeoutSec")]
        public int SpareNodeThresholdTimeoutSec { get; set; }

        [JsonProperty("simultaneouslyUsedChannelsLimit")]
        public int SimultaneouslyUsedChannelsLimit { get; set; }
    }

    public class ChannelInstance
    {
        [JsonProperty("endpoint")]
        public string Endpoint { get; set; }
        
        [JsonProperty("isSpare")]
        public bool IsSpare { get; set; }
    }
}