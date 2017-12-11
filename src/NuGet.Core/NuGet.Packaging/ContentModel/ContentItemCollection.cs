// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using NuGet.ContentModel.Infrastructure;
using NuGet.Shared;

namespace NuGet.ContentModel
{
    public class ContentItemCollection
    {
        private readonly Dictionary<string, List<Asset>> _assets = new Dictionary<string, List<Asset>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True if lib/contract exists
        /// </summary>
        public bool HasContract { get; private set; }

        public void Load(IEnumerable<string> paths)
        {
            // Read already loaded assets
            foreach (var path in paths)
            {
                // Skip files in the root of the directory
                var folder = GetFolder(path);
                if (folder != null)
                {
                    AddAsset(path, folder);

                    if (path.StartsWith("lib/contract", StringComparison.Ordinal))
                    {
                        HasContract = true;
                        AddAsset("ref/any" + path.Substring("lib/contract".Length), "ref");
                    }
                }
            }
        }

        private void AddAsset(string path, string folder)
        {
            var asset = new Asset()
            {
                Path = path,
                Folder = folder
            };

            if (_assets.TryGetValue(folder, out var assets))
            {
                assets.Add(asset);
            }
            else
            {
                _assets.Add(folder, new List<Asset>() { asset });
            }
        }

        private List<Asset> GetAssetsOrNull(PatternExpression pattern)
        {
            if (_assets.TryGetValue(pattern.Folder, out var items))
            {
                return items;
            }

            return null;
        }

        public IEnumerable<ContentItem> FindItems(PatternSet definition)
        {
            return FindItemsImplementation(definition, _assets.SelectMany(e => e.Value));
        }

        public IEnumerable<ContentItemGroup> FindItemGroups(PatternSet definition)
        {
            return FindItemGroupsOrNull(definition) ?? Enumerable.Empty<ContentItemGroup>();
        }

        public List<ContentItemGroup> FindItemGroupsOrNull(PatternSet definition)
        {
            List<ContentItemGroup> result = null;

            if (_assets.Count > 0)
            {
                var groupPatterns = definition.GroupExpressions;

                Dictionary<ContentItem, List<Asset>> groups = null;
                foreach (var groupPattern in groupPatterns)
                {
                    var assets = GetAssetsOrNull(groupPattern);
                    if (assets != null)
                    {
                        foreach (var asset in assets)
                        {
                            var item = groupPattern.Match(asset.Path, definition.PropertyDefinitions);
                            if (item != null)
                            {
                                if (groups == null)
                                {
                                    groups = new Dictionary<ContentItem, List<Asset>>(GroupComparer.DefaultComparer);
                                }

                                if (groups.TryGetValue(item, out var assetValues))
                                {
                                    assetValues.Add(asset);
                                }
                                else
                                {
                                    groups.Add(item, new List<Asset>() { asset });
                                }
                            }
                        }
                    }
                }

                if (groups != null)
                {
                    foreach (var grouping in groups)
                    {
                        var group = new ContentItemGroup(grouping.Key.Properties);

                        FindItemsImplementation(definition, grouping.Value, group.Items);

                        if (result == null)
                        {
                            result = new List<ContentItemGroup>(1);
                        }

                        result.Add(group);
                    }
                }
            }

            return result;
        }

        public bool HasItemGroup(SelectionCriteria criteria, params PatternSet[] definitions)
        {
            return FindBestItemGroup(criteria, definitions) != null;
        }

