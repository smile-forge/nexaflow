using System.IO;
using System.Security.Cryptography;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Images.Editing.Recipe;

/// <summary>
/// Identifies the exact image a recipe was made against, so a photo replaced or re-saved by another program is
/// noticed instead of having someone else's crop applied to it.
/// </summary>
/// <remarks>
/// Size and write time answer the common case without reading the file. When they differ — a copy that did not keep
/// the timestamp — the content hash decides, and it reads only the first and last 64 KB, which is where every format
/// keeps its header, its metadata and the end of its pixel data.
/// </remarks>
[LocalDiskOnly("fingerprints a photo that is a file on this disk, the only kind a recipe sidecar or a kept file is known by content for")]
public sealed record SourceFingerprint(long Length, DateTime LastWriteUtc, string ContentHash)
{
    private const int SampleBytes = 64 * 1024;

    public static SourceFingerprint Of(string path)
    {
        var info = new FileInfo(path);
        return new SourceFingerprint(info.Length, info.LastWriteTimeUtc, HashOf(path, info.Length));
    }

    /// <summary>True when <paramref name="path"/> still holds the image this fingerprint was taken from.</summary>
    public bool Matches(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != Length) return false;
        if (info.LastWriteTimeUtc == LastWriteUtc) return true;
        return string.Equals(HashOf(path, info.Length), ContentHash, StringComparison.Ordinal);
    }

    private static string HashOf(string path, long length)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[SampleBytes];

        AppendRange(stream, sha, buffer, 0);
        if (length > SampleBytes) AppendRange(stream, sha, buffer, Math.Max(SampleBytes, length - SampleBytes));

        return Convert.ToHexString(sha.GetHashAndReset());
    }

    private static void AppendRange(Stream stream, IncrementalHash sha, byte[] buffer, long offset)
    {
        stream.Position = offset;
        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        sha.AppendData(buffer, 0, read);
    }
}
