using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// The companion files every feature has declared (<see cref="IFileCompanionProvider"/>), and what the file browser
/// does with them: hides a companion beside its primary, badges the primary, and makes every operation on the
/// primary take its companions along.
/// </summary>
/// <remarks>
/// One per shell, like <see cref="FileSystemFeatureRegistry"/>, but reachable from the shell alone — the operation
/// queue, delete and paste know nothing else. A companion is the primary's whole name plus a suffix, so everything
/// here is arithmetic on names; whether a file is one is decided against its folder, never assumed from the name.
/// </remarks>
public sealed class FileCompanions
{
    private static readonly ConditionalWeakTable<IShellServices, FileCompanions> PerShell = new();

    public static FileCompanions None { get; } = new([]);

    public static FileCompanions For(IShellServices shell) => PerShell.GetValue(shell, Discover);

    private FileCompanions(IReadOnlyList<FileCompanionKind> kinds)
    {
        Kinds    = kinds;
        Suffixes = [.. kinds.Select(k => k.Suffix).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public IReadOnlyList<FileCompanionKind> Kinds { get; }

    public IReadOnlyList<string> Suffixes { get; }

    /// <summary>Why the companions of files handed to Windows are looked for on the disk directly: what goes to the
    /// clipboard, a drag-out or the Recycle Bin is a real path, which only ever names a file on this machine.</summary>
    private const string ToWindows = "files handed to Windows — the clipboard, a drag-out, the Recycle Bin — which are on this disk";

    /// <summary>A browser with these kinds of companion — for a test, or a caller that declares its own.</summary>
    public static FileCompanions Of(IReadOnlyList<FileCompanionKind> kinds) => new(kinds);

    private static FileCompanions Discover(IShellServices shell)
    {
        var kinds = new List<FileCompanionKind>();
        foreach (var type in shell.DiscoverImplementations<IFileCompanionProvider>())
        {
            if (type.GetConstructor(Type.EmptyTypes) is null) continue;
            if (Activator.CreateInstance(type) is IFileCompanionProvider provider) kinds.AddRange(provider.Companions);
        }
        return new FileCompanions(kinds);
    }

    // ── Names ───────────────────────────────────────────────────────────────

    /// <summary>The kind <paramref name="name"/> would be a companion of, and its primary's name; null if no suffix fits.</summary>
    public (FileCompanionKind Kind, string PrimaryName)? Parse(string name)
    {
        foreach (var kind in Kinds)
            if (name.Length > kind.Suffix.Length && name.EndsWith(kind.Suffix, StringComparison.OrdinalIgnoreCase))
                return (kind, name[..^kind.Suffix.Length]);
        return null;
    }

    /// <summary>The companions of the file at <paramref name="primaryPath"/> that exist.</summary>
    [LocalDiskOnly(ToWindows)]
    public IEnumerable<string> ExistingCompanionsOf(string primaryPath) =>
        Suffixes.Select(s => primaryPath + s).Where(File.Exists);

    /// <summary>
    /// <paramref name="paths"/> with every existing companion of each file after it — what a clipboard or a drag out
    /// to another program should carry, since it knows nothing of companions.
    /// </summary>
    [LocalDiskOnly(ToWindows)]
    public IReadOnlyList<string> WithCompanions(IEnumerable<string> paths)
    {
        var all = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (seen.Add(path)) all.Add(path);
            if (Suffixes.Count == 0 || !File.Exists(path)) continue;
            foreach (var companion in ExistingCompanionsOf(path))
                if (seen.Add(companion)) all.Add(companion);
        }
        return all;
    }

    /// <summary>
    /// Transfer items for <paramref name="items"/> that carry their companions, without the companions themselves
    /// when they came along as sources too (as they do from a clipboard or another program's drag) — the engine
    /// moves them with their primary, under the primary's resolved name.
    /// </summary>
    public IReadOnlyList<TransferItem> Attach(IReadOnlyList<TransferItem> items)
    {
        if (Suffixes.Count == 0) return items;
        var sources = new HashSet<string>(items.Select(i => i.Source), StringComparer.OrdinalIgnoreCase);
        return
        [
            .. items
                .Where(i => Parse(Path.GetFileName(i.Source)) is not { } c
                         || !sources.Contains(Path.Combine(Path.GetDirectoryName(i.Source) ?? string.Empty, c.PrimaryName)))
                .Select(i => i with { CompanionSuffixes = Suffixes }),
        ];
    }

    /// <summary>As <see cref="Follow"/>, through <paramref name="vfs"/> (<see cref="CompanionFiles.FollowAsync"/>), for a file
    /// that may be on a mount or a connected location.</summary>
    public async Task<IReadOnlyList<(string Path, Exception Error)>> FollowAsync(IVirtualFileSystem vfs, string from, string to,
                                                                                bool move, CancellationToken ct = default) =>
        Suffixes.Count == 0 || await vfs.IsDirectoryAsync(to, ct)
            ? []
            : await CompanionFiles.FollowAsync(vfs, from, to, Suffixes, move, ct);

    // ── Listing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// One folder's listing, as it is enumerated: companions are held back until the folder has been read — a
    /// companion may come before its primary — then dropped where their primary is listed, which is badged.
    /// </summary>
    public CompanionListing Listing() => new(this);
}

/// <summary>Sorts one folder's companions from its files while it is listed. See <see cref="FileCompanions.Listing"/>.</summary>
public sealed class CompanionListing
{
    private readonly FileCompanions _companions;
    private readonly Dictionary<string, ViewModels.FileSystemEntry> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(ViewModels.FileSystemEntry Entry, FileCompanionKind Kind, string PrimaryName)> _held = [];

    internal CompanionListing(FileCompanions companions) => _companions = companions;

    /// <summary>
    /// Takes one entry. True when it is held back — a possible companion, decided by <see cref="Finish"/>; false when
    /// the caller lists it now.
    /// </summary>
    public bool Hold(ViewModels.FileSystemEntry entry)
    {
        if (entry.IsDirectory) return false;
        if (_companions.Parse(entry.Name) is { } parsed)
        {
            _held.Add((entry, parsed.Kind, parsed.PrimaryName));
            return true;
        }
        _files[entry.Name] = entry;
        return false;
    }

    /// <summary>
    /// The folder has been read. Each held companion whose primary was listed badges it and is dropped; the rest —
    /// orphans — are returned, to be listed as ordinary files.
    /// </summary>
    public IReadOnlyList<ViewModels.FileSystemEntry> Finish()
    {
        var orphans = new List<ViewModels.FileSystemEntry>();
        foreach (var (entry, kind, primaryName) in _held)
        {
            if (_files.TryGetValue(primaryName, out var primary))
            {
                if (kind.Badge is not null) primary.Companion = kind;
            }
            else orphans.Add(entry);
        }
        return orphans;
    }
}
