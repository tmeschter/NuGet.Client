// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using NuGet.Common;
using NuGet.Shared;

namespace NuGet.Packaging.Signing
{
    /// <summary>
    /// Represents a file in a package that is listed in the signed manifest.
    /// </summary>
    public sealed class PackageContentManifestFileEntry
    {
        /// <summary>
        /// Path value in the manifest.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Hash values in the manifest.
        /// </summary>
        public IReadOnlyList<HashNameValuePair> Hashes { get; }

        public PackageContentManifestFileEntry(string path, IEnumerable<HashNameValuePair> hashes)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(null, nameof(path));
            }

            if (hashes == null)
            {
                throw new ArgumentNullException(nameof(hashes));
            }

            Path = path;
            Hashes = hashes.AsList().AsReadOnly();
        }
    }
}
