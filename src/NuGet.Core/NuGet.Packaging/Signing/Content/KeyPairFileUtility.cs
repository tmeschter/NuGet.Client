// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NuGet.Common;

namespace NuGet.Packaging.Signing
{
    public static class KeyPairFileUtility
    {
        /// <summary>
        /// Max file size.
        /// </summary>
        public const int MaxSize = 1024 * 1024;

        /// <summary>
        /// -Hash
        /// </summary>
        public static readonly string DashHash = "-Hash";

        /// <summary>
        /// File encoding.
        /// </summary>
        public static readonly Encoding Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// Throw if the expected value does not exist.
        /// </summary>
        public static string GetValueOrThrow(Dictionary<string, string> values, string key)
        {
            if (values.TryGetValue(key, out var value))
            {
                return value;
            }

            throw new SignatureException($"Missing expected key: {key}");
        }

        /// <summary>
        /// Reads all known hash algorithms and hash values.
        /// </summary>
        public static List<HashNameValuePair> GetHashes(Dictionary<string, string> section)
        {
            var hashes = new List<HashNameValuePair>(1);

            foreach (var hashEntry in section.Where(e => IsHashKey(e.Key)))
            {
                var hashAlgorithm = GetHashAlgorithmNameFromKey(hashEntry.Key);

                // Future hash algorithms will be unknown, these should be skipped.
                if (hashAlgorithm != HashAlgorithmName.Unknown)
                {
                    hashes.Add(new HashNameValuePair(hashAlgorithm, Convert.FromBase64String(hashEntry.Value)));
                }
            }

            return hashes;
        }

        /// <summary>
        /// True if the key ends with -HASH
        /// </summary>
        public static bool IsHashKey(string key)
        {
            return (key?.EndsWith(DashHash, StringComparison.Ordinal) == true);
        }

        public static HashAlgorithmName GetHashAlgorithmNameFromKey(string hashKey)
        {
            var hashAlgorithmName = HashAlgorithmName.Unknown;

            // Verify the key contains -HASH
            if (IsHashKey(hashKey))
            {
                // Remove -HASH
                var withoutDashHash = hashKey.Substring(0, hashKey.Length - DashHash.Length);

                // Parse hash algorithm name
                hashAlgorithmName = GetHashAlgorithmName(withoutDashHash);
            }

            return hashAlgorithmName;
        }

        /// <summary>
        /// Parse an algorithm name.
        /// </summary>
        public static HashAlgorithmName GetHashAlgorithmName(string hashAlgorithmName)
        {
            Enum.TryParse<HashAlgorithmName>(hashAlgorithmName, ignoreCase: false, result: out var parsedHashAlgorithm);
            return parsedHashAlgorithm;
        }
    }
}
