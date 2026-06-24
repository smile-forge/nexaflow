namespace Nexaflow.IO.Common;

/// <summary>
/// A pluggable whole-archive (password) encryption backend, discovered by reflection like
/// <see cref="IArchiveHandler"/>. Kept separate from the read/browse handlers because an encrypted
/// archive can't be browsed without the password — encrypt/decrypt are one-shot operations driven from
/// the Compressed tab, which supplies the password.
/// </summary>
public interface IArchiveEncryptor
{
    /// <summary>Backend display name (e.g. <c>"Zip AES"</c>).</summary>
    string Name { get; }

    /// <summary>True when this backend can encrypt the given file's format (e.g. <c>.zip</c>).</summary>
    bool CanEncrypt(string fileName);

    /// <summary>Writes an encrypted copy of <paramref name="sourcePath"/> to <paramref name="destPath"/>.</summary>
    void Encrypt(string sourcePath, string destPath, string password);

    /// <summary>Writes a decrypted (plain) copy of the encrypted <paramref name="sourcePath"/>.</summary>
    void Decrypt(string sourcePath, string destPath, string password);
}
