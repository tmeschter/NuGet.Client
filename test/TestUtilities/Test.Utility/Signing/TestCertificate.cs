// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Ocsp;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Test.Utility.Signing
{
    /// <summary>
    /// Test certificate pair.
    /// </summary>
    public class TestCertificate
    {
        /// <summary>
        /// Cert
        /// </summary>
        public X509Certificate2 Cert { get; set; }

        /// <summary>
        /// Issuer Cert
        /// </summary>
        public TestCertificate Issuer { get; set; }

        /// <summary>
        /// Public cert.
        /// </summary>
        public X509Certificate2 PublicCert => SigningTestUtility.GetPublicCert(Cert);

        /// <summary>
        /// Public cert.
        /// </summary>
        public X509Certificate2 PublicCertWithPrivateKey => SigningTestUtility.GetPublicCertWithPrivateKey(Cert);

#if IS_DESKTOP
        /// <summary>
        /// Org.BouncyCastle.X509.X509Certificate.
        /// </summary>
        public Org.BouncyCastle.X509.X509Certificate BouncyCastleCert => DotNetUtilities.FromX509Certificate(Cert);

        /// <summary>
        /// Org.BouncyCastle.X509.X509Certificate.
        /// </summary>
        public AsymmetricCipherKeyPair KeyPair => DotNetUtilities.GetKeyPair(Cert.PrivateKey);
#endif

        /// <summary>
        /// Certificate Revocation Status.
        /// </summary>
        public CertificateStatus Status { get; set; }

        /// <summary>
        /// Trust the PublicCert cert for the life of the object.
        /// </summary>
        /// <remarks>Dispose of the object returned!</remarks>
        public TrustedTestCert<TestCertificate> WithTrust(StoreName storeName = StoreName.TrustedPeople, StoreLocation storeLocation = StoreLocation.CurrentUser)
        {
            return new TrustedTestCert<TestCertificate>(this, e => PublicCert, storeName, storeLocation);
        }

        /// <summary>
        /// Trust the PublicCert cert for the life of the object.
        /// </summary>
        /// <remarks>Dispose of the object returned!</remarks>
        public TrustedTestCert<TestCertificate> WithPrivateKeyAndTrust(StoreName storeName = StoreName.TrustedPeople, StoreLocation storeLocation = StoreLocation.CurrentUser)
        {
            return new TrustedTestCert<TestCertificate>(this, e => PublicCertWithPrivateKey, storeName, storeLocation);
        }

        public static TestCertificate Generate(Action<X509V3CertificateGenerator> modifyGenerator = null, ChainCertificateRequest chainCertificateRequest = null)
        {
            var certName = "NuGetTest-" + Guid.NewGuid().ToString();
            var cert = SigningTestUtility.GenerateCertificate(certName, modifyGenerator, chainCertificateRequest: chainCertificateRequest);

            var testCertificate = new TestCertificate
            {
                Cert = cert,
                Issuer = chainCertificateRequest?.Issuer,
                Status = CertificateStatus.Good
            };

            return testCertificate;
        }
    }
}
