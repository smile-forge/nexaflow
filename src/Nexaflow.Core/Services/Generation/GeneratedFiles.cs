using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Generation;

/// <summary>The kinds of file the generation tools make, each with a home of its own.</summary>
internal enum MadeKind
{
    Picture,
    Recording,
    Video,
}

/// <summary>
/// Where a made file goes and how it is shown. It is saved where the model asked — a folder, or a file whose
/// extension becomes that of what was made — else in the user's own folder for its kind, under Nexaflow; it is named
/// from what was asked for and never written over a file already there; then it is opened as a double-click would.
/// </summary>
internal sealed class GeneratedFiles(Func<MadeKind, string> home, Func<string, Task> show)
{
    /// <summary>The folder under Pictures, Music and Videos that made files go to.</summary>
    private const string Folder = "Nexaflow";

    /// <summary>How many words of a prompt name its file.</summary>
    private const int NameWords = 6;

    /// <summary>The longest name taken from a prompt, before its extension.</summary>
    private const int NameLength = 60;

    /// <summary>Into the user's Pictures, Music and Videos, opened in the workspace's own viewers.</summary>
    public static GeneratedFiles For(IShellServices shell) => new(
        kind => Path.Combine(kind switch
        {
            MadeKind.Picture   => KnownFolderService.PicturesPath,
            MadeKind.Recording => KnownFolderService.MusicPath,
            _                  => KnownFolderService.VideosPath,
        }, Folder),
        path => shell.RunOnUiAsync(() => shell.HandleObject(path)));

    /// <summary>Saves <paramref name="media"/> where <paramref name="asked"/> says, opens it, and returns where it went.</summary>
    /// <param name="about">What was asked for — the prompt, or the text read out — which names the file.</param>
    public async Task<string> KeepAsync(MediaBytes media, MadeKind kind, string? asked, string about, CancellationToken ct)
    {
        var path = await SaveAsync(media.Bytes, Target(asked, home(kind), about, Extension(media.MimeType)), ct);
        await show(path);
        return path;
    }

    /// <summary>
    /// The file a made thing is meant for. A relative path is taken from <paramref name="home"/>; a folder, or a path
    /// ending in a separator, gets a name from <paramref name="about"/>.
    /// </summary>
    internal static string Target(string? asked, string home, string about, string extension)
    {
        if (string.IsNullOrWhiteSpace(asked)) return Path.Combine(home, NameFrom(about) + extension);

        var trimmed = asked.Trim();
        var full    = Path.GetFullPath(trimmed, home);
        return Directory.Exists(full) || Path.EndsInDirectorySeparator(trimmed)
            ? Path.Combine(full, NameFrom(about) + extension)
            : Path.ChangeExtension(full, extension);
    }

    /// <summary>A file name from the first words of <paramref name="about"/>, or "generated" where it has none.</summary>
    internal static string NameFrom(string about)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var words   = about.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                           .Select(w => new string([.. w.Where(c => char.IsLetterOrDigit(c) || c is '-' or '\'').Where(c => !invalid.Contains(c))]))
                           .Where(w => w.Length > 0)
                           .Take(NameWords);
        var name = string.Join(' ', words);
        if (name.Length > NameLength) name = name[..NameLength].TrimEnd();
        return name.Length > 0 ? name : "generated";
    }

    /// <summary>The extension a made file of <paramref name="mimeType"/> is saved with.</summary>
    internal static string Extension(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/jpeg"                 => ".jpg",
        "image/webp"                 => ".webp",
        "audio/wav" or "audio/x-wav" => ".wav",
        "video/mp4"                  => ".mp4",
        "video/webm"                 => ".webm",
        _                            => ".png",
    };

    /// <summary>Writes a new file at <paramref name="path"/>, or at "name (2)", "name (3)"… when it is taken.</summary>
    private static async Task<string> SaveAsync(byte[] bytes, string path, CancellationToken ct)
    {
        var folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext  = Path.GetExtension(path);

        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? path : Path.Combine(folder, $"{stem} ({n}){ext}");
            try
            {
                await using var file = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await file.WriteAsync(bytes, ct);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Taken — by an earlier file or a call running beside this one; try the next name.
            }
        }
    }

    /// <summary>A picture the model named, to work from: read whole, with its type from its extension.</summary>
    internal static MediaBytes? ReadPicture(string path, out string? problem)
    {
        problem = null;
        if (!Path.IsPathFullyQualified(path)) { problem = $"'{path}' is not a full path."; return null; }
        if (!File.Exists(path)) { problem = $"There is no file at '{path}'."; return null; }

        var type = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png"            => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp"           => "image/webp",
            _                 => null,
        };
        if (type is null) { problem = $"'{Path.GetFileName(path)}' is not a PNG, JPEG or WebP picture."; return null; }
        return new MediaBytes(File.ReadAllBytes(path), type);
    }

    /// <summary>What a tool says once a file is made: the file to the conversation, where it went to the model, and
    /// the picture itself where there is one, for the model to see what it made.</summary>
    internal static ToolResult Made(string path, string? note, ContextImage? picture)
    {
        var said = $"Saved to {path} and opened it.";
        if (!string.IsNullOrWhiteSpace(note)) said += $"\nThe model said: {note}";
        return ToolResult.Ok($"made {Path.GetFileName(path)}", said) with
        {
            Attachments = [path],
            Images      = picture is null ? null : [picture],
        };
    }
}
