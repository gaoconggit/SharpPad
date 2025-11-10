using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace monacoEditorCSharp.DataHelpers
{
    public static class DownloadNugetPackages
    {
        private static readonly string installationDirectory;
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly char[] InvalidPathChars = Path.GetInvalidFileNameChars();
        private const string DependencyManifestFileName = "sharppad.dependencies.json";
        private static readonly JsonSerializerOptions DependencyManifestSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
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
        private static readonly IReadOnlyList<NuGetFramework> PreferredFrameworks = BuildPreferredFrameworks();
        private readonly struct PackageRequest
        {
            public PackageRequest(string packageId, string version)
            {
                PackageId = packageId;
                Version = string.IsNullOrWhiteSpace(version) ? string.Empty : version.Trim();
            }

            public string PackageId { get; }
            public string Version { get; }
        }

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
            var processedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pendingPackages = new Queue<PackageRequest>();

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

                if (!string.IsNullOrWhiteSpace(downloadItem))
                {
                    pendingPackages.Enqueue(new PackageRequest(downloadItem, version));
                }
            }

            while (pendingPackages.Count > 0)
            {
                var request = pendingPackages.Dequeue();
                var packagePath = ResolvePackageDirectory(request.PackageId, request.Version);
                if (string.IsNullOrEmpty(packagePath) || !Directory.Exists(packagePath))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(packagePath);
                if (!processedDirectories.Add(fullPath))
                {
                    continue;
                }

                ProcessPackageDirectory(packagePath, assemblies, assembliesByName);

                foreach (var dependency in ReadDependencyManifest(packagePath))
                {
                    if (dependency == null || string.IsNullOrWhiteSpace(dependency.PackageId))
                    {
                        continue;
                    }

                    pendingPackages.Enqueue(new PackageRequest(
                        dependency.PackageId,
                        dependency.PackageVersion ?? string.Empty));
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

            var normalizedPackageName = packageName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedPackageName))
            {
                return;
            }

            var normalizedVersion = string.IsNullOrWhiteSpace(version) ? null : version.Trim();

            if (await TryDownloadWithDependenciesAsync(normalizedPackageName, normalizedVersion, preferredSourceKey))
            {
                return;
            }

            await DownloadPackageViaFlatContainerAsync(normalizedPackageName, normalizedVersion, preferredSourceKey);
        }

        private static async Task DownloadPackageViaFlatContainerAsync(string packageName, string version, string preferredSourceKey)
        {
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
                    var identityFromArchive = TryReadIdentityFromArchive(packageFile);
                    if (identityFromArchive != null)
                    {
                        WriteDependencyManifest(packageVersionDirectory, identityFromArchive, Array.Empty<PackageIdentity>());
                    }
                    downloadSuccessful = true;
                    break;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Console.WriteLine($"Failed to download {packageName} from {baseUrl}: {ex.Message}");
                    TryDeleteFile(packageFile);
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

        private static void ProcessPackageDirectory(
            string packagePath,
            List<PackageAssemblyInfo> assemblies,
            Dictionary<string, int> assembliesByName)
        {
            try
            {
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
                        if (!file.EndsWith(Path.Combine("net8.0", fileName), StringComparison.OrdinalIgnoreCase) &&
                            !file.Contains(Path.Combine("netstandard2.0", fileName), StringComparison.OrdinalIgnoreCase) &&
                            !file.Contains(Path.Combine("netstandard2.1", fileName), StringComparison.OrdinalIgnoreCase))
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
            catch
            {
                // Ignore directory enumeration issues
            }
        }

        private static IReadOnlyList<DependencyManifestEntry> ReadDependencyManifest(string packageDirectory)
        {
            var manifest = ReadDependencyManifestModel(packageDirectory);
            if (manifest?.Dependencies == null || manifest.Dependencies.Count == 0)
            {
                return Array.Empty<DependencyManifestEntry>();
            }

            return manifest.Dependencies.ToArray();
        }

        private static DependencyManifestModel? ReadDependencyManifestModel(string packageDirectory)
        {
            if (string.IsNullOrWhiteSpace(packageDirectory))
            {
                return null;
            }

            try
            {
                var manifestPath = Path.Combine(packageDirectory, DependencyManifestFileName);
                if (!File.Exists(manifestPath))
                {
                    return null;
                }

                var json = File.ReadAllText(manifestPath);
                return JsonSerializer.Deserialize<DependencyManifestModel>(json, DependencyManifestSerializerOptions);
            }
            catch
            {
                return null;
            }
        }

        internal static DependencyManifestModel? TryGetDependencyManifestModel(string packageName, string version = null)
        {
            var directory = TryGetPackageDirectory(packageName, version);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            return ReadDependencyManifestModel(directory);
        }

        private static void WriteDependencyManifest(
            string packageDirectory,
            PackageIdentity identity,
            IReadOnlyList<PackageIdentity> dependencies)
        {
            try
            {
                var manifest = new DependencyManifestModel
                {
                    PackageId = identity.Id,
                    PackageVersion = identity.Version?.ToNormalizedString() ?? string.Empty,
                    Dependencies = dependencies?
                        .Where(d => d != null && !string.IsNullOrWhiteSpace(d.Id))
                        .Select(d => new DependencyManifestEntry
                        {
                            PackageId = d.Id,
                            PackageVersion = d.Version?.ToNormalizedString()
                        })
                        .ToList() ?? new List<DependencyManifestEntry>()
                };

                var manifestPath = Path.Combine(packageDirectory, DependencyManifestFileName);
                var json = JsonSerializer.Serialize(manifest, DependencyManifestSerializerOptions);
                File.WriteAllText(manifestPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write dependency manifest for {identity.Id} {identity.Version}: {ex.Message}");
            }
        }

        private static PackageIdentity TryReadIdentityFromArchive(string packageFile)
        {
            try
            {
                using var reader = new PackageArchiveReader(packageFile);
                return reader.GetIdentity();
            }
            catch
            {
                return null;
            }
        }

        private static async Task<bool> TryDownloadWithDependenciesAsync(string packageName, string version, string preferredSourceKey)
        {
            try
            {
                var repositories = CreateSourceRepositories(preferredSourceKey);
                if (repositories.Count == 0)
                {
                    return false;
                }

                using var cacheContext = new SourceCacheContext();
                var logger = NullLogger.Instance;
                var cancellationToken = CancellationToken.None;

                NuGetVersion resolvedVersion;
                if (!string.IsNullOrWhiteSpace(version))
                {
                    if (!NuGetVersion.TryParse(version, out resolvedVersion))
                    {
                        Console.WriteLine($"Invalid NuGet version '{version}' for {packageName}.");
                        return false;
                    }
                }
                else
                {
                    resolvedVersion = await ResolveLatestVersionAsync(packageName, repositories, cacheContext, logger, cancellationToken);
                    if (resolvedVersion == null)
                    {
                        Console.WriteLine($"Unable to resolve latest version for {packageName} from configured sources.");
                        return false;
                    }
                }

                var identity = new PackageIdentity(packageName, resolvedVersion);
                var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var downloaded = await DownloadIdentityRecursiveAsync(identity, repositories, cacheContext, logger, processed, cancellationToken);
                if (!downloaded)
                {
                    Console.WriteLine($"Failed to download package {packageName} {resolvedVersion.ToNormalizedString()} via NuGet.Protocol.");
                }

                return downloaded;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NuGet dependency download failed for {packageName}: {ex.Message}");
                return false;
            }
        }

        private static IReadOnlyList<SourceRepository> CreateSourceRepositories(string preferredSourceKey)
        {
            var repositories = new List<SourceRepository>();
            foreach (var source in PackageSourceManager.GetOrderedSources(preferredSourceKey))
            {
                var serviceIndexUrl = PackageSourceManager.GetServiceIndexUrl(source);
                if (string.IsNullOrWhiteSpace(serviceIndexUrl))
                {
                    continue;
                }

                try
                {
                    repositories.Add(Repository.Factory.GetCoreV3(serviceIndexUrl));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to initialize NuGet source {source.Name} ({serviceIndexUrl}): {ex.Message}");
                }
            }

            return repositories;
        }

        private static async Task<NuGetVersion?> ResolveLatestVersionAsync(
            string packageId,
            IReadOnlyList<SourceRepository> repositories,
            SourceCacheContext cacheContext,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            foreach (var repository in repositories)
            {
                try
                {
                    var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
                    var versions = await resource.GetAllVersionsAsync(packageId, cacheContext, logger, cancellationToken);
                    var latest = versions?
                        .OrderByDescending(v => v)
                        .FirstOrDefault();

                    if (latest != null)
                    {
                        return latest;
                    }
                }
                catch
                {
                    // Try next repository
                }
            }

            return null;
        }

        private static async Task<bool> DownloadIdentityRecursiveAsync(
            PackageIdentity identity,
            IReadOnlyList<SourceRepository> repositories,
            SourceCacheContext cacheContext,
            ILogger logger,
            HashSet<string> processed,
            CancellationToken cancellationToken)
        {
            var key = BuildPackageKey(identity);
            if (!processed.Add(key))
            {
                return true;
            }

            var versionDirectory = GetPackageVersionDirectory(identity.Id, identity.Version.ToNormalizedString());
            if (HasAssemblies(versionDirectory))
            {
                return true;
            }

            Directory.CreateDirectory(versionDirectory);
            var packageFile = Path.Combine(versionDirectory, BuildPackageFileName(identity.Id, identity.Version.ToNormalizedString()));

            foreach (var repository in repositories)
            {
                try
                {
                    var findResource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
                    if (!await findResource.DoesPackageExistAsync(identity.Id, identity.Version, cacheContext, logger, cancellationToken))
                    {
                        continue;
                    }

                    using (var fileStream = new FileStream(packageFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true))
                    {
                        var copied = await findResource.CopyNupkgToStreamAsync(identity.Id, identity.Version, fileStream, cacheContext, logger, cancellationToken);
                        if (!copied)
                        {
                            TryDeleteFile(packageFile);
                            continue;
                        }
                    }

                    ExtractPackageArchive(packageFile, versionDirectory);

                    var dependencies = await ResolvePackageDependenciesAsync(packageFile, repositories, cacheContext, logger, cancellationToken);
                    WriteDependencyManifest(versionDirectory, identity, dependencies);
                    foreach (var dependency in dependencies)
                    {
                        var dependencyDownloaded = await DownloadIdentityRecursiveAsync(dependency, repositories, cacheContext, logger, processed, cancellationToken);
                        if (!dependencyDownloaded)
                        {
                            Console.WriteLine($"Failed to download dependency {dependency.Id} {dependency.Version} required by {identity.Id} {identity.Version}.");
                        }
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    var source = repository.PackageSource?.Source ?? "unknown";
                    Console.WriteLine($"Failed to download {identity.Id} {identity.Version} from {source}: {ex.Message}");
                    TryDeleteFile(packageFile);
                }
            }

            Console.WriteLine($"Unable to download {identity.Id} {identity.Version} from configured sources.");
            return HasAssemblies(versionDirectory);
        }

        private static async Task<IReadOnlyList<PackageIdentity>> ResolvePackageDependenciesAsync(
            string packageFile,
            IReadOnlyList<SourceRepository> repositories,
            SourceCacheContext cacheContext,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            try
            {
                using var reader = new PackageArchiveReader(packageFile);
                var dependencyGroups = reader.NuspecReader.GetDependencyGroups();
                var selectedGroup = SelectDependencyGroup(dependencyGroups);
                if (selectedGroup == null || selectedGroup.Packages == null)
                {
                    return Array.Empty<PackageIdentity>();
                }

                var dependencies = new List<PackageIdentity>();
                foreach (var dependency in selectedGroup.Packages)
                {
                    if (string.IsNullOrWhiteSpace(dependency.Id))
                    {
                        continue;
                    }

                    var versionRange = dependency.VersionRange ?? VersionRange.All;
                    var resolvedVersion = await ResolveDependencyVersionAsync(
                        dependency.Id,
                        versionRange,
                        repositories,
                        cacheContext,
                        logger,
                        cancellationToken);

                    if (resolvedVersion == null)
                    {
                        Console.WriteLine($"Unable to resolve dependency {dependency.Id} ({versionRange.OriginalString}) referenced by {Path.GetFileName(packageFile)}.");
                        continue;
                    }

                    dependencies.Add(new PackageIdentity(dependency.Id, resolvedVersion));
                }

                return dependencies;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to read dependencies from {Path.GetFileName(packageFile)}: {ex.Message}");
                return Array.Empty<PackageIdentity>();
            }
        }

        private static async Task<NuGetVersion?> ResolveDependencyVersionAsync(
            string packageId,
            VersionRange versionRange,
            IReadOnlyList<SourceRepository> repositories,
            SourceCacheContext cacheContext,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            versionRange ??= VersionRange.All;

            foreach (var repository in repositories)
            {
                try
                {
                    var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
                    var versions = await resource.GetAllVersionsAsync(packageId, cacheContext, logger, cancellationToken);
                    if (versions == null)
                    {
                        continue;
                    }

                    var resolved = versions
                        .Where(v => versionRange.Satisfies(v))
                        .OrderByDescending(v => v)
                        .FirstOrDefault();

                    if (resolved != null)
                    {
                        return resolved;
                    }
                }
                catch
                {
                    // Try next source
                }
            }

            return versionRange.HasLowerBound ? versionRange.MinVersion : null;
        }

        private static PackageDependencyGroup? SelectDependencyGroup(IEnumerable<PackageDependencyGroup> dependencyGroups)
        {
            if (dependencyGroups == null)
            {
                return null;
            }

            var groups = dependencyGroups.ToList();
            if (groups.Count == 0)
            {
                return null;
            }

            var anyGroup = groups.FirstOrDefault(g => NuGetFramework.FrameworkNameComparer.Equals(g.TargetFramework, NuGetFramework.AnyFramework));
            if (anyGroup != null)
            {
                return anyGroup;
            }

            foreach (var preferred in PreferredFrameworks)
            {
                var match = groups.FirstOrDefault(g => NuGetFramework.FrameworkNameComparer.Equals(g.TargetFramework, preferred));
                if (match != null)
                {
                    return match;
                }
            }

            return groups.First();
        }

        private static string BuildPackageKey(PackageIdentity identity)
        {
            var id = identity.Id?.ToLowerInvariant() ?? string.Empty;
            return $"{id}:{identity.Version.ToNormalizedString()}";
        }

        internal sealed class DependencyManifestModel
        {
            public string PackageId { get; set; }
            public string PackageVersion { get; set; }
            public List<DependencyManifestEntry> Dependencies { get; set; } = new();
        }

        internal sealed class DependencyManifestEntry
        {
            public string PackageId { get; set; }
            public string PackageVersion { get; set; }
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

        private static IReadOnlyList<NuGetFramework> BuildPreferredFrameworks()
        {
            var frameworks = new List<NuGetFramework>();
            foreach (var tfm in PreferredTfms)
            {
                if (string.IsNullOrWhiteSpace(tfm))
                {
                    continue;
                }

                try
                {
                    frameworks.Add(NuGetFramework.ParseFolder(tfm));
                }
                catch
                {
                    // Ignore invalid TFMs
                }
            }

            if (!frameworks.Any(f => NuGetFramework.FrameworkNameComparer.Equals(f, NuGetFramework.AnyFramework)))
            {
                frameworks.Add(NuGetFramework.AnyFramework);
            }

            return frameworks;
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

