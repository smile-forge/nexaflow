using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace Nexaflow.IO.Pe.Internal;

/// <summary>
/// Authenticode. Two independent halves, deliberately kept apart:
/// <list type="bullet">
/// <item>the <em>embedded</em> signature in the security data directory, decoded here with
/// <see cref="SignedCms"/> — this is what the file claims about itself;</item>
/// <item>the OS <em>verdict</em> from <see cref="WinTrust"/> — the only thing that settles trust, and
/// the only route that sees catalog-signed drivers, which have no embedded signature at all.</item>
/// </list>
/// A file can therefore be trusted with nothing to decode, or carry a beautiful signature the OS
/// rejects. Both cases render correctly.
/// </summary>
internal static class SecurityParser
{
    /// <summary>WIN_CERT_TYPE_PKCS_SIGNED_DATA — the only certificate type Authenticode uses.</summary>
    private const ushort PkcsSignedData = 0x0002;

    private const string SigningTimeOid     = "1.2.840.113549.1.9.5";
    private const string CodeSigningEkuOid  = "1.3.6.1.5.5.7.3.3";

    /// <summary>
    /// The two attribute OIDs an RFC 3161 timestamp token can hang off. Authenticode in practice
    /// uses Microsoft's <c>szOID_RFC3161_counterSign</c>, <b>not</b> the PKCS#9 one — every
    /// Microsoft-signed binary on a stock Windows install carries the former — so checking only the
    /// standard OID finds no timestamp anywhere.
    /// </summary>
    private static readonly string[] Rfc3161TimestampOids =
    [
        "1.3.6.1.4.1.311.3.3.1",        // szOID_RFC3161_counterSign (Microsoft)
        "1.2.840.113549.1.9.16.2.14",   // id-aa-timeStampToken (PKCS#9)
    ];

    public static PeSecurity Parse(PeImage image, PeReadOptions options)
    {
        var directory = image.Directory(PeDirectory.Security);
        bool hasDirectory = directory is { IsPresent: true };

        var security = new PeSecurity { HasSecurityDirectory = hasDirectory };

        if (hasDirectory)
            security = DecodeEmbedded(image, directory!, security);

        if (options.VerifySignature && image.Path is { } path && OperatingSystem.IsWindows())
        {
            var result = WinTrust.Verify(path);
            security = security with
            {
                Verdict       = result.Verdict,
                VerdictDetail = result.Detail,
                CatalogPath   = result.CatalogPath,
            };
        }
        else if (!hasDirectory && options.VerifySignature)
        {
            security = security with
            {
                Verdict       = PeTrustVerdict.Unsigned,
                VerdictDetail = "The file carries no embedded signature.",
            };
        }

        return security;
    }

    private static PeSecurity DecodeEmbedded(PeImage image, PeDataDirectory directory, PeSecurity security)
    {
        // Unique to this directory: VirtualAddress is a file offset, not an RVA.
        long offset = directory.VirtualAddress;
        var  buf    = image.Buffer;

        if (!buf.InRange(offset, 8))
        {
            image.Add(new PeDiagnostic(PeSeverity.Warning, "Security",
                "The certificate table points past the end of the file.", offset));
            return security with { Verdict = PeTrustVerdict.Malformed };
        }

        buf.TryU32(offset,     out uint length);
        buf.TryU16(offset + 6, out ushort certificateType);

        if (length <= 8 || !buf.InRange(offset, length))
        {
            image.Add(new PeDiagnostic(PeSeverity.Warning, "Security",
                $"The WIN_CERTIFICATE header declares {length} bytes, which does not fit in the file.", offset));
            return security with { Verdict = PeTrustVerdict.Malformed };
        }

        if (certificateType != PkcsSignedData)
        {
            image.Add(new PeDiagnostic(PeSeverity.Info, "Security",
                $"Certificate type 0x{certificateType:X4} is not PKCS#7 signed data; it was not decoded.", offset));
            return security;
        }

        var blob = buf.ToArray(offset + 8, length - 8);
        if (blob.Length == 0) return security with { Verdict = PeTrustVerdict.Malformed };

        try
        {
            var cms = new SignedCms();
            cms.Decode(blob);

            var signer = cms.SignerInfos.Count > 0 ? cms.SignerInfos[0] : null;
            if (signer?.Certificate is not { } certificate)
            {
                image.Add(new PeDiagnostic(PeSeverity.Warning, "Security",
                    "The signature contains no signer certificate."));
                return security with { Verdict = PeTrustVerdict.Malformed };
            }

            var (signingTime, counterSigners) = ReadCounterSignatures(signer);

            return security with
            {
                Signer          = Describe(certificate),
                SigningTime     = signingTime,
                CounterSigners  = counterSigners,
                DigestAlgorithm = FriendlyDigest(signer.DigestAlgorithm),
                Chain           = BuildChain(image, certificate, cms.Certificates, signingTime),
            };
        }
        catch (CryptographicException e)
        {
            image.Add(new PeDiagnostic(PeSeverity.Warning, "Security",
                $"The embedded signature could not be decoded: {e.Message}", offset));
            return security with { Verdict = PeTrustVerdict.Malformed };
        }
    }

