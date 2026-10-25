using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Features.Compressed.Services;

/// <summary>
/// Format-agnostic <b>detached</b> signing: hashes the whole archive and signs the hash with a per-user
/// ECDSA (P-256) key, storing the result in a <c>&lt;archive&gt;.sig</c> sidecar. The archive and its sidecar
/// are read and written through the runtime's file system, wherever the archive is; the key stays in the app's
/// config folder. Verification recomputes the hash and checks it against the embedded public key.
/// (Content-specific signing — APK/JAR — is a later, separate concern.)
/// </summary>
public sealed class ArchiveSignatureService
{
    private const string Algorithm = "ECDSA-P256-SHA256";
    private readonly IVirtualFileSystem _vfs;
    private readonly string _keyDir;

    public ArchiveSignatureService(IVirtualFileSystem vfs) : this(vfs, DefaultKeyDir()) { }

    /// <summary>Test seam: keep the signing key in an explicit directory.</summary>
    internal ArchiveSignatureService(IVirtualFileSystem vfs, string keyDir)
    {
        _vfs    = vfs;
        _keyDir = keyDir;
    }

    private static string DefaultKeyDir()
    {
        // Respect an isolated config root (used by dev/test) the same way ConfigManager does.
        var baseDir = Environment.GetEnvironmentVariable("NEXAFLOW_CONFIG_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Smile", "nexaflow");
        return Path.Combine(baseDir, "compressed");
    }

    public async Task<bool> HasSignatureAsync(string archivePath, CancellationToken ct = default)
        => await _vfs.GetEntryAsync(SigPath(archivePath), ct) is { IsDirectory: false };

    /// <summary>Signs the archive and writes its <c>.sig</c> sidecar; returns the signer fingerprint.</summary>
    public async Task<string> SignAsync(string archivePath, CancellationToken ct = default)
    {
        var hash = await HashArchiveAsync(archivePath, ct);
        using var ecdsa = LoadOrCreateKey();
        var sig = ecdsa.SignHash(hash);
        var pub = ecdsa.ExportSubjectPublicKeyInfo();
        var record = new SignatureRecord(
            Algorithm, Convert.ToBase64String(sig), Convert.ToBase64String(pub), Fingerprint(pub));
        await _vfs.WriteAllTextAsync(SigPath(archivePath), JsonSerializer.Serialize(record), ct: ct);
        return record.Fingerprint;
    }

    /// <summary>Verifies the archive against its sidecar. Returns whether it is valid and the signer
    /// fingerprint (null when there is no sidecar).</summary>
    public async Task<(bool Valid, string? Fingerprint)> VerifyAsync(string archivePath, CancellationToken ct = default)
    {
        if (!await HasSignatureAsync(archivePath, ct)) return (false, null);

        SignatureRecord? record;
        try { record = JsonSerializer.Deserialize<SignatureRecord>(await _vfs.ReadAllTextAsync(SigPath(archivePath), ct: ct)); }
        catch { return (false, null); }
        if (record is null) return (false, record?.Fingerprint);

        try
        {
            var hash = await HashArchiveAsync(archivePath, ct);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(record.PublicKey), out _);
            bool ok = ecdsa.VerifyHash(hash, Convert.FromBase64String(record.Signature));
            return (ok, record.Fingerprint);
        }
        catch { return (false, record.Fingerprint); }
    }

    private async Task<byte[]> HashArchiveAsync(string archivePath, CancellationToken ct)
    {
        await using var stream = await _vfs.OpenReadAsync(archivePath, ReadIntent.Sequential, ct);
        return await SHA256.HashDataAsync(stream, ct);
    }

    [LocalDiskOnly("the per-user signing key, in the app's config folder")]
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
