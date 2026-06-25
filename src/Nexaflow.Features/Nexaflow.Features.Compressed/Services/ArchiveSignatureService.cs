using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Nexaflow.Features.Compressed.Services;

/// <summary>
/// Format-agnostic <b>detached</b> signing: hashes the whole archive and signs the hash with a per-user
/// ECDSA (P-256) key, storing the result in a <c>&lt;archive&gt;.sig</c> sidecar. Verification recomputes
/// the hash and checks it against the embedded public key. (Content-specific signing — APK/JAR — is a
/// later, separate concern.)
/// </summary>
public sealed class ArchiveSignatureService
{
    private const string Algorithm = "ECDSA-P256-SHA256";
    private readonly string _keyDir;

    public ArchiveSignatureService() : this(DefaultKeyDir()) { }

    /// <summary>Test seam: keep the signing key in an explicit directory.</summary>
    internal ArchiveSignatureService(string keyDir) => _keyDir = keyDir;

    private static string DefaultKeyDir()
    {
        // Respect an isolated config root (used by dev/test) the same way ConfigManager does.
        var baseDir = Environment.GetEnvironmentVariable("NEXAFLOW_CONFIG_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Smile", "nexaflow");
        return Path.Combine(baseDir, "compressed");
    }

    public bool HasSignature(string archivePath) => File.Exists(SigPath(archivePath));

    /// <summary>Signs the archive and writes its <c>.sig</c> sidecar; returns the signer fingerprint.</summary>
    public string Sign(string archivePath)
    {
        using var ecdsa = LoadOrCreateKey();
        var sig = ecdsa.SignHash(HashArchive(archivePath));
        var pub = ecdsa.ExportSubjectPublicKeyInfo();
        var record = new SignatureRecord(
            Algorithm, Convert.ToBase64String(sig), Convert.ToBase64String(pub), Fingerprint(pub));
        File.WriteAllText(SigPath(archivePath), JsonSerializer.Serialize(record));
        return record.Fingerprint;
    }

    /// <summary>Verifies the archive against its sidecar. Returns whether it is valid and the signer
    /// fingerprint (null when there is no sidecar).</summary>
    public (bool Valid, string? Fingerprint) Verify(string archivePath)
    {
        var sigPath = SigPath(archivePath);
        if (!File.Exists(sigPath)) return (false, null);

        SignatureRecord? record;
        try { record = JsonSerializer.Deserialize<SignatureRecord>(File.ReadAllText(sigPath)); }
        catch { return (false, null); }
        if (record is null) return (false, record?.Fingerprint);

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(record.PublicKey), out _);
            bool ok = ecdsa.VerifyHash(HashArchive(archivePath), Convert.FromBase64String(record.Signature));
            return (ok, record.Fingerprint);
        }
        catch { return (false, record.Fingerprint); }
    }

    private static byte[] HashArchive(string archivePath)
    {
        using var fs = File.OpenRead(archivePath);
        return SHA256.HashData(fs);
    }

    private ECDsa LoadOrCreateKey()
    {
        Directory.CreateDirectory(_keyDir);
        var keyPath = Path.Combine(_keyDir, "signing-key.bin");
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (File.Exists(keyPath))
            ecdsa.ImportECPrivateKey(File.ReadAllBytes(keyPath), out _);
        else
            File.WriteAllBytes(keyPath, ecdsa.ExportECPrivateKey());
        return ecdsa;
    }

    private static string SigPath(string archivePath) => archivePath + ".sig";

    private static string Fingerprint(byte[] publicKey) => Convert.ToHexString(SHA256.HashData(publicKey))[..16];

    private sealed record SignatureRecord(string Algorithm, string Signature, string PublicKey, string Fingerprint);
}
