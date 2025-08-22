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

                var path = Path.Combine(installationDirectory, downloadItem);
                if (!Directory.Exists(path))
                {
                    continue;
                }

                var files = Directory.GetFiles(path, "*.dll", SearchOption.AllDirectories);
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
            string packageInstallationDirectory = Path.Combine(installationDirectory, packageName);
            string packageFile = Path.Combine(packageInstallationDirectory, $"{packageName}.nuget");
            
            if (File.Exists(packageFile))
            {
                return;
            }

            if (!Directory.Exists(packageInstallationDirectory))
            {
                Directory.CreateDirectory(packageInstallationDirectory);
            }

            string url = $"https://packages.nuget.org/api/v2/package/{packageName}";
            if (!String.IsNullOrWhiteSpace(version))
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

                ZipFile.ExtractToDirectory(packageFile, packageInstallationDirectory, true);
            }
            catch (Exception ex)
            {
                // Log exception or handle it appropriately
                Console.WriteLine($"Error downloading package {packageName}: {ex.Message}");
            }
        }

        // Keep the synchronous version for backward compatibility
        public static void DownloadPackage(string packageName, string version)
        {
            DownloadPackageAsync(packageName, version).GetAwaiter().GetResult();
        }
    }
}
