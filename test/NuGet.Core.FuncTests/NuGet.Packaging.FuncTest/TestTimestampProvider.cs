// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using NuGet.Packaging.Signing;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tsp;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509.Store;
using Test.Utility.Signing;

namespace NuGet.Packaging.FuncTest
{
    public class TestTimestampProvider : Rfc3161TimestampProvider
    {
        public X509Certificate2 TsaCert { get; set; }

        public X509Certificate2 TsaCACert { get; set; }

        public byte[] SignatureValueHash { get; set; }

        public TestTimestampProvider(Uri timeStampServerUrl) : base(timeStampServerUrl)
        {
        }

        internal override Rfc3161TimestampToken GetTimestampToken(Rfc3161TimestampRequest rfc3161TimestampRequest)
        {
            var bcTsaCert = DotNetUtilities.FromX509Certificate(TsaCert);

            var reqGenerator = new TimeStampRequestGenerator();

            reqGenerator.SetCertReq(certReq: true);

            var bouncyCastleRequest = reqGenerator.Generate(Oids.Sha256Oid, new byte[32], new BigInteger("100"));

            var tokenGenerator = TestTimestampUtility.GenerateTimestampTokenGenerator(
                SigningTestUtility.GetPrivateKeyParameter(TsaCert),
                bcTsaCert,
                Oids.Sha256Oid,
                Oids.BaselineTimestampPolicyOid);

            tokenGenerator.SetAccuracyMillis(10);

            var storeFactory = X509StoreFactory.Create("TestStore", new X509CollectionStoreParameters(new List<Org.BouncyCastle.X509.X509Certificate>() { DotNetUtilities.FromX509Certificate(TsaCACert) }));

            tokenGenerator.SetCertificates(storeFactory);

            var respGenerator = TestTimestampUtility.GenerateTimestampResponseGenerator(tokenGenerator, TspAlgorithms.Allowed);

            var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), new SecureRandom());

            var response = respGenerator.Generate(bouncyCastleRequest, serialNumber, DateTime.Now);

            var token = tokenGenerator.Generate(bouncyCastleRequest, serialNumber, DateTime.Now);

            return Rfc3161TimestampToken.LoadOnly(response.GetEncoded());
        }
    }
}
