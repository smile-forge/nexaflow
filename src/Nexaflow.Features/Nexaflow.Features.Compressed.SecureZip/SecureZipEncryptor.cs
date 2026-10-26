using System;
using System.IO;
using ICSharpCode.SharpZipLib.Zip;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Compressed.SecureZip;

/// <summary>Whole-archive AES-256 encryption for ZIP, backed by SharpZipLib. Encrypt rewrites a plain
/// zip with a password; Decrypt reads the encrypted zip with the password and writes a plain copy.</summary>
public sealed class SecureZipEncryptor : IArchiveEncryptor
{
    public string Name => "Zip AES-256";

    public bool CanEncrypt(string fileName)
        => Path.GetExtension(fileName).Equals(".zip", StringComparison.OrdinalIgnoreCase);

    public void Encrypt(Stream source, Stream destination, string password)
        => Copy(source, sourcePassword: null, destination, destPassword: password);

    public void Decrypt(Stream source, Stream destination, string password)
        => Copy(source, sourcePassword: password, destination, destPassword: null);

    /// <summary>Streams every file entry from one zip to another, applying or removing AES encryption. Both streams stay
    /// the caller's: neither is closed here.</summary>
    private static void Copy(Stream sourceStream, string? sourcePassword, Stream destination, string? destPassword)
    {
        using var source = new ZipFile(sourceStream, leaveOpen: true);
        if (sourcePassword is not null) source.Password = sourcePassword;

        using var zos = new ZipOutputStream(destination) { IsStreamOwner = false };
        if (destPassword is not null) zos.Password = destPassword;
        zos.SetLevel(6);

        var buffer = new byte[81920];
        foreach (ZipEntry entry in source)
        {
            if (!entry.IsFile) continue;
            var outEntry = new ZipEntry(entry.Name)
            {
                DateTime = entry.DateTime,
                AESKeySize = destPassword is null ? 0 : 256,   // 0 = no encryption
            };
            zos.PutNextEntry(outEntry);
            using (var input = source.GetInputStream(entry))
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    zos.Write(buffer, 0, read);
            }
            zos.CloseEntry();
        }
        zos.Finish();
    }
}
