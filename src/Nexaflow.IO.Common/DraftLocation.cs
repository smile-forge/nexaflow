namespace Nexaflow.IO.Common;

/// <summary>
/// Where files made for the user are kept until they save them — a picture drawn, a recording, a clip, a cut-out: each in a
/// folder of its own under the temp folder, so a name never collides and Windows clears what was never saved.
/// </summary>
public static class DraftLocation
{
    /// <summary>The folder every draft's own folder is in.</summary>
    public static string Root => Path.Combine(Path.GetTempPath(), "nexaflow-drafts");

    /// <summary>A path named <paramref name="fileName"/> in a new folder of its own under <paramref name="root"/>, the folder made.</summary>
    public static string NewFile(string fileName, string? root = null)
    {
        var folder = Path.Combine(root ?? Root, Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, fileName);
    }
}
