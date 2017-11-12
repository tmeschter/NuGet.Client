// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using NuGet.Packaging.Signing;
using NuGet.Test.Utility;
using Xunit;

namespace NuGet.Packaging.FuncTest
{
    public class TimestampProviderTests
    {
        [Fact]
        public void Rfc3161TimestampProvider_Success()
        {
            Debugger.Launch();
            var timestampProvider = new TestTimestampProvider(new Uri("http://func.test"));
            var authorCertName = "nuget@func.test";
            var ca1 = new X509Certificate2Builder { SubjectName = $"CN={authorCertName}" }.Build();
            var request = new TimestampRequest
            {
                Certificate = ca1,
                SigningSpec = SigningSpecifications.V1,
                TimestampHashAlgorithm = Common.HashAlgorithmName.SHA256,
                SignatureValue = new Guid().ToByteArray()
            };

            var timestampedSignature = timestampProvider.TimestampSignatureAsync(request, new TestLogger(), CancellationToken.None);


        }
    }
}