        public ContentItemGroup FindBestItemGroup(SelectionCriteria criteria, params PatternSet[] definitions)
        {
            foreach (var definition in definitions)
            {
                var itemGroups = FindItemGroupsOrNull(definition);
                if (itemGroups != null)
                {
                    foreach (var criteriaEntry in criteria.Entries)
                    {
                        ContentItemGroup bestGroup = null;

                        foreach (var itemGroup in itemGroups)
                        {
                            var groupIsValid = true;
                            foreach (var criteriaProperty in criteriaEntry.Properties)
                            {
                                if (criteriaProperty.Value == null)
                                {
                                    if (itemGroup.Properties.ContainsKey(criteriaProperty.Key))
                                    {
                                        groupIsValid = false;
                                        break;
                                    }
                                }
                                else
                                {
                                    object itemProperty;
                                    if (!itemGroup.Properties.TryGetValue(criteriaProperty.Key, out itemProperty))
                                    {
                                        groupIsValid = false;
                                        break;
                                    }
                                    ContentPropertyDefinition propertyDefinition;
                                    if (!definition.PropertyDefinitions.TryGetValue(criteriaProperty.Key, out propertyDefinition))
                                    {
                                        groupIsValid = false;
                                        break;
                                    }
                                    if (!propertyDefinition.IsCriteriaSatisfied(criteriaProperty.Value, itemProperty))
                                    {
                                        groupIsValid = false;
                                        break;
                                    }
                                }
                            }
                            if (groupIsValid)
                            {
                                if (bestGroup == null)
                                {
                                    bestGroup = itemGroup;
                                }
                                else
                                {
                                    var groupComparison = 0;
                                    foreach (var criteriaProperty in criteriaEntry.Properties)
                                    {
                                        if (criteriaProperty.Value == null)
                                        {
                                            continue;
                                        }
                                        else
                                        {
                                            var bestGroupValue = bestGroup.Properties[criteriaProperty.Key];
                                            var itemGroupValue = itemGroup.Properties[criteriaProperty.Key];
                                            var propertyDefinition = definition.PropertyDefinitions[criteriaProperty.Key];
                                            groupComparison = propertyDefinition.Compare(criteriaProperty.Value, bestGroupValue, itemGroupValue);
                                            if (groupComparison != 0)
                                            {
                                                break;
                                            }
                                        }
                                    }
                                    if (groupComparison > 0)
                                    {
                                        bestGroup = itemGroup;
                                    }
                                }
                            }
                        }
                        if (bestGroup != null)
                        {
                            return bestGroup;
                        }
                    }
                }
            }
            return null;
        }

        private IEnumerable<ContentItem> FindItemsImplementation(PatternSet definition, IEnumerable<Asset> assets)
        {
            var pathPatterns = definition.PathExpressions;

            foreach (var asset in assets)
            {
                var path = asset.Path;

                foreach (var pathPattern in pathPatterns)
                {
                    if (StringComparer.OrdinalIgnoreCase.Equals(pathPattern.Folder, asset.Folder))
                    {
                        var contentItem = pathPattern.Match(path, definition.PropertyDefinitions);
                        if (contentItem != null)
                        {
                            yield return contentItem;
                            break;
                        }
                    }
                }
            }
        }

        private void FindItemsImplementation(PatternSet definition, List<Asset> assets, IList<ContentItem> contentItems)
        {
            var pathPatterns = definition.PathExpressions;

            foreach (var asset in assets)
            {
                var path = asset.Path;

                foreach (var pathPattern in pathPatterns)
                {
                    if (StringComparer.OrdinalIgnoreCase.Equals(pathPattern.Folder, asset.Folder))
                    {
                        var contentItem = pathPattern.Match(path, definition.PropertyDefinitions);
                        if (contentItem != null)
                        {
                            contentItems.Add(contentItem);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Get the root folder of the path.
        /// </summary>
        private static string GetFolder(string path)
        {
            for (var i = 1; i < path.Length; i++)
            {
                if (path[i] == '/')
                {
                    return path.Substring(0, i);
                }
            }

            return null;
        }

        private class GroupComparer : IEqualityComparer<ContentItem>
        {
            public static readonly GroupComparer DefaultComparer = new GroupComparer();

            public int GetHashCode(ContentItem obj)
            {
                var hashCode = 0;
                foreach (var property in obj.Properties)
                {
                    hashCode ^= property.Key.GetHashCode();
                    hashCode ^= property.Value.GetHashCode();
                }
                return hashCode;
            }

            public bool Equals(ContentItem x, ContentItem y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x.Properties.Count != y.Properties.Count)
                {
                    return false;
                }

                foreach (var xProperty in x.Properties)
                {
                    object yValue;
                    if (!y.Properties.TryGetValue(xProperty.Key, out yValue))
                    {
                        return false;
                    }
                    if (!Equals(xProperty.Value, yValue))
                    {
                        return false;
                    }
                }

                foreach (var yProperty in y.Properties)
                {
                    object xValue;
                    if (!x.Properties.TryGetValue(yProperty.Key, out xValue))
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
