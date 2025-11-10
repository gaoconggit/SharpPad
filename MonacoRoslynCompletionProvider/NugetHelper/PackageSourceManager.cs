using System;
using System.Collections.Generic;
using System.Linq;

namespace monacoEditorCSharp.DataHelpers
{
    public class PackageSourceManager
    {
        private static readonly Dictionary<string, PackageSource> _packageSources = new()
        {
            {
                "nuget",
                new PackageSource
                {
                    Key = "nuget",
                    Name = "NuGet.org",
                    Url = "https://packages.nuget.org/api/v2/package",
                    SearchUrl = "https://azuresearch-usnc.nuget.org/query",
                    ApiUrl = "https://api.nuget.org/v3-flatcontainer",
                    ServiceIndexUrl = "https://api.nuget.org/v3/index.json",
                    IsDefault = true,
                    IsEnabled = true
                }
            },
            {
                "azure",
                new PackageSource
                {
                    Key = "azure",
                    Name = "Azure CDN",
                    Url = "https://nuget.cdn.azure.cn/api/v2/package",
                    SearchUrl = "https://azuresearch-usnc.nuget.org/query",
                    ApiUrl = "https://nuget.cdn.azure.cn/v3-flatcontainer",
                    ServiceIndexUrl = "https://nuget.cdn.azure.cn/v3/index.json",
                    IsDefault = false,
                    IsEnabled = true
                }
            },
            {
                "huawei",
                new PackageSource
                {
                    Key = "huawei",
                    Name = "Huawei Cloud",
                    Url = "https://mirrors.huaweicloud.com/repository/nuget/v2/package",
                    SearchUrl = "https://azuresearch-usnc.nuget.org/query",
                    ApiUrl = "https://mirrors.huaweicloud.com/repository/nuget/v3-flatcontainer",
                    ServiceIndexUrl = "https://mirrors.huaweicloud.com/repository/nuget/v3/index.json",
                    IsDefault = false,
                    IsEnabled = true
                }
            }
        };

        public static IEnumerable<PackageSource> GetAvailableSources()
        {
            return _packageSources.Values.Where(s => s.IsEnabled);
        }

        public static PackageSource GetSource(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return GetDefaultSource();
            }

            _packageSources.TryGetValue(key.ToLowerInvariant(), out var source);
            return source ?? GetDefaultSource();
        }

        public static PackageSource GetDefaultSource()
        {
            return _packageSources.Values.FirstOrDefault(s => s.IsDefault)
                   ?? _packageSources.Values.First();
        }

        public static IReadOnlyList<PackageSource> GetOrderedSources(string preferredSourceKey = null)
        {
            var preferred = GetSource(preferredSourceKey);
            var available = GetAvailableSources()
                .Where(s => s.IsEnabled)
                .ToList();

            if (preferred == null)
            {
                return available;
            }

            var ordered = new List<PackageSource> { preferred };
            ordered.AddRange(available.Where(s => !string.Equals(s.Key, preferred.Key, StringComparison.OrdinalIgnoreCase)));
            return ordered;
        }

        public static void AddCustomSource(string key, string name, string url, string searchUrl = null, string apiUrl = null, string serviceIndexUrl = null)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("Key and URL are required for custom package sources");
            }

            var customSource = new PackageSource
            {
                Key = key.ToLowerInvariant(),
                Name = name ?? key,
                Url = url,
                SearchUrl = searchUrl ?? "https://azuresearch-usnc.nuget.org/query",
                ApiUrl = apiUrl ?? $"{url.TrimEnd('/')}/v3-flatcontainer",
                ServiceIndexUrl = serviceIndexUrl ?? DeriveServiceIndexUrl(apiUrl, url),
                IsDefault = false,
                IsEnabled = true,
                IsCustom = true
            };

            _packageSources[customSource.Key] = customSource;
        }

        public static bool RemoveCustomSource(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            var lowerKey = key.ToLowerInvariant();
            if (_packageSources.TryGetValue(lowerKey, out var source) && source.IsCustom)
            {
                return _packageSources.Remove(lowerKey);
            }

            return false;
        }

        public static string[] GetSourceUrls(string preferredSourceKey = null)
        {
            return GetOrderedSources(preferredSourceKey)
                .Select(s => s.Url)
                .ToArray();
        }

        public static string? GetServiceIndexUrl(PackageSource source)
        {
            if (source == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(source.ServiceIndexUrl))
            {
                return source.ServiceIndexUrl;
            }

            return DeriveServiceIndexUrl(source.ApiUrl, source.Url);
        }

        private static string? DeriveServiceIndexUrl(string apiUrl, string fallbackUrl)
        {
            if (!string.IsNullOrWhiteSpace(apiUrl))
            {
                var trimmed = apiUrl.TrimEnd('/');
                if (trimmed.EndsWith("v3-flatcontainer", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{trimmed[..^"v3-flatcontainer".Length]}v3/index.json";
                }

                if (trimmed.EndsWith("/index.json", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed;
                }
            }

            if (!string.IsNullOrWhiteSpace(fallbackUrl))
            {
                var trimmed = fallbackUrl.TrimEnd('/');
                if (trimmed.EndsWith("/api/v2/package", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{trimmed[..^"/api/v2/package".Length]}/v3/index.json";
                }
            }

            return null;
        }
    }

    public class PackageSource
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public string SearchUrl { get; set; }
        public string ApiUrl { get; set; }
        public string ServiceIndexUrl { get; set; }
        public bool IsDefault { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsCustom { get; set; }
    }
}
