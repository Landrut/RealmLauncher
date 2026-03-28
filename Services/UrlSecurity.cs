using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace RealmLauncher.Services
{
    public static class UrlSecurity
    {
        private static readonly string[] DefaultHosts =
        {
            "gist.githubusercontent.com",
            "github.com",
            "api.steampowered.com",
            "steamcdn-a.akamaihd.net",
            "discord.gg"
        };

        public static HashSet<string> LoadAllowedHostsFromConfig()
        {
            var raw = ConfigurationManager.AppSettings["AllowedRemoteHosts"];
            var items = string.IsNullOrWhiteSpace(raw)
                ? DefaultHosts
                : raw.Split(new[] { ',', ';', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            return new HashSet<string>(
                items
                    .Select(x => x.Trim().ToLowerInvariant())
                    .Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
        }

        public static Uri RequireAllowedHttpsUrl(string url, ISet<string> allowedHosts, string label)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException(label + ": ссылка не задана.");
            }

            Uri uri;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri))
            {
                throw new InvalidOperationException(label + ": некорректный URL.");
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(label + ": разрешен только HTTPS.");
            }

            if (allowedHosts != null && allowedHosts.Count > 0 && !IsHostAllowed(uri.Host, allowedHosts))
            {
                throw new InvalidOperationException(
                    label + ": хост '" + uri.Host + "' не входит в список AllowedRemoteHosts.");
            }

            return uri;
        }

        private static bool IsHostAllowed(string host, IEnumerable<string> allowedHosts)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            foreach (var item in allowedHosts)
            {
                var allowed = (item ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(allowed))
                {
                    continue;
                }

                if (host.Equals(allowed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