    /// <summary>
    /// Pulls the countersigner certificates and the signing time. Both timestamp styles are handled:
    /// the legacy Authenticode countersignature carries a <c>signingTime</c> attribute directly,
    /// while a modern RFC 3161 token hides the time in a nested TSTInfo — which matters, because
    /// without it every correctly-timestamped older binary validates against today's clock and reads
    /// as expired.
    /// </summary>
    private static (DateTimeOffset?, IReadOnlyList<PeCertificateInfo>) ReadCounterSignatures(SignerInfo signer)
    {
        DateTimeOffset? signingTime = null;
        var             signers     = new List<PeCertificateInfo>();

        foreach (var counter in signer.CounterSignerInfos)
        {
            if (counter.Certificate is { } certificate) signers.Add(Describe(certificate));

            foreach (var attribute in counter.SignedAttributes)
            {
                if (attribute.Oid?.Value != SigningTimeOid || attribute.Values.Count == 0) continue;
                try
                {
                    var time = new Pkcs9SigningTime(attribute.Values[0].RawData);
                    signingTime ??= new DateTimeOffset(time.SigningTime.ToUniversalTime(), TimeSpan.Zero);
                }
                catch (CryptographicException) { /* a malformed attribute is not worth failing over */ }
            }
        }

        signingTime ??= ReadRfc3161Timestamp(signer, signers);
        return (signingTime, signers);
    }

    /// <summary>
    /// Decodes the RFC 3161 timestamp token carried as an unsigned attribute, returning its
    /// authoritative generation time and appending the timestamp authority's certificates.
    /// </summary>
    private static DateTimeOffset? ReadRfc3161Timestamp(SignerInfo signer, List<PeCertificateInfo> signers)
    {
        foreach (var attribute in signer.UnsignedAttributes)
        {
            if (attribute.Oid?.Value is not { } oid ||
                !Rfc3161TimestampOids.Contains(oid) || attribute.Values.Count == 0) continue;
            try
            {
                if (!Rfc3161TimestampToken.TryDecode(attribute.Values[0].RawData, out var token, out _) ||
                    token is null)
                    continue;

                foreach (var certificate in token.AsSignedCms().Certificates)
                    if (signers.All(s => s.Thumbprint != certificate.Thumbprint))
                        signers.Add(Describe(certificate));

                return token.TokenInfo.Timestamp;
            }
            catch (CryptographicException) { /* fall through: an undecodable token just means no time */ }
        }
        return null;
    }

    private static IReadOnlyList<PeChainElement> BuildChain(
        PeImage image, X509Certificate2 leaf, X509Certificate2Collection embedded, DateTimeOffset? signingTime)
    {
        try
        {
            using var chain = new X509Chain();

            // Revocation is left to WinVerifyTrust, which consults the cache and cannot block here.
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid(CodeSigningEkuOid));
            chain.ChainPolicy.ExtraStore.AddRange(embedded);

            // Validate as at signing time when we know it — that is the whole point of a timestamp,
            // and without it every correctly-timestamped old binary would read as expired.
            if (signingTime is { } when) chain.ChainPolicy.VerificationTime = when.UtcDateTime;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

            chain.Build(leaf);

            var elements = new List<PeChainElement>(chain.ChainElements.Count);
            foreach (var element in chain.ChainElements)
            {
                var messages = element.ChainElementStatus
                                      .Where(s => s.Status != X509ChainStatusFlags.NoError)
                                      .Select(s => s.StatusInformation.Trim())
                                      .Where(m => m.Length > 0)
                                      .ToArray();
                elements.Add(new PeChainElement(Describe(element.Certificate), messages));
            }
            return elements;
        }
        catch (CryptographicException e)
        {
            image.Add(new PeDiagnostic(PeSeverity.Info, "Security",
                $"The certificate chain could not be built: {e.Message}"));
            return [];
        }
    }

    private static PeCertificateInfo Describe(X509Certificate2 certificate) => new(
        certificate.Subject,
        certificate.Issuer,
        certificate.Thumbprint,
        certificate.SerialNumber,
        new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero),
        new DateTimeOffset(certificate.NotAfter.ToUniversalTime(),  TimeSpan.Zero),
        certificate.SignatureAlgorithm.FriendlyName ?? certificate.SignatureAlgorithm.Value ?? "unknown",
        certificate.RawData);

    private static string? FriendlyDigest(Oid oid)
        => oid.FriendlyName ?? oid.Value;
}
