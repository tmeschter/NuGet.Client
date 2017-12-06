// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.RuntimeModel;
using NuGet.Shared;
using NuGet.Versioning;

namespace NuGet.DependencyResolver
{
    public class RemoteDependencyWalker
    {
        private readonly RemoteWalkContext _context;

        public RemoteDependencyWalker(RemoteWalkContext context)
        {
            _context = context;
        }

        public Task<GraphNode<RemoteResolveResult>> WalkAsync(LibraryRange library, NuGetFramework framework, string runtimeIdentifier, RuntimeGraph runtimeGraph, bool recursive)
        {
            var eclipsedby = new EclipsedByNodes();
            eclipsedby.Add(library);

            return CreateGraphNode(library, framework, runtimeIdentifier, runtimeGraph, outerEdge: null, eclipsedBy: eclipsedby);
        }

        private async Task<GraphNode<RemoteResolveResult>> CreateGraphNode(
            LibraryRange libraryRange,
            NuGetFramework framework,
            string runtimeName,
            RuntimeGraph runtimeGraph,
            GraphEdge<RemoteResolveResult> outerEdge,
            EclipsedByNodes eclipsedBy)
        {
            List<LibraryDependency> dependencies = null;
            HashSet<string> runtimeDependencies = null;
            List<Task<GraphNode<RemoteResolveResult>>> tasks = null;

            if (runtimeGraph != null && !string.IsNullOrEmpty(runtimeName))
            {
                // HACK(davidfowl): This is making runtime.json support package redirects

                // Look up any additional dependencies for this package
                foreach (var runtimeDependency in runtimeGraph.FindRuntimeDependencies(runtimeName, libraryRange.Name))
                {
                    var libraryDependency = new LibraryDependency
                    {
                        LibraryRange = new LibraryRange()
                        {
                            Name = runtimeDependency.Id,
                            VersionRange = runtimeDependency.VersionRange,
                            TypeConstraint = LibraryDependencyTarget.PackageProjectExternal
                        }
                    };

                    if (StringComparer.OrdinalIgnoreCase.Equals(runtimeDependency.Id, libraryRange.Name))
                    {
                        if (libraryRange.VersionRange != null &&
                            runtimeDependency.VersionRange != null &&
                            libraryRange.VersionRange.MinVersion < runtimeDependency.VersionRange.MinVersion)
                        {
                            libraryRange = libraryDependency.LibraryRange;
                        }
                    }
                    else
                    {
                        // Otherwise it's a dependency of this node
                        if (dependencies == null)
                        {
                            // Init dependency lists
                            dependencies = new List<LibraryDependency>(1);
                            runtimeDependencies = new HashSet<string>();
                        }

                        dependencies.Add(libraryDependency);
                        runtimeDependencies.Add(libraryDependency.Name);
                    }
                }
            }

            var node = new GraphNode<RemoteResolveResult>(libraryRange)
            {
                // Resolve the dependency from the cache or sources
                Item = await ResolverUtility.FindLibraryCachedAsync(
                    _context.FindLibraryEntryCache,
                    libraryRange,
                    framework,
                    outerEdge,
                    _context,
                    CancellationToken.None)
            };

            Debug.Assert(node.Item != null, "FindLibraryCached should return an unresolved item instead of null");

            // Merge in runtime dependencies
            if (dependencies?.Count > 0)
            {
                var nodeDependencies = node.Item.Data.Dependencies.AsList();

                foreach (var nodeDep in nodeDependencies)
                {
                    if (runtimeDependencies?.Contains(nodeDep.Name, StringComparer.OrdinalIgnoreCase) != true)
                    {
                        dependencies.Add(nodeDep);
                    }
                }

                // Create a new item on this node so that we can update it with the new dependencies from
                // runtime.json files
                // We need to clone the item since they can be shared across multiple nodes
                node.Item = new GraphItem<RemoteResolveResult>(node.Item.Key)
                {
                    Data = new RemoteResolveResult()
                    {
                        Dependencies = dependencies,
                        Match = node.Item.Data.Match
                    }
                };
            }

            // Add the current node to the eclipsed by list before recursing
            var eclipsedByWithDependencies = eclipsedBy.WithNodes(node.Item.Data.Dependencies);

            foreach (var dependency in node.Item.Data.Dependencies)
            {
                // Skip dependencies if the dependency edge has 'all' excluded and
                // the node is not a direct dependency.
                if (outerEdge == null
                    || dependency.SuppressParent != LibraryIncludeFlags.All)
                {
                    // Dependency edge from the current node to the dependency
                    var innerEdge = new GraphEdge<RemoteResolveResult>(outerEdge, node.Item, dependency);

                    // Check for a cycle, this is needed for A (project) -> A (package)
                    // since the predicate will not be called for leaf nodes.
                    var result = eclipsedBy.GetDependencyResult(innerEdge);

                    if (result == DependencyResult.Acceptable)
                    {
                        if (tasks == null)
                        {
                            tasks = new List<Task<GraphNode<RemoteResolveResult>>>(1);
                        }

                        tasks.Add(CreateGraphNode(
                            dependency.LibraryRange,
                            framework,
                            runtimeName,
                            runtimeGraph,
                            innerEdge,
                            eclipsedByWithDependencies));
                    }
                    else
                    {
                        // Keep the node in the tree if we need to look at it later
                        if (result == DependencyResult.PotentiallyDowngraded ||
                            result == DependencyResult.Cycle)
                        {
                            var dependencyNode = new GraphNode<RemoteResolveResult>(dependency.LibraryRange)
                            {
                                Disposition = result == DependencyResult.Cycle ? Disposition.Cycle : Disposition.PotentiallyDowngraded
                            };

                            dependencyNode.OuterNode = node;
                            node.InnerNodes.Add(dependencyNode);
                        }
                    }
                }
            }

            while (tasks?.Count > 0)
            {
                // Wait for any node to finish resolving
                var task = await Task.WhenAny(tasks);

                // Extract the resolved node
                tasks.Remove(task);
                var dependencyNode = await task;
                dependencyNode.OuterNode = node;

                node.InnerNodes.Add(dependencyNode);
            }

            return node;
        }

