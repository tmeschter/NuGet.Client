// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using NuGet.Common;

namespace NuGet.Packaging.Signing
{
    public class KeyPairFileWriter : IDisposable
    {
        private readonly StreamWriter _writer;

        public KeyPairFileWriter(Stream stream, bool leaveOpen)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            _writer = new StreamWriter(stream, KeyPairFileUtility.Encoding, bufferSize: 8192, leaveOpen: leaveOpen);
        }

        /// <summary>
        /// Write key:value with EOL to the manifest stream.
        /// </summary>
        public void WritePair(string key, string value)
        {
            _writer.Write(FormatItem(key, value));
            WriteEOL();
        }

        /// <summary>
        /// Write HashName-Hash:hash with EOL to the manifest stream.
        /// </summary>
        public void WritePair(HashNameValuePair hashPair)
        {
            _writer.Write(FormatHashValue(hashPair));
            WriteEOL();
        }

        /// <summary>
        /// Write an empty line.
        /// </summary>
        public void WriteSectionBreak()
        {
            WriteEOL();
        }

        /// <summary>
        /// Write an end of line to the manifest writer.
        /// </summary>
        private void WriteEOL()
        {
            _writer.Write('\n');
        }

        /// <summary>
        /// key:value
        /// </summary>
        private static string FormatItem(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException(null, nameof(key));
            }

            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException(null, nameof(value));
            }

            return $"{key}:{value}";
        }

        /// <summary>
        /// HashName-Hash:hash
        /// </summary>
        private static string FormatHashValue(HashNameValuePair hashPair)
        {
            var hashKeyName = FormatHashKey(hashPair.HashAlgorithmName);
            var hash = Convert.ToBase64String(hashPair.HashValue);

            // {HashName}-HASH:hash
            return FormatItem(hashKeyName, hash);
        }

        /// <summary>
        /// Creates 'HashName-Hash'
        /// </summary>
        private static string FormatHashKey(HashAlgorithmName hashAlgorithmName)
        {
            var hashName = hashAlgorithmName.ToString().ToUpperInvariant();
            return $"{hashName}-Hash";
        }

        public void Dispose()
        {
            _writer.Dispose();
        }
    }
}
