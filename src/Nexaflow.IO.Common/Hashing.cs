using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.IO.Common;

/// <summary>
/// Lower-case hex digests behind the editor's Checksum commands. The string overloads hash in-memory
/// text; the file overloads stream from disk, so a whole-file checksum works at any size — and on the
/// read-only raw viewer, whose in-memory document is windowed rather than the full file.
/// </summary>
public static class Hashing
{
    public static string Md5(string text, Encoding? encoding = null)
        => Convert.ToHexStringLower(MD5.HashData((encoding ?? Encoding.UTF8).GetBytes(text)));

    public static string Sha256(string text, Encoding? encoding = null)
        => Convert.ToHexStringLower(SHA256.HashData((encoding ?? Encoding.UTF8).GetBytes(text)));

    public static async Task<string> Md5FileAsync(string path, CancellationToken ct = default)
    {
        await using var fs = OpenRead(path);
        return Convert.ToHexStringLower(await MD5.HashDataAsync(fs, ct));
    }

    public static async Task<string> Sha256FileAsync(string path, CancellationToken ct = default)
    {
        await using var fs = OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, ct));
    }

    private static FileStream OpenRead(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, useAsync: true);
}