        private class EclipsedByNodes
        {
            private readonly Dictionary<string, LibraryRange[]> _nodes;

            public EclipsedByNodes()
            {
                _nodes = new Dictionary<string, LibraryRange[]>(StringComparer.OrdinalIgnoreCase);
            }

            private EclipsedByNodes(Dictionary<string, LibraryRange[]> nodes)
            {
                _nodes = new Dictionary<string, LibraryRange[]>(nodes, StringComparer.OrdinalIgnoreCase);
            }

            public void Add(LibraryRange library)
            {
                var id = library.Name;
                if (!_nodes.TryGetValue(id, out var ranges))
                {
                    ranges = new LibraryRange[] { library };
                    _nodes.Add(id, ranges);
                }
                else
                {
                    var hasRange = false;

                    foreach (var range in ranges)
                    {
                        if (library.Equals(range))
                        {
                            hasRange = true;
                            break;
                        }
                    }

                    if (!hasRange)
                    {
                        var origCount = ranges.Length;
                        var updatedRanges = new LibraryRange[origCount + 1];

                        for (var i = 0; i < origCount; i++)
                        {
                            updatedRanges[i] = ranges[i];
                        }

                        updatedRanges[origCount] = library;
                        _nodes[id] = updatedRanges;
                    }
                }
            }

            public DependencyResult GetDependencyResult(GraphEdge<RemoteResolveResult> edge)
            {
                // Check for a version in a parent node
                var library = edge.Edge.LibraryRange;
                var id = library.Name;
                var result = DependencyResult.Acceptable;

                if (_nodes.TryGetValue(id, out var parentRanges))
                {
                    // Check for aa circular dependency (not possible if this id hasn't been seen before)
                    var currentEdge = edge;
                    while (currentEdge != null)
                    {
                        if (StringComparer.OrdinalIgnoreCase.Equals(id, currentEdge.Item.Key.Name))
                        {
                            return DependencyResult.Cycle;
                        }

                        currentEdge = currentEdge.OuterEdge;
                    }

                    // Check if a higher node eclipses or downgrades this one.
                    foreach (var parentRange in parentRanges)
                    {
                        if (library.IsEclipsedBy(parentRange))
                        {
                            if (parentRange.VersionRange != null &&
                                library.VersionRange != null &&
                                !IsGreaterThanOrEqualTo(parentRange.VersionRange, library.VersionRange))
                            {
                                // Mark the node as downgraded, but continue checking for a node
                                // that eclipses the library.
                                result = DependencyResult.PotentiallyDowngraded;
                            }
                            else
                            {
                                // No action is needed if the node is eclipsed
                                return DependencyResult.Eclipsed;
                            }
                        }
                    }
                }

                return result;
            }

            public bool IsCycle(GraphEdge<RemoteResolveResult> edge, string id)
            {
                var currentEdge = edge;

                // TODO: check the cache first
                while (currentEdge != null)
                {
                    if (StringComparer.OrdinalIgnoreCase.Equals(id, currentEdge.Item.Key.Name))
                    {
                        return true;
                    }

                    currentEdge = currentEdge.OuterEdge;
                }

                return false;
            }

            public EclipsedByNodes Clone()
            {
                return new EclipsedByNodes(_nodes);
            }

            public EclipsedByNodes WithNodes(IEnumerable<LibraryDependency> dependencies)
            {
                var nodes = Clone();
                foreach (var range in dependencies)
                {
                    nodes.Add(range.LibraryRange);
                }
                return nodes;
            }
        }

