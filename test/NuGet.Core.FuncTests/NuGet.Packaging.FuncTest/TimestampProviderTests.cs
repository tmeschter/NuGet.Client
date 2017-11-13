// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging.Signing;
using NuGet.Test.Utility;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.X509;
using Test.Utility.Signing;
using Xunit;

namespace NuGet.Packaging.FuncTest
{
    public class TimestampProviderTests
    {
        [Fact]
        public void Rfc3161TimestampProvider_SuccessAsync()
        {
            Debugger.Launch();

            var testLogger = new TestLogger();
            var timestampProvider = new TestTimestampProvider(new Uri("http://func.test"));
            var authorCertName = "author@nuget.func.test";
            var authorCertCAName = "authorCA@nuget.func.test";
            var tsaCertName = "tsa@nuget.func.test";
            var tsaCertCAName = "tsaCA@nuget.func.test";
            var data = "Test data to be signed and timestamped";

            Action<X509V3CertificateGenerator> actionGenerator = delegate (X509V3CertificateGenerator gen)
            {
                var usages = new[] { KeyPurposeID.IdKPTimeStamping };

                gen.AddExtension(
                    X509Extensions.ExtendedKeyUsage.Id,
                    critical: true,
                    extensionValue: new ExtendedKeyUsage(usages));
            };

            using (var authorCACert = SigningTestUtility.GenerateCertificate(authorCertCAName, modifyGenerator: null))
            using (var tsaCACert = SigningTestUtility.GenerateCertificate(tsaCertCAName, modifyGenerator: null))
            using (var authorCert = SigningTestUtility.GenerateCertificate(authorCertName, modifyGenerator: null))
            using (var tsaCert = SigningTestUtility.GenerateCertificate(tsaCertName, tsaCACert, modifyGenerator: actionGenerator))
            {
                var signedCms = SigningTestUtility.GenerateSignedCms(authorCert, Encoding.ASCII.GetBytes(data));
                var signatureValue = signedCms.Encode();

                var request = new TimestampRequest
                {
                    Certificate = authorCert,
                    SigningSpec = SigningSpecifications.V1,
                    TimestampHashAlgorithm = Common.HashAlgorithmName.SHA256,
                    SignatureValue = signatureValue
                };

                timestampProvider.TsaCert = tsaCert;
                timestampProvider.TsaCACert = tsaCACert;
                timestampProvider.SignatureValueHash = signatureValue;

                var timestampedSignature = timestampProvider.TimestampSignatureAsync(request, new TestLogger(), CancellationToken.None);
            }
        }
    }
}
