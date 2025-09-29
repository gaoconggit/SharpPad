using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using System.Linq;

namespace MonacoRoslynCompletionProvider
{
    public static class NugetSourceManager
    {
        private static string _currentSource = "nuget"; // Default to nuget.org
        private static readonly object _lock = new object();
        private static Dictionary<string, NugetSource> _availableSources;

        static NugetSourceManager()
        {
            LoadSourcesFromConfig();
        }

        public static Dictionary<string, NugetSource> GetAvailableSources()
        {
            lock (_lock)
            {
                if (_availableSources == null)
                {
                    LoadSourcesFromConfig();
                }
                return new Dictionary<string, NugetSource>(_availableSources);
            }
        }

        public static string GetCurrentSource()
        {
            lock (_lock)
            {
                return _currentSource;
            }
        }

        public static void SetCurrentSource(string sourceKey)
        {
            lock (_lock)
            {
                if (_availableSources != null && _availableSources.ContainsKey(sourceKey))
                {
                    _currentSource = sourceKey;
                }
                else
                {
                    throw new ArgumentException($"Source key '{sourceKey}' not found in available sources.");
                }
            }
        }

        public static NugetSource GetCurrentSourceInfo()
        {
            lock (_lock)
            {
                if (_availableSources != null && _availableSources.TryGetValue(_currentSource, out var source))
                {
                    return source;
                }
                // Fallback to nuget.org if current source is not found
                return new NugetSource
                {
                    Key = "nuget",
                    Name = "nuget.org",
                    Url = "https://api.nuget.org/v3/index.json",
                    V2Url = "https://packages.nuget.org/api/v2",
                    SearchUrl = "https://azuresearch-usnc.nuget.org/query",
                    FlatContainerUrl = "https://api.nuget.org/v3-flatcontainer"
                };
            }
        }

        private static void LoadSourcesFromConfig()
        {
            _availableSources = new Dictionary<string, NugetSource>();

            try
            {
                var nugetConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "NuGet.config");
                if (!File.Exists(nugetConfigPath))
                {
                    // Add default source if no config file exists
                    AddDefaultSource();
                    return;
                }

                var doc = XDocument.Load(nugetConfigPath);
                var packageSources = doc.Root?.Element("packageSources");

                if (packageSources != null)
                {
                    foreach (var add in packageSources.Elements("add"))
                    {
                        var key = add.Attribute("key")?.Value;
                        var value = add.Attribute("value")?.Value;

                        if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                        {
                            var source = CreateSourceFromUrl(key, value);
                            _availableSources[key] = source;
                        }
                    }
                }

                // Ensure we have at least the default source
                if (!_availableSources.ContainsKey("nuget"))
                {
                    AddDefaultSource();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading NuGet.config: {ex.Message}");
                AddDefaultSource();
            }
        }

        private static void AddDefaultSource()
        {
            _availableSources["nuget"] = new NugetSource
            {
                Key = "nuget",
                Name = "nuget.org",
                Url = "https://api.nuget.org/v3/index.json",
                V2Url = "https://packages.nuget.org/api/v2",
                SearchUrl = "https://azuresearch-usnc.nuget.org/query",
                FlatContainerUrl = "https://api.nuget.org/v3-flatcontainer"
            };
        }

        private static NugetSource CreateSourceFromUrl(string key, string url)
        {
            string name;
            string v2Url;
            string searchUrl;
            string flatContainerUrl;

            // Map known sources to their proper endpoints
            if (url.Contains("mirrors.huaweicloud.com"))
            {
                name = "华为云镜像源";
                v2Url = "https://mirrors.huaweicloud.com/repository/nuget/v2";
                searchUrl = "https://mirrors.huaweicloud.com/repository/nuget/query";
                flatContainerUrl = "https://mirrors.huaweicloud.com/repository/nuget/v3-flatcontainer";
            }
            else if (url.Contains("nuget.cdn.azure.cn"))
            {
                name = "Azure 中国镜像源";
                v2Url = "https://nuget.cdn.azure.cn/v2";
                searchUrl = "https://nuget.cdn.azure.cn/query";
                flatContainerUrl = "https://nuget.cdn.azure.cn/v3-flatcontainer";
            }
            else if (url.Contains("api.nuget.org"))
            {
                name = "nuget.org";
                v2Url = "https://packages.nuget.org/api/v2";
                searchUrl = "https://azuresearch-usnc.nuget.org/query";
                flatContainerUrl = "https://api.nuget.org/v3-flatcontainer";
            }
            else
            {
                // Generic fallback for unknown sources
                name = key;
                v2Url = url.Replace("/v3/index.json", "/v2").Replace("/api/v3/index.json", "/api/v2");
                searchUrl = url.Replace("/v3/index.json", "/query").Replace("/api/v3/index.json", "/query");
                flatContainerUrl = url.Replace("/v3/index.json", "/v3-flatcontainer").Replace("/api/v3/index.json", "/v3-flatcontainer");
            }

            return new NugetSource
            {
                Key = key,
                Name = name,
                Url = url,
                V2Url = v2Url,
                SearchUrl = searchUrl,
                FlatContainerUrl = flatContainerUrl
            };
        }
    }

    public class NugetSource
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public string V2Url { get; set; }
        public string SearchUrl { get; set; }
        public string FlatContainerUrl { get; set; }
    }
}