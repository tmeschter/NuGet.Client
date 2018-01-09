// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Ocsp;
using Org.BouncyCastle.OpenSsl;

namespace Test.Utility.Signing
{
    public class TrustedTestCertificateChain : IDisposable
    {
        private UnknownStatus _unknownStatus = new UnknownStatus();
        private IDictionary<string, TestCertificate> _certLookUp = new Dictionary<string, TestCertificate>(StringComparer.OrdinalIgnoreCase);

        public IList<TrustedTestCert<TestCertificate>> Certificates { get; }

        public TrustedTestCert<TestCertificate> Root => Certificates?.First();

        public TrustedTestCert<TestCertificate> Leaf => Certificates?.Last();

        public TrustedTestCertificateChain(IList<TrustedTestCert<TestCertificate>> certificates)
        {
            if (certificates.Count() < 1)
            {
                throw new InvalidDataException("A certificate chain should have atleast 2 certificates");
            }

            Certificates = certificates;
            var path = @"c:\users\anmishr\desktop";
            var i = 0;

            foreach (var cert in Certificates)
            {
                var filePath = Path.Combine(path, $"test{i++}.cer");

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                File.WriteAllBytes(filePath, cert.Source.Cert.Export(X509ContentType.Cert));

                _certLookUp[cert.Source.Cert.SerialNumber] = cert.Source;
            }
        }

        public TestCertificate GetCertificate(string serialNumber)
        {
            if (_certLookUp.ContainsKey(serialNumber))
            {
                return _certLookUp[serialNumber];
            }
            else
            {
                return null;
            }
        }

        public void Dispose()
        {
            (Certificates as List<TrustedTestCert<TestCertificate>>)?.ForEach(c => c.Dispose());
        }
    }
}