        //private Func<LibraryRange, DependencyResult> ChainPredicate(Func<LibraryRange, DependencyResult> predicate, GraphNode<RemoteResolveResult> node, LibraryDependency dependency)
        //{
        //    var item = node.Item;

        //    return library =>
        //    {
        //        if (StringComparer.OrdinalIgnoreCase.Equals(item.Data.Match.Library.Name, library.Name))
        //        {
        //            return DependencyResult.Cycle;
        //        }

        //        foreach (var d in item.Data.Dependencies)
        //        {
        //            if (d != dependency && library.IsEclipsedBy(d.LibraryRange))
        //            {
        //                if (d.LibraryRange.VersionRange != null &&
        //                    library.VersionRange != null &&
        //                    !IsGreaterThanOrEqualTo(d.LibraryRange.VersionRange, library.VersionRange))
        //                {
        //                    return DependencyResult.PotentiallyDowngraded;
        //                }

        //                return DependencyResult.Eclipsed;
        //            }
        //        }

        //        return predicate(library);
        //    };
        //}

        // Verifies if minimum version specification for nearVersion is greater than the
        // minimum version specification for farVersion
        public static bool IsGreaterThanOrEqualTo(VersionRange nearVersion, VersionRange farVersion)
        {
            if (!nearVersion.HasLowerBound)
            {
                return true;
            }
            else if (!farVersion.HasLowerBound)
            {
                return false;
            }
            else if (nearVersion.IsFloating || farVersion.IsFloating)
            {
                NuGetVersion nearMinVersion;
                NuGetVersion farMinVersion;

                string nearRelease;
                string farRelease;

                if (nearVersion.IsFloating)
                {
                    if (nearVersion.Float.FloatBehavior == NuGetVersionFloatBehavior.Major)
                    {
                        // nearVersion: "*"
                        return true;
                    }

                    nearMinVersion = GetReleaseLabelFreeVersion(nearVersion);
                    nearRelease = nearVersion.Float.OriginalReleasePrefix;
                }
                else
                {
                    nearMinVersion = nearVersion.MinVersion;
                    nearRelease = nearVersion.MinVersion.Release;
                }

                if (farVersion.IsFloating)
                {
                    if (farVersion.Float.FloatBehavior == NuGetVersionFloatBehavior.Major)
                    {
                        // farVersion: "*"
                        return false;
                    }

                    farMinVersion = GetReleaseLabelFreeVersion(farVersion);
                    farRelease = farVersion.Float.OriginalReleasePrefix;
                }
                else
                {
                    farMinVersion = farVersion.MinVersion;
                    farRelease = farVersion.MinVersion.Release;
                }

                var result = nearMinVersion.CompareTo(farMinVersion, VersionComparison.Version);
                if (result != 0)
                {
                    return result > 0;
                }

                if (string.IsNullOrEmpty(nearRelease))
                {
                    // near is 1.0.0-*
                    return true;
                }
                else if (string.IsNullOrEmpty(farRelease))
                {
                    // near is 1.0.0-alpha-* and far is 1.0.0-*
                    return false;
                }
                else
                {
                    var lengthToCompare = Math.Min(nearRelease.Length, farRelease.Length);

                    return StringComparer.OrdinalIgnoreCase.Compare(
                        nearRelease.Substring(0, lengthToCompare),
                        farRelease.Substring(0, lengthToCompare)) >= 0;
                }
            }

            return nearVersion.MinVersion >= farVersion.MinVersion;
        }

        private static NuGetVersion GetReleaseLabelFreeVersion(VersionRange versionRange)
        {
            if (versionRange.Float.FloatBehavior == NuGetVersionFloatBehavior.Major)
            {
                return new NuGetVersion(int.MaxValue, int.MaxValue, int.MaxValue);
            }
            else if (versionRange.Float.FloatBehavior == NuGetVersionFloatBehavior.Minor)
            {
                return new NuGetVersion(versionRange.MinVersion.Major, int.MaxValue, int.MaxValue, int.MaxValue);
            }
            else if (versionRange.Float.FloatBehavior == NuGetVersionFloatBehavior.Patch)
            {
                return new NuGetVersion(versionRange.MinVersion.Major, versionRange.MinVersion.Minor, int.MaxValue, int.MaxValue);
            }
            else if (versionRange.Float.FloatBehavior == NuGetVersionFloatBehavior.Revision)
            {
                return new NuGetVersion(
                    versionRange.MinVersion.Major,
                    versionRange.MinVersion.Minor,
                    versionRange.MinVersion.Patch,
                    int.MaxValue);
            }
            else
            {
                return new NuGetVersion(
                    versionRange.MinVersion.Major,
                    versionRange.MinVersion.Minor,
                    versionRange.MinVersion.Patch,
                    versionRange.MinVersion.Revision);
            }
        }

        private enum DependencyResult
        {
            Acceptable,
            Eclipsed,
            PotentiallyDowngraded,
            Cycle
        }
    }
}
