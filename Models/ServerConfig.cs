using System.Collections.Generic;
using Newtonsoft.Json;

namespace RealmLauncher.Models
{
    public sealed class ServerConfig
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("ip")]
        public string Ip { get; set; }

        [JsonProperty("query_port")]
        public int? QueryPort { get; set; }

        [JsonProperty("mods")]
        public List<string> Mods { get; set; }

        [JsonProperty("password_sha256")]
        public string PasswordSha256 { get; set; }

        [JsonProperty("password")]
        public string Password { get; set; }

        [JsonProperty("game_password")]
        public string GamePassword { get; set; }

        [JsonProperty("links")]
        public List<ServerLink> Links { get; set; }

        [JsonProperty("discord")]
        public DiscordPresenceConfig Discord { get; set; }

        public ServerConfig()
        {
            Mods = new List<string>();
            Links = new List<ServerLink>();
        }
    }

    public sealed class DiscordPresenceConfig
    {
        [JsonProperty("details_idle")]
        public string DetailsIdle { get; set; }

        [JsonProperty("details_playing")]
        public string DetailsPlaying { get; set; }

        [JsonProperty("large_image")]
        public string LargeImage { get; set; }

        [JsonProperty("large_text")]
        public string LargeText { get; set; }
    }

    public sealed class ServerLink
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }
    }
}
