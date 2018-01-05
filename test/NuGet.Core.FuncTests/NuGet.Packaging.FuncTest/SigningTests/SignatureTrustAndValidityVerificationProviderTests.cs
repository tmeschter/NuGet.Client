// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if IS_DESKTOP

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NuGet.Common;
using NuGet.Packaging.Signing;
using NuGet.Test.Utility;
using Test.Utility.Signing;
using Xunit;

namespace NuGet.Packaging.FuncTest
{
    [Collection("Signing Functional Test Collection")]
    public class SignatureTrustAndValidityVerificationProviderTests
    {
        private SigningSpecifications _specification => SigningSpecifications.V1;

        private SigningTestFixture _testFixture;
        private TrustedTestCert<TestCertificate> _trustedTestCert;
        private IList<ISignatureVerificationProvider> _trustProviders;

        public SignatureTrustAndValidityVerificationProviderTests(SigningTestFixture fixture)
        {
            _testFixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
            _trustedTestCert = _testFixture.TrustedTestCertificate;
            _trustProviders = new List<ISignatureVerificationProvider>()
            {
                new SignatureTrustAndValidityVerificationProvider()
            };
        }

        [CIOnlyFact]
        public async Task VerifySignedPackage_ValidCertificate_Success()
        {
            // Arrange
            var nupkg = new SimpleTestPackageContext();
            using (var dir = TestDirectory.Create())
            using (var testCertificate = new X509Certificate2(_trustedTestCert.Source.Cert))
            {
                var signedPackagePath = await SignedArchiveTestUtility.CreateSignedPackageAsync(testCertificate, nupkg, dir);
                var verifier = new PackageSignatureVerifier(_trustProviders, SignedPackageVerifierSettings.VerifyCommandDefaultPolicy);
                using (var packageReader = new PackageArchiveReader(signedPackagePath))
                {
                    // Act
                    var result = await verifier.VerifySignaturesAsync(packageReader, CancellationToken.None);
                    var resultsWithErrors = result.Results.Where(r => r.GetErrorIssues().Any());

                    // Assert
                    result.Valid.Should().BeTrue();
                    resultsWithErrors.Count().Should().Be(0);
                }
            }
        }

        [CIOnlyFact]
        public async Task VerifySignedPackage_ValidCertificateAndTimestamp_Success()
        {
            // Arrange
            var nupkg = new SimpleTestPackageContext();
            using (var dir = TestDirectory.Create())
            using (var testCertificate = new X509Certificate2(_trustedTestCert.Source.Cert))
            {
                var signedPackagePath = await SignedArchiveTestUtility.CreateSignedAndTimeStampedPackageAsync(testCertificate, nupkg, dir);
                var verifier = new PackageSignatureVerifier(_trustProviders, SignedPackageVerifierSettings.VerifyCommandDefaultPolicy);
                using (var packageReader = new PackageArchiveReader(signedPackagePath))
                {
                    // Act
                    var result = await verifier.VerifySignaturesAsync(packageReader, CancellationToken.None);
                    var resultsWithErrors = result.Results.Where(r => r.GetErrorIssues().Any());

                    // Assert
                    result.Valid.Should().BeTrue();
                    resultsWithErrors.Count().Should().Be(0);
                }
            }
        }

        [CIOnlyFact]
        public async Task GetTrustResultAsync_WithInvalidSignature_Throws()
        {
            var package = new SimpleTestPackageContext();

            using (var directory = TestDirectory.Create())
            using (var testCertificate = new X509Certificate2(_trustedTestCert.Source.Cert))
            {
                var packageFilePath = await SignedArchiveTestUtility.CreateSignedAndTimeStampedPackageAsync(testCertificate, package, directory);

                using (var packageReader = new PackageArchiveReader(packageFilePath))
                {
                    var signature = (await packageReader.GetSignaturesAsync(CancellationToken.None)).Single();
                    var invalidSignature = SignedArchiveTestUtility.GenerateInvalidSignature(signature);
                    var provider = new SignatureTrustAndValidityVerificationProvider();

                    var result = await provider.GetTrustResultAsync(
                        packageReader,
                        invalidSignature,
                        SignedPackageVerifierSettings.Default,
                        CancellationToken.None);

                    var issue = result.Issues.FirstOrDefault(log => log.Code == NuGetLogCode.NU3012);

                    Assert.NotNull(issue);
                    Assert.Equal("Primary signature validation failed.", issue.Message);
                }
            }
        }

        [CIOnlyFact]
        public async Task GetTrustResultAsync_WithNoSigningCertificate_Throws()
        {
            var package = new SimpleTestPackageContext();

            using (var directory = TestDirectory.Create())
            using (var testCertificate = new X509Certificate2(_trustedTestCert.Source.Cert))
            {
                var packageFilePath = await SignedArchiveTestUtility.CreateSignedAndTimeStampedPackageAsync(testCertificate, package, directory);

                using (var packageReader = new PackageArchiveReader(packageFilePath))
                {
                    var signature = (await packageReader.GetSignaturesAsync(CancellationToken.None)).Single();
                    var signatureWithNoCertificates = SignedArchiveTestUtility.GenerateSignatureWithNoCertificates(signature);
                    var provider = new SignatureTrustAndValidityVerificationProvider();

                    var result = await provider.GetTrustResultAsync(
                        packageReader,
                        signatureWithNoCertificates,
                        SignedPackageVerifierSettings.Default,
                        CancellationToken.None);

                    var issue = result.Issues.FirstOrDefault(log => log.Code == NuGetLogCode.NU3010);

                    Assert.NotNull(issue);
                    Assert.Equal("The primary signature does not have a signing certificate.", issue.Message);
                }
            }
        }
    }
}
#endif