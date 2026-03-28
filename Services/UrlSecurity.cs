using System;
using System.Collections.Generic;
using System.Linq;

namespace RealmLauncher.Services
{
    public static class UrlSecurity
    {
        public static HashSet<string> LoadAllowedHosts(IEnumerable<string> hosts)
        {
            var items = (hosts ?? Enumerable.Empty<string>())
                .Select(x => x ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x));

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
                throw new InvalidOperationException(label + ": URL is empty.");
            }

            Uri uri;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri))
            {
                throw new InvalidOperationException(label + ": invalid URL.");
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(label + ": only HTTPS is allowed.");
            }

            if (allowedHosts != null && allowedHosts.Count > 0 && !IsHostAllowed(uri.Host, allowedHosts))
            {
                throw new InvalidOperationException(label + ": host '" + uri.Host + "' is not in AllowedRemoteHosts.");
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
