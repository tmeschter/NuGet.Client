// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Test.Utility.Signing
{
    public class ChainCertificateRequest
    {
        public string CrlServerBaseUri { get; set; }

        public bool IsCA { get; set; }

        public TestCertificate Issuer { get; set; }

        public string IssuerDN => Issuer?.Cert.Subject;
    }
}