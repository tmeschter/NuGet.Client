// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.DependencyResolver;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Repositories;
using NuGet.RuntimeModel;
using NuGet.Shared;

namespace NuGet.Commands
{
    internal class ProjectRestoreCommand
    {
        private readonly RestoreCollectorLogger _logger;

        private readonly ProjectRestoreRequest _request;

        public ProjectRestoreCommand(ProjectRestoreRequest request)
        {
            _logger = request.Log;
            _request = request;
        }

        public async Task<Tuple<bool, List<RestoreTargetGraph>, RuntimeGraph>> TryRestoreAsync(LibraryRange projectRange,
            IEnumerable<FrameworkRuntimePair> frameworkRuntimePairs,
            NuGetv3LocalRepository userPackageFolder,
            IReadOnlyList<NuGetv3LocalRepository> fallbackPackageFolders,
            RemoteDependencyWalker remoteWalker,
            RemoteWalkContext context,
            bool forceRuntimeGraphCreation,
            CancellationToken token)
        {
            var allRuntimes = RuntimeGraph.Empty;
            var frameworkTasks = new List<Task<RestoreTargetGraph>>();
            var graphs = new List<RestoreTargetGraph>();
            var runtimesByFramework = frameworkRuntimePairs.ToLookup(p => p.Framework, p => p.RuntimeIdentifier);

            foreach (var pair in runtimesByFramework)
            {
                _logger.LogVerbose(string.Format(CultureInfo.CurrentCulture, Strings.Log_RestoringPackages, pair.Key.DotNetFrameworkName));

                frameworkTasks.Add(WalkDependenciesAsync(projectRange,
                    pair.Key,
                    remoteWalker,
                    context,
                    token: token));
            }

            var frameworkGraphs = await Task.WhenAll(frameworkTasks);

            graphs.AddRange(frameworkGraphs);

            await InstallPackagesAsync(graphs,
                userPackageFolder,
                token);

            // Check if any non-empty RIDs exist before reading the runtime graph (runtime.json).
            // Searching all packages for runtime.json and building the graph can be expensive.
            var hasNonEmptyRIDs = frameworkRuntimePairs.Any(
                tfmRidPair => !string.IsNullOrEmpty(tfmRidPair.RuntimeIdentifier));

            // The runtime graph needs to be created for scenarios with supports, forceRuntimeGraphCreation allows this.
            // Resolve runtime dependencies
            if (hasNonEmptyRIDs || forceRuntimeGraphCreation)
            {
                var localRepositories = new List<NuGetv3LocalRepository>();
                localRepositories.Add(userPackageFolder);
                localRepositories.AddRange(fallbackPackageFolders);

                var runtimeGraphs = new List<RestoreTargetGraph>();
                var runtimeTasks = new List<Task<RestoreTargetGraph[]>>();

                foreach (var graph in graphs)
                {
                    // Get the runtime graph for this specific tfm graph
                    var runtimeGraph = GetRuntimeGraph(graph, localRepositories);
                    var runtimeIds = runtimesByFramework[graph.Framework];

                    // Merge all runtimes for the output
                    allRuntimes = RuntimeGraph.Merge(allRuntimes, runtimeGraph);

                    runtimeTasks.Add(WalkRuntimeDependenciesAsync(projectRange,
                        graph,
                        runtimeIds.Where(rid => !string.IsNullOrEmpty(rid)),
                        remoteWalker,
                        context,
                        runtimeGraph,
                        token: token));
                }

                foreach (var runtimeSpecificGraph in (await Task.WhenAll(runtimeTasks)).SelectMany(g => g))
                {
                    runtimeGraphs.Add(runtimeSpecificGraph);
                }

                graphs.AddRange(runtimeGraphs);

                // Install runtime-specific packages
                await InstallPackagesAsync(runtimeGraphs,
                    userPackageFolder,
                    token);
            }

            // Update the logger with the restore target graphs
            // This allows lazy initialization for the Transitive Warning Properties
            _logger.ApplyRestoreOutput(graphs);

            // Warn for all dependencies that do not have exact matches or
            // versions that have been bumped up unexpectedly.
            await UnexpectedDependencyMessages.LogAsync(graphs, _request.Project, _logger);

            var success = await ResolutionSucceeded(graphs, context, token);

            return Tuple.Create(success, graphs, allRuntimes);
        }

        private Task<RestoreTargetGraph> WalkDependenciesAsync(LibraryRange projectRange,
            NuGetFramework framework,
            RemoteDependencyWalker walker,
            RemoteWalkContext context,
            CancellationToken token)
        {
            return WalkDependenciesAsync(projectRange,
                framework,
                runtimeIdentifier: null,
                runtimeGraph: RuntimeGraph.Empty,
                walker: walker,
                context: context,
                token: token);
        }

