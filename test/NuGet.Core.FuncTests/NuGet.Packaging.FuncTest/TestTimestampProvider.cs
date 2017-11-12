// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Security.Cryptography.X509Certificates;
using NuGet.Packaging.Signing;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Test.Utility.Signing;

namespace NuGet.Packaging.FuncTest
{
    public class TestTimestampProvider : Rfc3161TimestampProvider
    {
        public X509Certificate2 TsaCert { get; set; }

        public TestTimestampProvider(Uri timeStampServerUrl) : base(timeStampServerUrl)
        {
        }

        internal override Rfc3161TimestampToken GetTimestampToken(Rfc3161TimestampRequest rfc3161TimestampRequest)
        {
            var bcTsaCert = DotNetUtilities.FromX509Certificate(TsaCert);

            var generator = TestTimestampUtility.GenerateTimestampGenerator(
                SigningTestUtility.GetPrivateKeyParameter(TsaCert),
                bcTsaCert,
                Oids.Sha256Oid,
                Oids.BaselineTimestampPolicyOid);

            generator.SetAccuracyMillis(10);

            var bouncyCastleRequest = rfc3161TimestampRequest.BouncyCastleTimestampRequest();
            var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), new SecureRandom());
            var token = generator.Generate(bouncyCastleRequest, serialNumber, DateTime.Now);
            return Rfc3161TimestampToken.LoadOnly(token.GetEncoded());
        }
    }
}
