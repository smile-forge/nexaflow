namespace Nexaflow.IO.Pe;

/// <summary>The Windows trust verdict for an image.</summary>
public enum PeTrustVerdict
{
    /// <summary>Not evaluated (verification was not requested, or is unavailable off-Windows).</summary>
    NotChecked,
    /// <summary>No embedded signature and no catalog entry.</summary>
    Unsigned,
    /// <summary>Signed and trusted.</summary>
    Valid,
    /// <summary>Signed, but the chain does not terminate in a trusted root.</summary>
    Untrusted,
    /// <summary>Signed, but the signing certificate has expired and no valid countersignature covers it.</summary>
    Expired,
    /// <summary>Signed with a revoked certificate.</summary>
    Revoked,
    /// <summary>A signature is present but could not be parsed, or the digest does not match the file.</summary>
    Malformed,
}

/// <summary>A flattened certificate, so the model stays a plain value type with no disposable state.
/// <see cref="RawData"/> lets a UI rebuild an <c>X509Certificate2</c> on demand for the system dialog.</summary>
public sealed record PeCertificateInfo(
    string          Subject,
    string          Issuer,
    string          Thumbprint,
    string          SerialNumber,
    DateTimeOffset  NotBefore,
    DateTimeOffset  NotAfter,
    string          SignatureAlgorithm,
    byte[]          RawData)
{
    public bool IsExpired => DateTimeOffset.UtcNow > NotAfter;

    /// <summary>The CN= portion of the subject, which is what a user recognises.</summary>
    public string CommonName
    {
        get
        {
            foreach (var part in Subject.Split(','))
            {
                var t = part.Trim();
                if (t.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)) return t[3..].Trim('"');
            }
            return Subject;
        }
    }
}

/// <summary>One link in the built certificate chain.</summary>
public sealed record PeChainElement(PeCertificateInfo Certificate, IReadOnlyList<string> StatusMessages)
{
    public bool IsOk => StatusMessages.Count == 0;
}

/// <summary>
/// Authenticode state. Note the two independent halves: an <em>embedded</em> signature lives in the
/// security data directory, while a driver is usually <em>catalog</em>-signed and has no embedded
/// signature at all — so <see cref="HasSecurityDirectory"/> being false does not mean unsigned.
/// Only <see cref="Verdict"/>, which comes from the OS, settles it.
/// </summary>
public sealed record PeSecurity
{
    public static readonly PeSecurity NotChecked = new();

    public bool                            HasSecurityDirectory { get; init; }
    public PeTrustVerdict                  Verdict              { get; init; } = PeTrustVerdict.NotChecked;
    public string?                         VerdictDetail        { get; init; }

    /// <summary>The <c>.cat</c> that vouched for this file, when trust came from a catalog rather
    /// than an embedded signature. Null for embedded signatures and for unsigned files.</summary>
    public string? CatalogPath { get; init; }

    /// <summary>Trust came from a security catalog, not from bytes inside the file. The norm for
    /// drivers and for a good deal of in-box Windows.</summary>
    public bool IsCatalogSigned => CatalogPath is { Length: > 0 };

    public PeCertificateInfo?              Signer         { get; init; }
    public DateTimeOffset?                 SigningTime    { get; init; }
    public IReadOnlyList<PeCertificateInfo> CounterSigners { get; init; } = [];
    public IReadOnlyList<PeChainElement>   Chain          { get; init; } = [];

    /// <summary>The digest algorithm the signature used, e.g. "sha256".</summary>
    public string? DigestAlgorithm { get; init; }

    public bool IsSigned => Verdict is not (PeTrustVerdict.Unsigned or PeTrustVerdict.NotChecked);
}