        private async Task<RestoreTargetGraph> WalkDependenciesAsync(LibraryRange projectRange,
            NuGetFramework framework,
            string runtimeIdentifier,
            RuntimeGraph runtimeGraph,
            RemoteDependencyWalker walker,
            RemoteWalkContext context,
            CancellationToken token)
        {
            var name = FrameworkRuntimePair.GetTargetGraphName(framework, runtimeIdentifier);
            var graphs = new List<GraphNode<RemoteResolveResult>>
            {
                await walker.WalkAsync(
                projectRange,
                framework,
                runtimeIdentifier,
                runtimeGraph,
                recursive: true)
            };

            // Resolve conflicts
            await _logger.LogAsync(LogLevel.Verbose, string.Format(CultureInfo.CurrentCulture, Strings.Log_ResolvingConflicts, name));

            // Flatten and create the RestoreTargetGraph to hold the packages
            return RestoreTargetGraph.Create(runtimeGraph, graphs, context, _logger, framework, runtimeIdentifier);
        }

        private async Task<bool> ResolutionSucceeded(IEnumerable<RestoreTargetGraph> graphs, RemoteWalkContext context, CancellationToken token)
        {
            var success = true;
            foreach (var graph in graphs)
            {
                if (graph.Conflicts.Any())
                {
                    success = false;

                    foreach (var conflict in graph.Conflicts)
                    {
                        var graphName = DiagnosticUtility.FormatGraphName(graph);

                        var message = string.Format(CultureInfo.CurrentCulture, Strings.Log_ResolverConflict,
                            conflict.Name,
                            string.Join(", ", conflict.Requests),
                            graphName);

                        _logger.Log(RestoreLogMessage.CreateError(NuGetLogCode.NU1106, message, conflict.Name, graph.TargetGraphName));
                    }
                }

                if (graph.Unresolved.Count > 0)
                {
                    success = false;
                }
            }

            if (!success)
            {
                // Log message for any unresolved dependencies
                await UnresolvedMessages.LogAsync(graphs, context, context.Logger, token);
            }

            return success;
        }

        private async Task InstallPackagesAsync(IEnumerable<RestoreTargetGraph> graphs,
            NuGetv3LocalRepository userPackageFolder,
            CancellationToken token)
        {
            var uniquePackages = new HashSet<LibraryIdentity>();
            var packagesToInstall = graphs
                .SelectMany(g => g.Install.Where(match => uniquePackages.Add(match.Library)))
                .ToList();

            if (packagesToInstall.Count > 0)
            {
                // Use up to MaxDegreeOfConcurrency, create less threads if less packages exist.
                var threadCount = Math.Min(packagesToInstall.Count, _request.MaxDegreeOfConcurrency);

                if (threadCount <= 1)
                {
                    foreach (var match in packagesToInstall)
                    {
                        await InstallPackageAsync(match, userPackageFolder, token);
                    }
                }
                else
                {
                    var bag = new ConcurrentBag<RemoteMatch>(packagesToInstall);
                    var tasks = Enumerable.Range(0, threadCount)
                        .Select(async _ =>
                        {
                            RemoteMatch match;
                            while (bag.TryTake(out match))
                            {
                                await InstallPackageAsync(match, userPackageFolder, token);
                            }
                        });
                    await Task.WhenAll(tasks);
                }
            }
        }

        private async Task InstallPackageAsync(RemoteMatch installItem, NuGetv3LocalRepository userPackageFolder, CancellationToken token)
        {
            var packageIdentity = new PackageIdentity(installItem.Library.Name, installItem.Library.Version);

            // Check if the package has already been installed.
            if (!userPackageFolder.Exists(packageIdentity.Id, packageIdentity.Version))
            {
                var versionFolderPathContext = new VersionFolderPathContext(
                    packageIdentity,
                    _request.PackagesDirectory,
                    _logger,
                    _request.PackageSaveMode,
                    _request.XmlDocFileSaveMode);

                using (var packageDependency = await installItem.Provider.GetPackageDownloaderAsync(
                    packageIdentity,
                    _request.CacheContext,
                    _logger,
                    token))
                {
                    // Install, returns true if the package was actually installed.
                    // Returns false if the package was a noop once the lock
                    // was acquired.
                    var installed = await PackageExtractor.InstallFromSourceAsync(
                        packageDependency,
                        versionFolderPathContext,
                        token);

                    if (installed)
                    {
                        // If the package was added, clear the cache so that the next caller can see it.
                        // Avoid calling this for packages that were not actually installed.
                        userPackageFolder.ClearCacheForIds(new string[] { packageIdentity.Id });
                    }
                }
            }
        }

