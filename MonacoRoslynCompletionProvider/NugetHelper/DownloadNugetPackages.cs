//using NuGet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace monacoEditorCSharp.DataHelpers
{
    public static class DownloadNugetPackages
    {
        private static readonly string installationDirectory;
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly char[] InvalidPathChars = Path.GetInvalidFileNameChars();
        private static readonly HashSet<string> LibraryExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".dll",
            ".so",
            ".dylib",
            ".a",
            ".targets",
            ".props"
        };

        private static readonly string[] PreferredTfms =
        {
            "net9.0",
            "net8.0",
            "net7.0",
            "net6.0",
            "net5.0",
            "netcoreapp3.1",
            "netcoreapp3.0",
            "netstandard2.1",
            "netstandard2.0"
        };

        private static readonly HashSet<string> PreferredRuntimeIdentifiers = BuildPreferredRuntimeIdentifiers();

        static DownloadNugetPackages()
        {
            installationDirectory = InitializeInstallationDirectory();
        }

        public static List<PackageAssemblyInfo> LoadPackages(string packages)
        {
            var assemblies = new List<PackageAssemblyInfo>();
            if (string.IsNullOrWhiteSpace(packages))
            {
                return assemblies;
            }

            var assembliesByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
                        if (file.IndexOf($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            file.IndexOf($"{Path.AltDirectorySeparatorChar}ref{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            continue;
                        }

                        var fileName = Path.GetFileName(file);
                        if (!file.EndsWith(Path.Combine("net8.0", fileName)) &&
                            !file.Contains(Path.Combine("netstandard2.0", fileName)) &&
                            !file.Contains(Path.Combine("netstandard2.1", fileName)))
                        {
                            continue;
                        }

                        var assemblyName = AssemblyName.GetAssemblyName(file);
                        var info = new PackageAssemblyInfo(file, assemblyName);
                        var key = assemblyName.Name ?? Path.GetFileNameWithoutExtension(file);

                        if (assembliesByName.TryGetValue(key, out var index))
                        {
                            var existing = assemblies[index];
                            var existingVersion = existing.AssemblyName.Version;
                            if (existingVersion == null ||
                                (assemblyName.Version != null && assemblyName.Version > existingVersion))
                            {
                                assemblies[index] = info;
                            }
                        }
                        else
                        {
                            assembliesByName[key] = assemblies.Count;
                            assemblies.Add(info);
                        }
                    }
                    catch
                    {
                        // Ignore individual file failures; continue probing remaining files
                    }
                }
            }

            return assemblies;
        }

        public static async Task DownloadAllPackagesAsync(string packages, string preferredSourceKey = null)
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

                tasks.Add(DownloadPackageAsync(downloadItem, version, preferredSourceKey));
            }

            await Task.WhenAll(tasks);
        }

        // Keep the synchronous version for backward compatibility
        public static void DownloadAllPackages(string packages)
        {
            DownloadAllPackagesAsync(packages).GetAwaiter().GetResult();
        }

        public static async Task DownloadPackageAsync(string packageName, string version, string preferredSourceKey = null)
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

            // Get available source URLs with preferred source first
            var sourceUrls = PackageSourceManager.GetSourceUrls(preferredSourceKey);

            Exception lastException = null;
            bool downloadSuccessful = false;

            foreach (var baseUrl in sourceUrls)
            {
                try
                {
                    string url = $"{baseUrl.TrimEnd('/')}/{packageName}";
                    if (versionSpecified)
                    {
                        url += $"/{version}";
                    }

                    using (var response = await httpClient.GetAsync(url))
                    {
                        response.EnsureSuccessStatusCode();
                        using (var fileStream = new FileStream(packageFile, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await response.Content.CopyToAsync(fileStream);
                        }
                    }

                    ExtractPackageArchive(packageFile, packageVersionDirectory);
                    downloadSuccessful = true;
                    break; // Success, no need to try other sources
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Console.WriteLine($"Failed to download {packageName} from {baseUrl}: {ex.Message}");
                    TryDeleteFile(packageFile); // Clean up partial download
                    // Continue to next source
                }
            }

            if (!downloadSuccessful)
            {
                Console.WriteLine($"Error downloading package {packageName} from all sources: {lastException?.Message}");

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

        private static string InitializeInstallationDirectory()
        {
            var candidatePaths = new List<string>();

            var configuredPath = Environment.GetEnvironmentVariable("SHARPPAD_NUGET_CACHE");
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                candidatePaths.Add(configuredPath);
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                candidatePaths.Add(Path.Combine(localAppData, "SharpPad", "NugetPackages", "packages"));
            }

            var baseDirectory = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                candidatePaths.Add(Path.Combine(baseDirectory, "NugetPackages", "packages"));
            }

            candidatePaths.Add(Path.Combine(Path.GetTempPath(), "SharpPad", "NugetPackages", "packages"));

            foreach (var path in candidatePaths
                         .Select(TryNormalizePath)
                         .Where(p => !string.IsNullOrWhiteSpace(p))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    Directory.CreateDirectory(path);
                    return path;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to initialize NuGet cache at {path}: {ex.Message}");
                }
            }

            throw new InvalidOperationException("Unable to initialize NuGet package cache directory for SharpPad.");
        }

        private static string TryNormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }
        }

        private static bool HasAssemblies(string directory)
        {
            try
            {
                return Directory.Exists(directory) &&
                       Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                           .Any(file =>
                           {
                               var extension = Path.GetExtension(file);
                               return !string.IsNullOrEmpty(extension) && LibraryExtensions.Contains(extension);
                           });
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

        public static string? TryGetPackageDirectory(string packageName, string version = null)
        {
            if (string.IsNullOrWhiteSpace(packageName))
            {
                return null;
            }

            try
            {
                var resolved = ResolvePackageDirectory(packageName, version);
                if (string.IsNullOrWhiteSpace(resolved) || !Directory.Exists(resolved))
                {
                    return null;
                }

                var packageRoot = GetPackageRootDirectory(packageName);
                if (string.Equals(resolved, packageRoot, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var latest = Directory.GetDirectories(resolved)
                            .Select(dir => new DirectoryInfo(dir))
                            .Where(info => info.Exists)
                            .OrderByDescending(info => info.LastWriteTimeUtc)
                            .Select(info => info.FullName)
                            .FirstOrDefault(dir => HasAssemblies(dir) || Directory.Exists(Path.Combine(dir, "runtimes")));

                        if (!string.IsNullOrEmpty(latest))
                        {
                            return latest;
                        }
                    }
                    catch
                    {
                        // Ignore enumeration failures and fall back to resolved path.
                    }
                }

                return resolved;
            }
            catch
            {
                return null;
            }
        }

        private static void ExtractPackageArchive(string packageFile, string destinationDirectory)
        {
            if (TryExtractPackageSelective(packageFile, destinationDirectory))
            {
                return;
            }

            TryDeleteDirectory(destinationDirectory);
            Directory.CreateDirectory(destinationDirectory);
            ZipFile.ExtractToDirectory(packageFile, destinationDirectory, true);
        }

        private static bool TryExtractPackageSelective(string packageFile, string destinationDirectory)
        {
            try
            {
                Directory.CreateDirectory(destinationDirectory);
                var basePath = Path.GetFullPath(destinationDirectory);
                var extractedAny = false;

                using var archive = ZipFile.OpenRead(packageFile);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }

                    if (!ShouldExtractEntry(entry))
                    {
                        continue;
                    }

                    var normalizedPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    var targetPath = Path.Combine(destinationDirectory, normalizedPath);
                    var fullTargetPath = Path.GetFullPath(targetPath);
                    if (!fullTargetPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var directory = Path.GetDirectoryName(fullTargetPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    entry.ExtractToFile(fullTargetPath, overwrite: true);
                    extractedAny = true;
                }

                if (!extractedAny)
                {
                    return false;
                }

                return HasAssemblies(destinationDirectory) ||
                       Directory.EnumerateFiles(destinationDirectory, "*.*", SearchOption.AllDirectories).Any();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Selective extraction failed for {Path.GetFileName(packageFile)}: {ex.Message}");
                return false;
            }
        }

        private static bool ShouldExtractEntry(ZipArchiveEntry entry)
        {
            var fullName = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return false;
            }

            if (fullName.StartsWith("[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (fullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (fullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) ||
                fullName.StartsWith("ref/", StringComparison.OrdinalIgnoreCase))
            {
                var segments = fullName.Split('/');
                if (segments.Length < 3)
                {
                    return false;
                }

                return ShouldKeepTfm(segments[1]) && IsInterestingFile(entry.Name);
            }

            if (fullName.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase))
            {
                var segments = fullName.Split('/');
                if (segments.Length < 3)
                {
                    return false;
                }

                var rid = segments[1];
                if (!ShouldKeepRid(rid))
                {
                    return false;
                }

                if (segments.Length >= 4 &&
                    (segments[2].Equals("lib", StringComparison.OrdinalIgnoreCase) ||
                     segments[2].Equals("ref", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!ShouldKeepTfm(segments[3]))
                    {
                        return false;
                    }
                }

                return IsInterestingFile(entry.Name);
            }

            if (fullName.StartsWith("analyzers/", StringComparison.OrdinalIgnoreCase))
            {
                return entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            }

            if (fullName.StartsWith("build/", StringComparison.OrdinalIgnoreCase) ||
                fullName.StartsWith("buildTransitive/", StringComparison.OrdinalIgnoreCase))
            {
                return entry.Name.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                       entry.Name.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);
            }

            if (!fullName.Contains('/'))
            {
                return IsInterestingFile(entry.Name);
            }

            return false;
        }

        private static bool ShouldKeepRid(string rid)
        {
            if (string.IsNullOrWhiteSpace(rid))
            {
                return false;
            }

            return PreferredRuntimeIdentifiers.Contains(rid);
        }

        private static bool ShouldKeepTfm(string tfm)
        {
            if (string.IsNullOrWhiteSpace(tfm))
            {
                return false;
            }

            var normalized = tfm.ToLowerInvariant();
            if (PreferredTfms.Any(t => normalized.StartsWith(t, StringComparison.Ordinal)))
            {
                return true;
            }

            if (normalized.StartsWith("netstandard2.", StringComparison.Ordinal) ||
                normalized.StartsWith("netcoreapp3.", StringComparison.Ordinal))
            {
                return true;
            }

            if (normalized.StartsWith("net", StringComparison.Ordinal))
            {
                for (int i = 0; i < PreferredTfms.Length; i++)
                {
                    var candidate = PreferredTfms[i];
                    var dashIndex = candidate.IndexOf('-');
                    var candidatePrefix = dashIndex > 0 ? candidate[..dashIndex] : candidate;
                    if (normalized.StartsWith(candidatePrefix, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsInterestingFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            var extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension))
            {
                return false;
            }

            if (LibraryExtensions.Contains(extension))
            {
                return true;
            }

            return extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> BuildPreferredRuntimeIdentifiers()
        {
            var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    identifiers.Add(value.Trim());
                }
            }

            var currentRid = RuntimeInformation.RuntimeIdentifier;
            if (!string.IsNullOrWhiteSpace(currentRid))
            {
                Add(currentRid);

                var segments = currentRid.Split('-', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 1; i < segments.Length; i++)
                {
                    Add(string.Join('-', segments.Take(i)));
                }
            }

            if (OperatingSystem.IsWindows())
            {
                Add("win");
            }
            else if (OperatingSystem.IsMacOS())
            {
                Add("osx");
                Add("unix");
            }
            else if (OperatingSystem.IsLinux())
            {
                Add("linux");
                Add("unix");
            }

            Add("any");
            return identifiers;
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
            catch (Exception ex)
            {
                // Log the error but don't throw to allow cleanup attempts to continue
                Console.WriteLine($"Failed to delete directory {directory}: {ex.Message}");
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

        /// <summary>
        /// Removes a NuGet package from the local cache by deleting its directory
        /// </summary>
        /// <param name="packageName">The package name</param>
        /// <param name="version">Optional version. If not specified, removes the entire package directory</param>
        public static void RemovePackage(string packageName, string version = null)
        {
            if (string.IsNullOrWhiteSpace(packageName))
            {
                return;
            }

            var errors = new List<string>();

            if (!string.IsNullOrWhiteSpace(version))
            {
                // Remove specific version
                var versionDirectory = GetPackageVersionDirectory(packageName, version);
                if (Directory.Exists(versionDirectory))
                {
                    try
                    {
                        Directory.Delete(versionDirectory, true);
                    }
                    catch (Exception ex)
                    {
                        var error = $"Failed to delete package {packageName} version {version}: {ex.Message}";
                        Console.WriteLine(error);
                        errors.Add(error);
                    }
                }

                // If package root is now empty, remove it too
                var packageRoot = GetPackageRootDirectory(packageName);
                if (Directory.Exists(packageRoot) && !Directory.EnumerateFileSystemEntries(packageRoot).Any())
                {
                    try
                    {
                        Directory.Delete(packageRoot, false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to delete empty package root directory {packageRoot}: {ex.Message}");
                        // Don't add to errors list since this is just cleanup
                    }
                }
            }
            else
            {
                // Remove entire package directory (all versions)
                var packageRoot = GetPackageRootDirectory(packageName);
                if (Directory.Exists(packageRoot))
                {
                    try
                    {
                        Directory.Delete(packageRoot, true);
                    }
                    catch (Exception ex)
                    {
                        var error = $"Failed to delete package {packageName}: {ex.Message}";
                        Console.WriteLine(error);
                        errors.Add(error);
                    }
                }
            }

            if (errors.Count > 0)
            {
                throw new IOException($"Failed to remove package: {string.Join("; ", errors)}");
            }
        }

        /// <summary>
        /// Removes multiple NuGet packages from the local cache
        /// </summary>
        /// <param name="packages">Semicolon-separated list of packages in "name,version" format</param>
        public static void RemoveAllPackages(string packages)
        {
            if (string.IsNullOrWhiteSpace(packages))
            {
                return;
            }

            string[] npackages = packages.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in npackages)
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                string[] parts = item.Contains(',') ? item.Split(',') : new[] { item, string.Empty };
                string packageName = parts[0].Trim();
                string version = parts.Length > 1 ? parts[1].Trim() : string.Empty;

                RemovePackage(packageName, version);
            }
        }
    }

    public sealed class PackageAssemblyInfo
    {
        public PackageAssemblyInfo(string path, AssemblyName assemblyName)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            AssemblyName = assemblyName ?? throw new ArgumentNullException(nameof(assemblyName));
        }

        public string Path { get; }
        public AssemblyName AssemblyName { get; }
    }
}

