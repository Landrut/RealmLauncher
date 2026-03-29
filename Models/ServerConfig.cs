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

        public ServerConfig()
        {
            Mods = new List<string>();
        }
    }
}
