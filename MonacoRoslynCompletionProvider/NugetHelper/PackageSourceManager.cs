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

        public static void AddCustomSource(string key, string name, string url, string searchUrl = null, string apiUrl = null)
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
            var preferredSource = GetSource(preferredSourceKey);
            var allSources = GetAvailableSources().Where(s => s.IsEnabled).ToList();

            // Put preferred source first, then others
            var orderedSources = new List<PackageSource>();
            if (preferredSource != null)
            {
                orderedSources.Add(preferredSource);
                orderedSources.AddRange(allSources.Where(s => s.Key != preferredSource.Key));
            }
            else
            {
                orderedSources.AddRange(allSources);
            }

            return orderedSources.Select(s => s.Url).ToArray();
        }
    }

    public class PackageSource
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public string SearchUrl { get; set; }
        public string ApiUrl { get; set; }
        public bool IsDefault { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsCustom { get; set; }
    }
}