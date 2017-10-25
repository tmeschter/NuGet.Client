// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace NuGet.Common
{
    /// <summary>
    /// Contains a hash value and the name of the algorithm used to create it.
    /// </summary>
    public class HashNameValuePair
    {
        /// <summary>
        /// Algorithm name.
        /// </summary>
        public HashAlgorithmName HashAlgorithmName { get; }

        /// <summary>
        /// Hash value.
        /// </summary>
        public byte[] HashValue { get; }

        /// <summary>
        /// Create a HashNameValuePair.
        /// </summary>
        /// <param name="hashAlgorithmName">Algorithm used to create <see cref="hashValue"/>.</param>
        /// <param name="hashValue">Hash value.</param>
        public HashNameValuePair(HashAlgorithmName hashAlgorithmName, byte[] hashValue)
        {
            HashAlgorithmName = hashAlgorithmName;
            HashValue = hashValue ?? throw new ArgumentNullException(nameof(hashValue));
        }
    }
}
