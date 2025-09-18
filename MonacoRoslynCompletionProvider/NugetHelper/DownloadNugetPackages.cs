//using NuGet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO.Compression;

namespace monacoEditorCSharp.DataHelpers
{
    public static class DownloadNugetPackages
    {
        private static readonly string installationDirectory = Path.Combine(Directory.GetCurrentDirectory(), "NugetPackages", "packages");
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly char[] InvalidPathChars = Path.GetInvalidFileNameChars();

        static DownloadNugetPackages()
        {
            Directory.CreateDirectory(installationDirectory);
        }

        public static List<Assembly> LoadPackages(string packages)
        {
            List<Assembly> assemblies = new List<Assembly>();
            if (String.IsNullOrWhiteSpace(packages))
            {
                return assemblies;
            }

            string[] npackages = packages.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in npackages)
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                string[] parts = item.Contains(',') ? item.Split(',') : new[] { item, string.Empty };
                string downloadItem = parts[0].Trim();
                string version = parts.Length > 1 ? parts[1].Trim() : string.Empty;

                var packagePath = ResolvePackageDirectory(downloadItem, version);
                if (string.IsNullOrEmpty(packagePath) || !Directory.Exists(packagePath))
                {
                    continue;
                }

                var files = Directory.GetFiles(packagePath, "*.dll", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        var fileName = Path.GetFileName(file);
                        if (file.EndsWith(Path.Combine("net8.0", fileName)) ||
                            file.Contains(Path.Combine("netstandard2.0", fileName)) ||
                            file.Contains(Path.Combine("netstandard2.1", fileName)))
                        {
                            var assembly = Assembly.LoadFrom(file);
                            assemblies.Add(assembly);
                        }
                    }
                    catch (Exception)
                    {
                        // Log exception or handle it appropriately
                    }
                }
            }
            return assemblies;
        }

        public static async Task DownloadAllPackagesAsync(string packages)
        {
            if (String.IsNullOrWhiteSpace(packages))
            {
                return;
            }

            var tasks = new List<Task>();
            string[] npackages = packages.Split(';', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var item in npackages)
            {
                if (String.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                string[] parts = item.Contains(',') ? item.Split(',') : new[] { item, string.Empty };
                string downloadItem = parts[0].Trim();
                string version = parts.Length > 1 ? parts[1].Trim() : string.Empty;

                tasks.Add(DownloadPackageAsync(downloadItem, version));
            }

            await Task.WhenAll(tasks);
        }

        // Keep the synchronous version for backward compatibility
        public static void DownloadAllPackages(string packages)
        {
            DownloadAllPackagesAsync(packages).GetAwaiter().GetResult();
        }

        public static async Task DownloadPackageAsync(string packageName, string version)
        {
            if (string.IsNullOrWhiteSpace(packageName))
            {
                return;
            }

            bool versionSpecified = !string.IsNullOrWhiteSpace(version);

            string packageRootDirectory = GetPackageRootDirectory(packageName);
            string packageVersionDirectory = versionSpecified
                ? GetPackageVersionDirectory(packageName, version)
                : packageRootDirectory;

            if (HasAssemblies(packageVersionDirectory))
            {
                return;
            }

            Directory.CreateDirectory(packageRootDirectory);
            Directory.CreateDirectory(packageVersionDirectory);

            string packageFile = Path.Combine(packageVersionDirectory, BuildPackageFileName(packageName, version));

            string url = $"https://packages.nuget.org/api/v2/package/{packageName}";
            if (versionSpecified)
            {
                url += $"/{version}";
            }

            try
            {
                using (var response = await httpClient.GetAsync(url))
                {
                    response.EnsureSuccessStatusCode();
                    using (var fileStream = new FileStream(packageFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fileStream);
                    }
                }

                ZipFile.ExtractToDirectory(packageFile, packageVersionDirectory, true);
            }
            catch (Exception ex)
            {
                // Log exception or handle it appropriately
                Console.WriteLine($"Error downloading package {packageName}: {ex.Message}");

                if (!HasAssemblies(packageVersionDirectory))
                {
                    TryDeleteFile(packageFile);
                    if (versionSpecified)
                    {
                        TryDeleteDirectory(packageVersionDirectory);
                    }
                }
            }
        }

        // Keep the synchronous version for backward compatibility
        public static void DownloadPackage(string packageName, string version)
        {
            DownloadPackageAsync(packageName, version).GetAwaiter().GetResult();
        }

        private static bool HasAssemblies(string directory)
        {
            try
            {
                return Directory.Exists(directory) &&
                       Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories).Any();
            }
            catch
            {
                return false;
            }
        }

        private static string BuildPackageFileName(string packageName, string version)
        {
            string safePackage = SanitizePathSegment(packageName, "package");
            if (string.IsNullOrWhiteSpace(version))
            {
                return $"{safePackage}.nuget";
            }

            string safeVersion = SanitizePathSegment(version, "version");
            return $"{safePackage}.{safeVersion}.nuget";
        }

        private static string GetPackageRootDirectory(string packageName)
        {
            string safePackage = SanitizePathSegment(packageName, "package");
            return Path.Combine(installationDirectory, safePackage);
        }

        private static string GetPackageVersionDirectory(string packageName, string version)
        {
            string packageRoot = GetPackageRootDirectory(packageName);
            string safeVersion = SanitizePathSegment(version, "version");
            return Path.Combine(packageRoot, safeVersion);
        }

        private static string ResolvePackageDirectory(string packageName, string version)
        {
            if (string.IsNullOrWhiteSpace(packageName))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(version))
            {
                var versionDirectory = GetPackageVersionDirectory(packageName, version);
                if (Directory.Exists(versionDirectory))
                {
                    return versionDirectory;
                }

                return null;
            }

            var packageRoot = GetPackageRootDirectory(packageName);
            if (!Directory.Exists(packageRoot))
            {
                return null;
            }

            if (HasAssemblies(packageRoot))
            {
                return packageRoot;
            }

            var latestVersionDirectory = Directory.GetDirectories(packageRoot)
                .OrderByDescending(dir => Directory.GetLastWriteTimeUtc(dir))
                .FirstOrDefault();

            return latestVersionDirectory;
        }

        private static string SanitizePathSegment(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var trimmed = value.Trim();
            var sanitizedChars = trimmed.Select(ch => InvalidPathChars.Contains(ch) ? '_' : ch).ToArray();
            var sanitized = new string(sanitizedChars);

            return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
        }

        private static void TryDeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch
            {
                // Swallow cleanup exceptions
            }
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
                // Swallow cleanup exceptions
            }
        }
    }
}
