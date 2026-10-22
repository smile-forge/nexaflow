using System.IO;
using Nexaflow.Core.Services.Agent;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Generation;

/// <summary>The kinds of file the generation tools make, each with a home of its own.</summary>
internal enum MadeKind
{
    Picture,
    Recording,
    Video,
}

/// <summary>A file a generation tool made: where it is, and whether it is a draft in a temp folder, not yet saved by the user.</summary>
internal readonly record struct MadeFile(string Path, bool Draft);

/// <summary>
/// Where a made file goes and how it is shown — only into what the user has given the assistant. Asked for a folder, or a
/// file whose extension becomes that of what was made, it is saved there when that is inside a folder open in a tab; asked
/// for nowhere, it is kept in a temp folder and opened as a draft (<see cref="DraftFile"/>) that the user saves where they
/// choose. It is named from what was asked for and never written over a file already there.
/// </summary>
/// <param name="scope">What the user has given the assistant: the pictures a tool may work from, the folders it may save in.</param>
/// <param name="drafts">The temp folder drafts are kept in, each in a folder of its own.</param>
/// <param name="offered">Where a draft's Save as starts, for each kind.</param>
/// <param name="show">Opens a saved file's path, or a <see cref="DraftFile"/>, as <see cref="IShellServices.HandleObject"/> does.</param>
internal sealed class GeneratedFiles(PathScope scope, string drafts, Func<MadeKind, string> offered, Func<object, Task> show)
{
    /// <summary>How many words of a prompt name its file.</summary>
    private const int NameWords = 6;

    /// <summary>The longest name taken from a prompt, before its extension.</summary>
    private const int NameLength = 60;

    /// <summary>Into what <paramref name="scope"/> holds, else a draft whose Save as starts in the user's Pictures, Music or
    /// Videos; opened in the workspace's own viewers.</summary>
    public static GeneratedFiles For(IShellServices shell, PathScope scope) => new(
        scope,
        DraftLocation.Root,
        kind => kind switch
        {
            MadeKind.Picture   => KnownFolderService.PicturesPath,
            MadeKind.Recording => KnownFolderService.MusicPath,
            _                  => KnownFolderService.VideosPath,
        },
        made => shell.RunOnUiAsync(() => shell.HandleObject(made)));

    /// <summary>
    /// Why a made thing cannot go where <paramref name="asked"/> says, or null where it can: nowhere asked, or a full path
    /// inside a folder open in a tab. Asked before anything is made, so nothing is made only to be thrown away.
    /// </summary>
    public string? Refusal(string? asked)
    {
        if (string.IsNullOrWhiteSpace(asked)) return null;
        if (!Path.IsPathFullyQualified(asked.Trim())) return $"'path' is a full path inside a folder open in a tab; '{asked}' is not a full path.";
        return scope.ResolveInFolder(asked) is null ? RunScope.OutsideToWrite(asked) : null;
    }

    /// <summary>
    /// Saves <paramref name="media"/> where <paramref name="asked"/> says when that is inside a folder open in a tab, and
    /// anywhere else — nowhere asked, or a place <see cref="Refusal"/> turned away — keeps it as a draft; then opens it.
    /// </summary>
    /// <param name="about">What was asked for — the prompt, or the text read out — which names the file.</param>
    public async Task<MadeFile> KeepAsync(MediaBytes media, MadeKind kind, string? asked, string about, CancellationToken ct)
    {
        var extension = Extension(media.MimeType);
        if (!string.IsNullOrWhiteSpace(asked) && scope.ResolveInFolder(Target(asked, about, extension)) is { } target)
        {
            var saved = await SaveAsync(media.Bytes, target, ct);
            await show(saved);
            return new MadeFile(saved, Draft: false);
        }

        var draft = await SaveAsync(media.Bytes, DraftLocation.NewFile(NameFrom(about) + extension, drafts), ct);
        await show(new DraftFile(draft, offered(kind)));
        return new MadeFile(draft, Draft: true);
    }

    /// <summary>
    /// Saves <paramref name="markdown"/> — a document made from the one at <paramref name="from"/>, a summary or a translation
    /// of it — where <paramref name="asked"/> says when that is inside a folder open in a tab, and anywhere else keeps it as a
    /// draft named <paramref name="name"/>, beside copies of what its links and pictures name beside <paramref name="from"/> so
    /// they still show, its Save as starting in <paramref name="from"/>'s folder; then opens it.
    /// </summary>
    public async Task<MadeFile> KeepDocumentAsync(string markdown, string? asked, string name, string from, string? attachments, CancellationToken ct)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(markdown);
        var folder = Path.GetDirectoryName(Path.GetFullPath(from))!;
        var files = VirtualFileSystem.Instance;

        if (!string.IsNullOrWhiteSpace(asked) && scope.ResolveInFolder(Target(asked, Path.GetFileNameWithoutExtension(name), ".md")) is { } target)
        {
            var saved = await SaveAsync(bytes, target, ct);
            await show(saved);
            return new MadeFile(saved, Draft: false);
        }

        var draft = await SaveAsync(bytes, DraftLocation.NewFile(name, drafts), ct);
        var beside = Path.GetDirectoryName(draft)!;

        foreach (var attached in Nexaflow.Markdown.Packaging.MarkdownAttachments.Paths(markdown))
        {
            // Read where the document reads it — beside it, or inside its package — so a draft shows what the document shows.
            var source = Path.Combine(attachments ?? folder, attached);
            if (!files.Exists(source)) continue;

            var copy = Path.Combine(beside, attached);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            await File.WriteAllBytesAsync(copy, files.ReadAllBytes(source), ct);
        }

        await show(new DraftFile(draft, folder));
        return new MadeFile(draft, Draft: true);
    }

    /// <summary>
    /// The file a made thing is meant for, made full: a folder, or a path ending in a separator, gets a name from
    /// <paramref name="about"/>; a file takes <paramref name="extension"/>. Null for anything that is not a full path.
    /// </summary>
    internal static string? Target(string asked, string about, string extension)
    {
        var trimmed = asked.Trim();
        if (PathScope.Full(trimmed) is not { } full) return null;
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

    /// <summary>A picture the model named, to work from — one open in a tab, or in a folder open in one — read whole,
    /// with its type from its extension.</summary>
    internal MediaBytes? ReadPicture(string path, out string? problem)
    {
        problem = null;
        if (scope.Resolve(path) is not { } full) { problem = RunScope.Outside(path); return null; }
        if (!File.Exists(full)) { problem = $"There is no file at '{path}'."; return null; }

        var type = Path.GetExtension(full).ToLowerInvariant() switch
        {
            ".png"            => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp"           => "image/webp",
            _                 => null,
        };
        if (type is null) { problem = $"'{Path.GetFileName(full)}' is not a PNG, JPEG or WebP picture."; return null; }
        return new MediaBytes(File.ReadAllBytes(full), type);
    }

    /// <summary>What a tool says once a file is made: where it went to the model and the file to the conversation — or, for
    /// a draft, only that it is open for the user — and the picture itself where there is one, for the model to see what it
    /// made.</summary>
    internal static ToolResult Made(MadeFile file, string? note, ContextImage? picture)
    {
        var said = file.Draft
            ? "Opened it for the user in a new tab, not saved yet: they save it where they choose."
            : $"Saved to {file.Path} and opened it.";
        if (!string.IsNullOrWhiteSpace(note)) said += $"\nThe model said: {note}";
        return ToolResult.Ok($"made {Path.GetFileName(file.Path)}", said) with
        {
            Attachments = file.Draft ? null : [file.Path],
            Images      = picture is null ? null : [picture],
        };
    }
}
