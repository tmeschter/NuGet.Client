﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace NuGet.PackageManagement
{
    /// <summary>
    /// Aggregate repository helper for the Resolver Gather step.
    /// </summary>
    public static class ResolverGather
    {
        private const int MaxThreads = 4;

        // Packages may have dependencies that span repositories
        // Example:
        // Repo 1:  A   C   E
        //           \ / \ /
        // Repo 2:    B   D
        //
        // To correctly resolve all dependencies of A we must check each source for every dependency.

        /// <summary>
        /// Gather dependency info for the install packages and the new targets.
        /// </summary>
        public static async Task<HashSet<SourceDependencyInfo>> GatherPackageDependencyInfo(ResolutionContext context,
        IEnumerable<PackageIdentity> primaryTargets,
        IEnumerable<PackageIdentity> installedPackages,
        NuGetFramework targetFramework,
        IEnumerable<SourceRepository> primarySources,
        IEnumerable<SourceRepository> allSources,
        SourceRepository packagesFolderSource,
        CancellationToken token)
        {
            return await GatherPackageDependencyInfo(context, null, primaryTargets, installedPackages,
                targetFramework, primarySources, allSources, packagesFolderSource, token);
        }

        /// <summary>
        /// Gather dependency info for the install packages and the new targets.
        /// </summary>
        /// <param name="primaryTargetIds">Gathers all versions of the ids</param>
        /// <param name="primaryTargets">Gathers a single version of the packages</param>
        /// <param name="installedPackages">Already installed packages</param>
        /// <param name="targetFramework">Project target framework</param>
        /// <param name="primarySources">Primary source to search for the primary targets</param>
        /// <param name="allSources">Fallback sources</param>
        /// <param name="packagesFolderSource">Source for installed packages</param>
        public static async Task<HashSet<SourceDependencyInfo>> GatherPackageDependencyInfo(ResolutionContext context,
            IEnumerable<string> primaryTargetIds,
            IEnumerable<PackageIdentity> primaryTargets,
            IEnumerable<PackageIdentity> installedPackages,
            NuGetFramework targetFramework,
            IEnumerable<SourceRepository> primarySources,
            IEnumerable<SourceRepository> allSources,
            SourceRepository packagesFolderSource,
            CancellationToken token)
        {
            // get a distinct set of packages from all repos
            var combinedResults = new HashSet<SourceDependencyInfo>(PackageIdentity.Comparer);

            // get the dependency info resources for each repo
            // primary and all may share the same resources
            var getResourceTasks = new List<Task>();

            var depResources = new Dictionary<SourceRepository, Task<DependencyInfoResource>>();
            foreach (var source in allSources.Concat(primarySources).Concat(new SourceRepository[] { packagesFolderSource }))
            {
                if (!depResources.ContainsKey(source))
                {
                    var task = Task.Run(async () => await source.GetResourceAsync<DependencyInfoResource>(token));

                    depResources.Add(source, task);

                    // Limit the number of tasks to MaxThreads by awaiting each time we hit the limit
                    while (getResourceTasks.Count >= MaxThreads)
                    {
                        var finishedTask = await Task.WhenAny(getResourceTasks);

                        getResourceTasks.Remove(finishedTask);
                    }
                }
            }

            // a resource may be null, if it is exclude this source from the gather
            var primaryDependencyResources = new List<Tuple<SourceRepository, DependencyInfoResource>>();

            foreach (var source in primarySources)
            {
                var resource = await depResources[source];

                if (source != null)
                {
                    primaryDependencyResources.Add(new Tuple<SourceRepository, DependencyInfoResource>(source, resource));
                }
            }

            var allDependencyResources = new List<Tuple<SourceRepository, DependencyInfoResource>>();

            foreach (var source in allSources)
            {
                var resource = await depResources[source];

                if (source != null)
                {
                    allDependencyResources.Add(new Tuple<SourceRepository, DependencyInfoResource>(source, resource));
                }
            }

            // resolve primary targets only from primary sources
            foreach (var primaryTarget in primaryTargets)
            {
                await GatherPackage(primaryTarget.Id, primaryTarget.Version, combinedResults,
                    primaryDependencyResources,
                    targetFramework,
                    context,
                    ignoreExceptions: false,
                    token: token);
            }

            // null can occur for scenarios with PackageIdentities only
            if (primaryTargetIds != null)
            {
                foreach (var primaryTargetId in primaryTargetIds)
                {
                    await GatherPackage(primaryTargetId,
                        version: null,
                        packageCache: combinedResults,
                        dependencyResources: primaryDependencyResources,
                        targetFramework: targetFramework,
                        context: context,
                        ignoreExceptions: false,
                        token: token);
                }
            }

            // Find all missing packages
            var neededPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allPrimaryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (primaryTargetIds != null)
            {
                allPrimaryIds.UnionWith(primaryTargetIds);
            }

            allPrimaryIds.UnionWith(primaryTargets.Select(target => target.Id));

            neededPackageIds.UnionWith(allPrimaryIds);

            // make sure the primary targets exist
            foreach (var primaryId in allPrimaryIds)
            {
                if (!combinedResults.Any(package => StringComparer.OrdinalIgnoreCase.Equals(primaryId, package.Id)))
                {
                    throw new InvalidOperationException(String.Format(Strings.PackageNotFound, primaryId));
                }
            }

            // find all packages that have already been installed
            var installedInfo = new HashSet<SourceDependencyInfo>(PackageIdentity.Comparer);

            foreach (var installedPackage in installedPackages)
            {
                var installedResource = await depResources[packagesFolderSource];

                var packageInfo = await installedResource.ResolvePackage(installedPackage, targetFramework, token);

                // Installed packages should exist, but if they do not an attempt will be made to find them in the sources.
                if (packageInfo != null)
                {
                    installedInfo.Add(CreateSourceDependencyInfo(context, packagesFolderSource, packageInfo));
                    combinedResults.Add(CreateSourceDependencyInfo(context, packagesFolderSource, packageInfo));
                }
            }

            // all packages found in the repos
            var idsSearched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // mark all primary ids so that they are not searched for in the fallback repos
            idsSearched.UnionWith(allPrimaryIds);

            // gather all missing dependencies
            bool complete = false;

            // closure of all packages related to the new package
            // start with just the primary ids
            var closureIds = new HashSet<string>(allPrimaryIds, StringComparer.OrdinalIgnoreCase);

            // walk the dependency graph both upwards and downwards for the new package
            // this is done in multiple passes to find the complete closure when
            // new dependecies are found
            while (!complete)
            {
                complete = true;

                // find all dependencies of packages that we have expanded, and search those also
                closureIds.UnionWith(combinedResults.Where(package => idsSearched.Contains(package.Id))
                    .SelectMany(package => package.Dependencies)
                    .Select(dependency => dependency.Id));

                // expand all parents of expanded packages
                closureIds.UnionWith(combinedResults.Where(
                    package => package.Dependencies.Any(dependency => idsSearched.Contains(dependency.Id)))
                    .Select(package => package.Id));

                // all unique ids gathered so far
                var currentResultIds = new HashSet<string>(combinedResults.Select(package => package.Id), StringComparer.OrdinalIgnoreCase);

                // installed packages must be gathered to find a complete solution
                closureIds.UnionWith(installedPackages.Select(package => package.Id)
                    .Where(id => !currentResultIds.Contains(id)));

                // if any dependencies are completely missing they must be retrieved
                closureIds.UnionWith(combinedResults.SelectMany(package => package.Dependencies)
                    .Select(dependency => dependency.Id).Where(id => !currentResultIds.Contains(id)));

                var missingIds = closureIds.Except(idsSearched, StringComparer.OrdinalIgnoreCase);

                // Gather packages for all missing ids
                foreach (var missingId in missingIds)
                {
                    complete = false;
                    idsSearched.Add(missingId);

                    // Gather across all sources in parallel
                    await GatherPackage(packageId: missingId, version: null,
                        packageCache: combinedResults,
                        dependencyResources: allDependencyResources,
                        targetFramework: targetFramework,
                        context: context, ignoreExceptions: true, token: token);
                }
            }

            // resolve all targets
            return combinedResults;
        }

        /// <summary>
        /// Retrieve packages from the given sources.
        /// </summary>
        private static async Task GatherPackage(
            string packageId,
            NuGetVersion version,
            HashSet<SourceDependencyInfo> packageCache,
            List<Tuple<SourceRepository, DependencyInfoResource>> dependencyResources,
            NuGetFramework targetFramework,
            ResolutionContext context,
            bool ignoreExceptions,
            CancellationToken token)
        {
            // results need to be kept in order
            var results = new Queue<Task<IEnumerable<SourceDependencyInfo>>>();

            // resolve from each source
            foreach (var sourceTuple in dependencyResources)
            {
                // Limit the number of tasks to MaxThreads
                // If we hit the limit stop and await the oldest task
                if (results.Count >= MaxThreads)
                {
                    packageCache.UnionWith(await results.Dequeue());
                }

                SourceRepository source = sourceTuple.Item1;
                DependencyInfoResource resolverRes = sourceTuple.Item2;

                var task = Task.Run(async () => await GatherPackageCore(packageId, version,
                    source, resolverRes, targetFramework, context, ignoreExceptions, token));

                results.Enqueue(task);
            }

            // retrieve package results from the gather tasks
            // order is important here. packages from the first repository beat packages from later repositories
            while (results.Count > 0)
            {
                packageCache.UnionWith(await results.Dequeue());
            }
        }

        /// <summary>
        /// Call the DependencyInfoResource safely
        /// </summary>
        private static async Task<IEnumerable<SourceDependencyInfo>> GatherPackageCore(
            string packageId,
            NuGetVersion version,
            SourceRepository source,
            DependencyInfoResource resource,
            NuGetFramework targetFramework,
            ResolutionContext context,
            bool ignoreExceptions,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var results = new List<SourceDependencyInfo>();

            try
            {
                // Call the dependecy info resource
                if (version == null)
                {
                    // find all versions of a package
                    var packages = await resource.ResolvePackages(packageId, targetFramework, token);

                    results.AddRange(packages.Select(package =>
                            CreateSourceDependencyInfo(context, source, package)));
                }
                else
                {
                    // find a single package id and version
                    var package = await resource.ResolvePackage(new PackageIdentity(packageId, version), targetFramework, token);

                    if (package != null)
                    {
                        results.Add(CreateSourceDependencyInfo(context, source, package));
                    }
                }
            }
            catch
            {
                // Secondary sources should not stop the gather process. They are often invalid
                // such as scenarios where a UNC is offline.
                if (!ignoreExceptions)
                {
                    throw;
                }
            }

            return results;
        }

        // Combine the soure and dependency info
        private static SourceDependencyInfo CreateSourceDependencyInfo(ResolutionContext context, SourceRepository source, PackageDependencyInfo package)
        {
            // Set the includePrerelease on the version range on every single package dependency to context.IncludePreerelease
            var packageDependencies = package.Dependencies;
            var modifiedPackageDependencies = new List<PackageDependency>();
            foreach (var packageDependency in packageDependencies)
            {
                var versionRange = packageDependency.VersionRange;
                var modifiedVersionRange = new VersionRange(versionRange.MinVersion, versionRange.IsMinInclusive, versionRange.MaxVersion,
                    versionRange.IsMaxInclusive, context.IncludePrerelease, versionRange.Float);

                var modifiedPackageDependency = new PackageDependency(packageDependency.Id, modifiedVersionRange);
                modifiedPackageDependencies.Add(modifiedPackageDependency);
            }

            var modifiedPackageDependencyInfo = new PackageDependencyInfo(new PackageIdentity(package.Id, package.Version), modifiedPackageDependencies);
            return new SourceDependencyInfo(modifiedPackageDependencyInfo, source);
        }
    }
}
