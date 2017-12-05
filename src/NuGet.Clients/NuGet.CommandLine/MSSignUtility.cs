using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Commands;
using NuGet.Common;
using NuGet.Packaging.Signing;
using NuGet.Protocol;

namespace NuGet.CommandLine
{
    public static class MSSignUtility
    {
        private static Common.HashAlgorithmName ValidateAndParseHashAlgorithm(string value, string name, SigningSpecifications spec)
        {
            var hashAlgorithm = Common.HashAlgorithmName.SHA256;

            if (!string.IsNullOrEmpty(value))
            {
                if (!spec.AllowedHashAlgorithms.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(string.Format(CultureInfo.CurrentCulture,
                        NuGetCommand.SignCommandInvalidArgumentException,
                        name));
                }

                hashAlgorithm = CryptoHashUtility.GetHashAlgorithmName(value);
            }

            if (hashAlgorithm == Common.HashAlgorithmName.Unknown)
            {
                throw new ArgumentException(string.Format(CultureInfo.CurrentCulture,
                        NuGetCommand.SignCommandInvalidArgumentException,
                        name));
            }

            return hashAlgorithm;
        }

        private static IEnumerable<string> GetPackages(string packages)
        {
            // resolve path into multiple packages if needed.
            var packagesToSign = LocalFolderUtility.ResolvePackageFromPath(packages);
            LocalFolderUtility.EnsurePackageFileExists(packages, packagesToSign);

            return packagesToSign;
        }

        private static SignPackageRequest GetSignRequest(X509Certificate2Collection certCollection, X509Certificate2 cert, CngKey privateKey, string timestamper, string hashAlgorithmName,  string timestampHashAlgorithmName, ILogger logger)
        {
            WarnIfNoTimestamper(logger, timestamper);

            var signingSpec = SigningSpecifications.V1;
            var hashAlgorithm = ValidateAndParseHashAlgorithm(hashAlgorithmName, nameof(hashAlgorithmName), signingSpec);
            var timestampHashAlgorithm = ValidateAndParseHashAlgorithm(timestampHashAlgorithmName, nameof(timestampHashAlgorithmName), signingSpec);

            return new SignPackageRequest()
            {
                SignatureHashAlgorithm = hashAlgorithm,
                TimestampHashAlgorithm = timestampHashAlgorithm,
                Certificate = cert,
                PrivateKey = privateKey,
                AdditionalCertificates = certCollection
            };
        }

        private static void WarnIfNoTimestamper(ILogger logger, string timestamper)
        {
            if (string.IsNullOrEmpty(timestamper))
            {
                logger.Log(LogMessage.CreateWarning(NuGetLogCode.NU3521, NuGetCommand.SignCommandNoTimestamperWarning));
            }
        }

        public static Task<int> MssignAsync(
            string packages,
            X509Certificate2Collection certCollection,
            X509Certificate2 cert,
            CngKey privateKey,
            string timestamper,
            string hashAlgorithmName,
            string timestampHashAlgorithmName,
            ILogger logger,
            string outputDirectory,
            bool overwrite)
        {
            var signRequest = GetSignRequest(certCollection, cert, privateKey, timestamper, hashAlgorithmName, timestampHashAlgorithmName, logger);
            var signCommandRunner = new SignCommandRunner();

            return signCommandRunner.ExecuteCommandAsync(
                GetPackages(packages), signRequest, timestamper, logger, outputDirectory, overwrite, CancellationToken.None);
        }
    }
}