        private Task<RestoreTargetGraph[]> WalkRuntimeDependenciesAsync(LibraryRange projectRange,
            RestoreTargetGraph graph,
            IEnumerable<string> runtimeIds,
            RemoteDependencyWalker walker,
            RemoteWalkContext context,
            RuntimeGraph runtimes,
            CancellationToken token)
        {
            var resultGraphs = new List<Task<RestoreTargetGraph>>();

            foreach (var runtimeName in runtimeIds)
            {
                _logger.LogVerbose(string.Format(CultureInfo.CurrentCulture, Strings.Log_RestoringPackages, FrameworkRuntimePair.GetTargetGraphName(graph.Framework, runtimeName)));

                if (HasRuntimeDependencies(graph, runtimes, runtimeName))
                {
                    resultGraphs.Add(WalkDependenciesAsync(projectRange,
                        graph.Framework,
                        runtimeName,
                        runtimes,
                        walker,
                        context,
                        token));
                }
                else
                {
                    // Copy the graph and add the RID
                    var copyGraph = RestoreTargetGraph.Create(runtimes, graph.Graphs, context, _logger, graph.Framework, runtimeName);
                    resultGraphs.Add(Task.FromResult(copyGraph));
                }
            }

            return Task.WhenAll(resultGraphs);
        }

        private bool HasRuntimeDependencies(RestoreTargetGraph graph, RuntimeGraph runtimeGraph, string runtime)
        {
            return graph.Flattened.Any(e => runtimeGraph.FindRuntimeDependencies(runtime, e.Key.Name).Any());
        }

        private RuntimeGraph GetRuntimeGraph(RestoreTargetGraph graph, IReadOnlyList<NuGetv3LocalRepository> localRepositories)
        {
            _logger.LogVerbose(Strings.Log_ScanningForRuntimeJson);
            var runtimeGraph = RuntimeGraph.Empty;

            foreach (var pair in GetRuntimeGraphs(graph, localRepositories))
            {
                _logger.LogVerbose(string.Format(CultureInfo.CurrentCulture, Strings.Log_MergingRuntimes, pair.Key));

                var mergeKey = new RuntimeGraphMergeKey(runtimeGraph, pair.Value);

                runtimeGraph = _mergeCache.GetOrAdd(mergeKey, k => RuntimeGraph.Merge(k.Left, k.Right));
            }

            return runtimeGraph;
        }

        private List<KeyValuePair<LibraryIdentity, RuntimeGraph>> GetRuntimeGraphs(RestoreTargetGraph graph, IReadOnlyList<NuGetv3LocalRepository> localRepositories)
        {
            var runtimeGraphs = new List<KeyValuePair<LibraryIdentity, RuntimeGraph>>();

            foreach (var node in graph.Flattened)
            {
                var library = node.Key;

                if (library.Type == LibraryType.Package)
                {
                    // Locate the package in the local repository
                    var info = NuGetv3LocalRepositoryUtility.GetPackage(localRepositories, library.Name, library.Version);

                    if (info != null)
                    {
                        var runtimeGraph = info.Package.RuntimeGraph;

                        if (runtimeGraph != null)
                        {
                            runtimeGraphs.Add(new KeyValuePair<LibraryIdentity, RuntimeGraph>(library, runtimeGraph));
                        }
                    }
                }
            }

            return runtimeGraphs;
        }

        // TODO: Remove this hack!!
        private static readonly ConcurrentDictionary<RuntimeGraphMergeKey, RuntimeGraph> _mergeCache = new ConcurrentDictionary<RuntimeGraphMergeKey, RuntimeGraph>();

        private class RuntimeGraphMergeKey : IEquatable<RuntimeGraphMergeKey>
        {
            public RuntimeGraph Left { get; }

            public RuntimeGraph Right { get; }

            public RuntimeGraphMergeKey(RuntimeGraph left, RuntimeGraph right)
            {
                Left = left;
                Right = right;
            }

            public bool Equals(RuntimeGraphMergeKey other)
            {
                if (other == null)
                {
                    return false;
                }

                if (ReferenceEquals(this, other))
                {
                    return true;
                }

                return Left.Equals(other.Left)
                    && Right.Equals(other.Right);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as RuntimeGraphMergeKey);
            }

            public override int GetHashCode()
            {
                var combiner = new HashCodeCombiner();

                combiner.AddObject(Left);
                combiner.AddObject(Right);

                return combiner.CombinedHash;
            }
        }

        private class RuntimeGraphSetKey : IEquatable<RuntimeGraphSetKey>
        {
            public RuntimeGraph[] RuntimeGraphs { get; }

            public RuntimeGraphSetKey(IEnumerable<RuntimeGraph> graphs)
            {
                RuntimeGraphs = graphs?.ToArray();
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as RuntimeGraphSetKey);
            }

            public override int GetHashCode()
            {
                var combiner = new HashCodeCombiner();

                if (RuntimeGraphs != null)
                {
                    combiner.AddSequence(RuntimeGraphs);
                }

                return combiner.CombinedHash;
            }

            public bool Equals(RuntimeGraphSetKey other)
            {
                if (other == null)
                {
                    return false;
                }

                if (ReferenceEquals(this, other))
                {
                    return true;
                }

                if (RuntimeGraphs == null && other.RuntimeGraphs == null)
                {
                    return true;
                }

                if (RuntimeGraphs?.Length == other.RuntimeGraphs?.Length)
                {
                    for (var i=0; i < RuntimeGraphs.Length; i++)
                    {
                        if (!RuntimeGraphs[i].Equals(other.RuntimeGraphs[i]))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
        }
    }
}