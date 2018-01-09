// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using NuGet.CommandLine.Test;
using NuGet.Packaging.Signing;
using NuGet.Test.Utility;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Ocsp;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Ocsp;
using Test.Utility.Signing;

namespace NuGet.CommandLine.FuncTest.Commands
{
    /// <summary>
    /// Used to bootstrap functional tests for signing.
    /// </summary>
    public class SignCommandTestFixture : IDisposable
    {
        private static readonly string _testTimestampServer = Environment.GetEnvironmentVariable("TIMESTAMP_SERVER_URL");

        private const string _ocspRequestContentType = "application/ocsp-request";
        private const string _ocspResponseContentType = "application/ocsp-response";
        private const int _validCertChainLength = 2;

        private TrustedTestCert<TestCertificate> _trustedTestCert;
        private TrustedTestCert<TestCertificate> _trustedTestCertWithInvalidEku;
        private TrustedTestCert<TestCertificate> _trustedTestCertExpired;
        private TrustedTestCert<TestCertificate> _trustedTestCertNotYetValid;
        private TrustedTestCertificateChain _trustedTestCertChain;
        private IList<ISignatureVerificationProvider> _trustProviders;
        private SigningSpecifications _signingSpecifications;
        private MockServer _crlServer;
        private bool _crlServerRunning;
        private object _crlServerRunningLock = new object();
        private TestDirectory _testDirectory;
        private string _nugetExePath;

        public TrustedTestCert<TestCertificate> TrustedTestCertificate
        {
            get
            {
                if (_trustedTestCert == null)
                {
                    var actionGenerator = SigningTestUtility.CertificateModificationGeneratorForCodeSigningEkuCert;

                    // Code Sign EKU needs trust to a root authority
                    // Add the cert to Root CA list in LocalMachine as it does not prompt a dialog
                    // This makes all the associated tests to require admin privilege
                    _trustedTestCert = TestCertificate.Generate(actionGenerator).WithPrivateKeyAndTrust(StoreName.Root, StoreLocation.LocalMachine);
                }

                return _trustedTestCert;
            }
        }

        public TrustedTestCert<TestCertificate> TrustedTestCertificateWithInvalidEku
        {
            get
            {
                if (_trustedTestCertWithInvalidEku == null)
                {
                    var actionGenerator = SigningTestUtility.CertificateModificationGeneratorForInvalidEkuCert;

                    // Add the cert to Root CA list in LocalMachine as it does not prompt a dialog
                    // This makes all the associated tests to require admin privilege
                    _trustedTestCertWithInvalidEku = TestCertificate.Generate(actionGenerator).WithPrivateKeyAndTrust(StoreName.Root, StoreLocation.LocalMachine);
                }

                return _trustedTestCertWithInvalidEku;
            }
        }

        public TrustedTestCert<TestCertificate> TrustedTestCertificateExpired
        {
            get
            {
                if (_trustedTestCertExpired == null)
                {
                    var actionGenerator = SigningTestUtility.CertificateModificationGeneratorExpiredCert;

                    // Code Sign EKU needs trust to a root authority
                    // Add the cert to Root CA list in LocalMachine as it does not prompt a dialog
                    // This makes all the associated tests to require admin privilege
                    _trustedTestCertExpired = TestCertificate.Generate(actionGenerator).WithPrivateKeyAndTrust(StoreName.Root, StoreLocation.LocalMachine);
                }

                return _trustedTestCertExpired;
            }
        }

        public TrustedTestCert<TestCertificate> TrustedTestCertificateNotYetValid
        {
            get
            {
                if (_trustedTestCertNotYetValid == null)
                {
                    var actionGenerator = SigningTestUtility.CertificateModificationGeneratorNotYetValidCert;

                    // Code Sign EKU needs trust to a root authority
                    // Add the cert to Root CA list in LocalMachine as it does not prompt a dialog
                    // This makes all the associated tests to require admin privilege
                    _trustedTestCertNotYetValid = TestCertificate.Generate(actionGenerator).WithPrivateKeyAndTrust(StoreName.Root, StoreLocation.LocalMachine);
                }

                return _trustedTestCertNotYetValid;
            }
        }

        public TrustedTestCertificateChain TrustedTestCertificateChain
        {
            get
            {
                if (_trustedTestCertChain == null)
                {
                    var certChain = SigningTestUtility.GenerateCertificateChain(_validCertChainLength, CrlServer.Uri, TestDirectory.Path);
                    _trustedTestCertChain = new TrustedTestCertificateChain(certChain);

                    SetUpCrlDistributionPoint();
                }

                return _trustedTestCertChain;
            }
        }

