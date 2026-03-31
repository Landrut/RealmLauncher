using System.Collections.Generic;

namespace RealmLauncher.Services
{
    public static class AppRuntimeConfig
    {
        public const int DefaultQueryPort = 27015;
        public static readonly bool RequireSignedUpdateManifest = true;
        public const string UpdateManifestSignaturePublicKeyXml =
            "<RSAKeyValue><Modulus>zIAxrvteAI45CCVrfV/Gk7cWcnBR7KDRVPC1oaQUc+KRcHzBEZyJ2ya9zRUp6PE5UfAjh1GeB4C19uIRzftU56BKIz6Dfb7H0kIf/Dw9X5a1QWO+lgPolMgLRCIiucVnYsfa/muB2DhAtylcA9F6tiDzSBI86/sjtJv6uyV1UqFD/h+q+kEieScDFt5s3udwHmgCzmRDPLiCWHMycU111ivv+bykeNGwc9loesa7sz7e0H2PgO+t1dvM4OQCkprYuyXVLuPS2T+XUoAYp+q2zKz/lnwBsh8r99a4qPPjA+ZQvzJJ+g+5k/RiJCzW3IZapqIT0vkBNfaoDvV3ubrTavxYcx5J4MDIAApEE8uNK/6rOQ51hdIoauUKf9gl5EkVMC2WWHNx2t68UILq6D3WBSfMkPbqkv4k5T+GH8vIHrDufZEIr5Pmas+Za/hBexUlXbN6NorhrzGFEFCMy1gOEXzNrbH96VfaLxT4tZfyWjF5bFFt6GfzvF/fEird4fNZ</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
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
            "discord.gg",
            "discord.com"
        };

        public static HashSet<string> BuildAllowedHosts()
        {
            return UrlSecurity.LoadAllowedHosts(AllowedRemoteHosts);
        }
    }
}
