// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NuGet.Packaging.Signing;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tsp;
using Test.Utility.Signing;

namespace NuGet.Packaging.FuncTest
{
    public static class TestTimestampUtility
    {
        public static TimeStampTokenGenerator GenerateTimestampGenerator(
            AsymmetricKeyParameter keypair,
            Org.BouncyCastle.X509.X509Certificate cert,
            string digestOID,
            string tsaPolicyOID)
        {
            return new TimeStampTokenGenerator(keypair, cert, digestOID, tsaPolicyOID);
        }

        internal static TimeStampRequest BouncyCastleTimestampRequest(this Rfc3161TimestampRequest request)
        {
            return new TimeStampRequest(request.RawData);
        }
    }
}
