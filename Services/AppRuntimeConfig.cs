using System.Collections.Generic;

namespace RealmLauncher.Services
{
    public static class AppRuntimeConfig
    {
        public const string ServerConfigUrl = "https://gist.githubusercontent.com/Landrut/b14952dd01f52b9267c1bad84faedc75/raw/realm-roleplay.json";
        public const string DiscordInviteUrl = "https://discord.gg/Vw26qw4spu";
        public const string UpdateManifestUrl = "https://gist.githubusercontent.com/Landrut/052e98e840a62835505698c8d409b1fd/raw/update-manifest.json";
        public const string NewsFeedUrl = "https://gist.githubusercontent.com/Landrut/17bf997f7774832f976296f2f136a577/raw/news-feed.json";

        public static readonly string[] AllowedRemoteHosts =
        {
            "gist.githubusercontent.com",
            "github.com",
            "api.steampowered.com",
            "steamcdn-a.akamaihd.net",
            "discord.gg"
        };

        public static HashSet<string> BuildAllowedHosts()
        {
            return UrlSecurity.LoadAllowedHosts(AllowedRemoteHosts);
        }
    }
}
