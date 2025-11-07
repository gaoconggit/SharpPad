using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace monacoEditorCSharp.DataHelpers
{
    /// <summary>
    /// Resolves NuGet package dependencies using NuGet.Protocol library
    /// </summary>
    public class NuGetDependencyResolver
    {
        private static readonly ILogger _logger = NullLogger.Instance;
        private static readonly CancellationToken _cancellationToken = CancellationToken.None;

        /// <summary>
        /// Resolves all dependencies for a given package and returns the complete list of packages to download
        /// </summary>
        /// <param name="packageId">The package ID</param>
        /// <param name="version">The package version (optional, latest if not specified)</param>
        /// <param name="preferredSourceKey">Preferred NuGet source key</param>
        /// <returns>List of package identities with their versions that need to be downloaded</returns>
        public static async Task<List<PackageIdentity>> ResolveDependenciesAsync(
            string packageId,
            string version,
            string preferredSourceKey = null)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return new List<PackageIdentity>();
            }

            var results = new List<PackageIdentity>();
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Get NuGet source repositories
                var sourceRepository = GetSourceRepository(preferredSourceKey);

                // Resolve the target framework (prefer .NET 9.0, fall back to .NET 8.0, then .NET Standard 2.1/2.0)
                var targetFramework = NuGet.Frameworks.NuGetFramework.Parse("net9.0");

                // Get the package metadata
                var packageVersion = await GetPackageVersionAsync(sourceRepository, packageId, version);
                if (packageVersion == null)
                {
                    Console.WriteLine($"Could not find package {packageId} version {version}");
                    return results;
                }

                var rootIdentity = new PackageIdentity(packageId, packageVersion);

                // Recursively resolve dependencies
                await ResolveDependenciesRecursiveAsync(
                    rootIdentity,
                    sourceRepository,
                    targetFramework,
                    results,
                    processed
                );

                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error resolving dependencies for {packageId}: {ex.Message}");
                return results;
            }
        }

        /// <summary>
        /// Resolves dependencies for multiple packages
        /// </summary>
        public static async Task<List<PackageIdentity>> ResolveDependenciesForMultiplePackagesAsync(
            string packages,
            string preferredSourceKey = null)
        {
            if (string.IsNullOrWhiteSpace(packages))
            {
                return new List<PackageIdentity>();
            }

            var allDependencies = new Dictionary<string, PackageIdentity>(StringComparer.OrdinalIgnoreCase);
            string[] packageList = packages.Split(';', StringSplitOptions.RemoveEmptyEntries);

            foreach (var item in packageList)
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                string[] parts = item.Contains(',') ? item.Split(',') : new[] { item, string.Empty };
                string packageId = parts[0].Trim();
                string version = parts.Length > 1 ? parts[1].Trim() : string.Empty;

                var dependencies = await ResolveDependenciesAsync(packageId, version, preferredSourceKey);

                // Merge dependencies, keeping the highest version if there are conflicts
                foreach (var dep in dependencies)
                {
                    if (allDependencies.TryGetValue(dep.Id, out var existing))
                    {
                        // Keep the higher version
                        if (dep.Version > existing.Version)
                        {
                            allDependencies[dep.Id] = dep;
                        }
                    }
                    else
                    {
                        allDependencies[dep.Id] = dep;
                    }
                }
            }

            return allDependencies.Values.ToList();
        }

        private static async Task ResolveDependenciesRecursiveAsync(
            PackageIdentity package,
            SourceRepository sourceRepository,
            NuGet.Frameworks.NuGetFramework targetFramework,
            List<PackageIdentity> results,
            HashSet<string> processed)
        {
            var packageKey = $"{package.Id}|{package.Version}";

            if (!processed.Add(packageKey))
            {
                return; // Already processed
            }

            // Add this package to results
            results.Add(package);

            try
            {
                // Get package metadata to find dependencies
                var metadataResource = await sourceRepository.GetResourceAsync<PackageMetadataResource>(_cancellationToken);
                var metadata = await metadataResource.GetMetadataAsync(
                    package,
                    new SourceCacheContext(),
                    _logger,
                    _cancellationToken
                );

                if (metadata == null)
                {
                    return;
                }

                // Get dependency groups and find the best match for our target framework
                var dependencyGroups = metadata.DependencySets;
                var compatibleGroup = GetBestCompatibleDependencyGroup(dependencyGroups, targetFramework);

                if (compatibleGroup == null || !compatibleGroup.Packages.Any())
                {
                    return; // No dependencies
                }

                // Process each dependency
                foreach (var dependency in compatibleGroup.Packages)
                {
                    // Skip if this is just a minimum version specification without a specific version
                    var dependencyVersion = dependency.VersionRange.MinVersion;
                    if (dependencyVersion == null)
                    {
                        // Try to get the latest version
                        dependencyVersion = await GetPackageVersionAsync(sourceRepository, dependency.Id, null);
                    }

                    if (dependencyVersion == null)
                    {
                        continue;
                    }

                    var dependencyIdentity = new PackageIdentity(dependency.Id, dependencyVersion);

                    // Recursively resolve this dependency's dependencies
                    await ResolveDependenciesRecursiveAsync(
                        dependencyIdentity,
                        sourceRepository,
                        targetFramework,
                        results,
                        processed
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error resolving dependencies for {package.Id} {package.Version}: {ex.Message}");
            }
        }

        private static async Task<NuGetVersion> GetPackageVersionAsync(
            SourceRepository sourceRepository,
            string packageId,
            string version)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(version))
                {
                    // Parse and return the specific version
                    if (NuGetVersion.TryParse(version, out var nugetVersion))
                    {
                        return nugetVersion;
                    }
                }

                // Get the latest version
                var metadataResource = await sourceRepository.GetResourceAsync<PackageMetadataResource>(_cancellationToken);
                var metadata = await metadataResource.GetMetadataAsync(
                    packageId,
                    includePrerelease: false,
                    includeUnlisted: false,
                    new SourceCacheContext(),
                    _logger,
                    _cancellationToken
                );

                return metadata?.OrderByDescending(m => m.Identity.Version).FirstOrDefault()?.Identity.Version;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting version for {packageId}: {ex.Message}");
                return null;
            }
        }

        private static NuGet.Packaging.PackageDependencyGroup GetBestCompatibleDependencyGroup(
            IEnumerable<NuGet.Packaging.PackageDependencyGroup> dependencyGroups,
            NuGet.Frameworks.NuGetFramework targetFramework)
        {
            if (dependencyGroups == null || !dependencyGroups.Any())
            {
                return null;
            }

            // Define fallback frameworks in priority order
            var fallbackFrameworks = new[]
            {
                NuGet.Frameworks.NuGetFramework.Parse("net9.0"),
                NuGet.Frameworks.NuGetFramework.Parse("net8.0"),
                NuGet.Frameworks.NuGetFramework.Parse("net7.0"),
                NuGet.Frameworks.NuGetFramework.Parse("net6.0"),
                NuGet.Frameworks.NuGetFramework.Parse("netstandard2.1"),
                NuGet.Frameworks.NuGetFramework.Parse("netstandard2.0"),
            };

            // Try to find exact match first
            foreach (var framework in fallbackFrameworks)
            {
                var exactMatch = dependencyGroups.FirstOrDefault(g =>
                    g.TargetFramework.Framework.Equals(framework.Framework, StringComparison.OrdinalIgnoreCase) &&
                    g.TargetFramework.Version == framework.Version
                );

                if (exactMatch != null)
                {
                    return exactMatch;
                }
            }

            // Try to find compatible match
            var reducer = new NuGet.Frameworks.FrameworkReducer();
            var frameworks = dependencyGroups.Select(g => g.TargetFramework).ToList();
            var nearest = reducer.GetNearest(targetFramework, frameworks);

            if (nearest != null)
            {
                return dependencyGroups.FirstOrDefault(g => g.TargetFramework == nearest);
            }

            // Return the first group as last resort
            return dependencyGroups.FirstOrDefault();
        }

        private static SourceRepository GetSourceRepository(string preferredSourceKey = null)
        {
            var source = PackageSourceManager.GetSource(preferredSourceKey);

            // Use the API URL for v3 protocol, fall back to Url if ApiUrl is not available
            var sourceUrl = !string.IsNullOrWhiteSpace(source.ApiUrl)
                ? source.ApiUrl
                : source.Url;

            var nugetPackageSource = new NuGet.Configuration.PackageSource(sourceUrl);
            var providers = Repository.Provider.GetCoreV3();

            return new SourceRepository(nugetPackageSource, providers);
        }
    }
}