        private void SetUpCrlDistributionPoint()
        {
            lock (_crlServerRunningLock)
            {
                if (!_crlServerRunning)
                {
                    CrlServer.Post.Add(
                        "/",
                        request =>
                        {
                            if (string.Equals(request.ContentType, _ocspRequestContentType, StringComparison.OrdinalIgnoreCase))
                            {
                                var ocspRequest = new OcspReq(request.InputStream);
                                var respId = new RespID(new ResponderID(new X509Name(_trustedTestCertChain.Root.Source.Cert.Subject)));
                                var basicOcspRespGenerator = new BasicOcspRespGenerator(respId);
                                var nonce = ocspRequest.GetExtensionValue(OcspObjectIdentifiers.PkixOcspNonce);
                                var ocspRequestList = ocspRequest.GetRequestList();

                                if (nonce != null)
                                {
                                    var extensions = new X509Extensions(new Dictionary<DerObjectIdentifier, Org.BouncyCastle.Asn1.X509.X509Extension>()
                                    {
                                        { OcspObjectIdentifiers.PkixOcspNonce, new Org.BouncyCastle.Asn1.X509.X509Extension(critical: false, value: nonce) }
                                    });

                                    basicOcspRespGenerator.SetResponseExtensions(extensions);
                                }

                                var currentDateTime = DateTime.UtcNow;
                                var issuer = _trustedTestCertChain.Root.Source;

                                foreach (var ocspReq in ocspRequestList)
                                {
                                    var certificateId = ocspReq.GetCertID();
                                    var certificate = _trustedTestCertChain.GetCertificate(certificateId.SerialNumber.ToString(radix: 16));
                                    CertificateStatus status = new UnknownStatus();

                                    if (certificate != null)
                                    {
                                        status = certificate.Status;
                                        issuer = certificate.Issuer;
                                    }

                                    basicOcspRespGenerator.AddResponse(certificateId, status, thisUpdate: currentDateTime, nextUpdate: currentDateTime.AddDays(1), singleExtensions: null);
                                }

                                var certificateChain = _trustedTestCertChain.Certificates.Select(c => c.Source.BouncyCastleCert).ToArray();
                                var basicOcspResp = basicOcspRespGenerator.Generate("SHA512WITHRSA", issuer.KeyPair.Private, certificateChain, currentDateTime);
                                var ocspRespGenerator = new OCSPRespGenerator();
                                var ocspResp = ocspRespGenerator.Generate(OCSPRespGenerator.Successful, basicOcspResp);
                                var bytes = ocspResp.GetEncoded();

                                return new Action<HttpListenerResponse>(response =>
                                {
                                    response.StatusCode = 200;
                                    response.ContentType = _ocspResponseContentType;
                                    response.ContentLength64 = bytes.Length;
                                    response.OutputStream.Write(bytes, 0, bytes.Length);
                                });

                            }
                            else
                            {
                                return new Action<HttpListenerResponse>(response =>
                                {
                                    response.StatusCode = 400;
                                });
                            }
                        });

                    CrlServer.Start();
                    _crlServerRunning = true;
                }
            }
        }

        public IList<ISignatureVerificationProvider> TrustProviders
        {
            get
            {
                if (_trustProviders == null)
                {
                    _trustProviders = new List<ISignatureVerificationProvider>()
                    {
                        new SignatureTrustAndValidityVerificationProvider(),
                        new IntegrityVerificationProvider()
                    };
                }

                return _trustProviders;
            }
        }

        public SigningSpecifications SigningSpecifications
        {
            get
            {
                if (_signingSpecifications == null)
                {
                    _signingSpecifications = SigningSpecifications.V1;
                }

                return _signingSpecifications;
            }
        }

        public string NuGetExePath
        {
            get
            {
                if (_nugetExePath == null)
                {
                    _nugetExePath = Util.GetNuGetExePath();
                }

                return _nugetExePath;
            }
        }
        public MockServer CrlServer
        {
            get
            {
                if (_crlServer == null)
                {
                    _crlServer = new MockServer();
                }

                return _crlServer;
            }
        }

        public TestDirectory TestDirectory
        {
            get
            {
                if (_testDirectory == null)
                {
                    _testDirectory = TestDirectory.Create();
                }

                return _testDirectory;
            }

        }

        public string Timestamper => _testTimestampServer;


        public void Dispose()
        {
            _trustedTestCert?.Dispose();
            _trustedTestCertWithInvalidEku?.Dispose();
            _trustedTestCertExpired?.Dispose();
            _trustedTestCertNotYetValid?.Dispose();
            _trustedTestCertChain?.Dispose();
            _crlServer?.Stop();
            _crlServer?.Dispose();
            _testDirectory?.Dispose();
        }
    }
}