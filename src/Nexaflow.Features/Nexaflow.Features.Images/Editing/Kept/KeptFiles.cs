using System.IO;
using System.Security.Cryptography;
using System.Text;
using Nexaflow.Features.Images.Editing.Recipe;

namespace Nexaflow.Features.Images.Editing.Kept;

/// <summary>
/// What a picture tool made for a photo, kept on disk so it is made once: each file found again by the photo's content
/// and a line saying what was made of it and by which models — after a restart too — and, past the budget, the least
/// recently used let go first.
/// </summary>
public sealed class KeptFiles(string folder, long budgetBytes, string extension)
{
    public string Folder => folder;

    /// <summary>The app's cache folder for what is kept under <paramref name="name"/>.</summary>
    public static string CacheFolder(string name)
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smile", "nexaflow", "cache", name);

    /// <summary>
    /// Where what <paramref name="made"/> describes, made of <paramref name="photo"/>, is kept: by the photo's content, so
    /// the same photo moved keeps it and a photo changed on disk has it made afresh.
    /// </summary>
    public string PathOf(string photo, string made)
    {
        string content = $"vfs:{photo}";   // a photo inside an archive: by where it is
        if (File.Exists(photo))
        {
            var print = SourceFingerprint.Of(photo);
            content = $"{print.Length}:{print.ContentHash}";
        }
        var key = Encoding.UTF8.GetBytes($"{content}|{made}");
        return Path.Combine(folder, Convert.ToHexStringLower(SHA256.HashData(key))[..32] + extension);
    }

    /// <summary>What is kept, if it is here — marked as just used.</summary>
    public string? Find(string photo, string made)
    {
        var path = PathOf(photo, made);
        if (!File.Exists(path)) return null;
        Touch(path);
        return path;
    }

    /// <summary>Takes <paramref name="written"/> as what is kept at <paramref name="path"/>, letting the oldest go past the budget.</summary>
    public void Keep(string written, string path)
    {
        File.Move(written, path, overwrite: true);
        Prune(path);
    }

    /// <summary>Marks what is kept at <paramref name="path"/> as just used.</summary>
    public static void Touch(string path)
    {
        try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch (IOException) { /* read-only or in use: it is still found */ }
    }

    /// <summary>Lets the least recently used go while they pass the budget, never <paramref name="keep"/>.</summary>
    private void Prune(string keep)
    {
        var kept = new DirectoryInfo(folder).GetFiles("*" + extension).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        long total = 0;
        foreach (var file in kept)
        {
            total += file.Length;
            if (total <= budgetBytes || string.Equals(file.FullName, Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase)) continue;
            try { file.Delete(); } catch (IOException) { /* in use by a viewer: next time */ }
        }
    }
}
