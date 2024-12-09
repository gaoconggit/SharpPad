//using NuGet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace monacoEditorCSharp.DataHelpers
{
    public static class DownloadNugetPackages
    {
        private static string installationDirectory = Directory.GetCurrentDirectory() + "//NugetPackages//packages";

        public static List<Assembly> LoadPackages(string packages)
        {
            List<Assembly> assemblies = new List<Assembly>();
            if (!String.IsNullOrWhiteSpace(packages))
            {

                string[] npackages = packages.Split(';');
                foreach (var item in npackages)
                {
                    if (item.Trim() == "")
                    {
                        continue;
                    }
                    string downloadItem = "";
                    string version = "";
                    if (item.Contains(','))
                    {
                        downloadItem = item.Split(',')[0];
                        version = item.Split(',')[1];
                    }
                    else
                    {
                        downloadItem = item;

                    }

                    downloadItem = downloadItem.Trim();
                    version = version.Trim();

                    var path = $"{installationDirectory}//{downloadItem}";

                    var files = Directory.GetFiles(path, "*.dll", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try
                        {
                            if (file.EndsWith(@$"net8.0\{downloadItem}.dll") || file.Contains(@$"netstandard2.0\{downloadItem}.dll"))
                            {
                                var assembly = Assembly.LoadFrom(file);
                                assemblies.Add(assembly);
                            }
                        }
                        catch (Exception)
                        {
                            // throw
                        }
                    }

                }
            }
            return assemblies;
        }
        
        public static void DownloadAllPackages(string packages)
        {
            if (!String.IsNullOrWhiteSpace(packages))
            {
                string[] npackages = packages.Split(';');
                foreach (var item in npackages)
                {
                    if (!String.IsNullOrWhiteSpace(item))
                    {
                        string downloadItem = "";
                        string version = "";
                        if (item.Contains(','))
                        {
                            downloadItem = item.Split(',')[0];
                            version = item.Split(',')[1];
                        }
                        else
                        {
                            downloadItem = item;

                        }

                        downloadItem = downloadItem.Trim();
                        version = version.Trim();
                        if (!String.IsNullOrWhiteSpace(version))
                        {
                            DownloadPackage(downloadItem, version);
                        }
                        else
                        {
                            DownloadPackage(downloadItem, null);
                        }
                    }
                }
            }
        }
        public static void DownloadPackage(string packageName, string version)
        {
            string packageInstallationDirectory = installationDirectory + $"//{packageName}";
            if (!System.IO.File.Exists($"{packageInstallationDirectory}//{packageName}.nuget"))
            {
                if (!System.IO.Directory.Exists(packageInstallationDirectory))
                {
                    System.IO.Directory.CreateDirectory(packageInstallationDirectory);
                }
                //https://www.nuget.org/api/v2/package/NuGet.Core/2.14.0
                string url = "https://packages.nuget.org/api/v2/package/" + $"{packageName}";
                if (!String.IsNullOrWhiteSpace(version))
                {
                    url += $"/{version}";
                }


                var wc = new System.Net.WebClient();
                wc.DownloadFile(url, $"{packageInstallationDirectory}//{packageName}.nuget");

                System.IO.Compression.ZipFile.ExtractToDirectory($"{packageInstallationDirectory}//{packageName}.nuget", packageInstallationDirectory, true);

            }

        }
    }
}
