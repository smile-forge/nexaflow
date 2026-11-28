using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.ClientTools;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Localization;
using Nexaflow.Search;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.Model;

/// <summary>
/// What the assistant is given of a file-system tab: what it shows, the tools that act in it, how far those reach and how
/// risky acting there is — and searching for files, for the assistant and for the person.
/// </summary>
public sealed class FolderAssistance
{
    private readonly FileSystemModel _model;

    public FolderAssistance(FileSystemModel model) => _model = model;

    private FolderLocation Location => _model.Location;

    /// <summary>AI surfaces of the viewlets active for the open folder; empty when none apply.</summary>
    private IReadOnlyList<IViewletAiSurface> _viewletSurfaces = [];

    /// <summary>The active viewlet set changed: their context and tools join this tab's.</summary>
    public void SetActiveViewletSurfaces(IReadOnlyList<IViewletAiSurface> surfaces) => _viewletSurfaces = surfaces;

    /// <summary>What the tab shows, for the assistant. Read on the UI thread, where the loader changes the rows.</summary>
    public string GetContext()
    {
        var entries = _model.Listing.Entries;
        if (Location.IsThisPc)
        {
            var drives = entries.Count;
            return $"File browser — This PC ({drives} drive{(drives == 1 ? "" : "s")}).";
        }
        if (string.IsNullOrEmpty(Location.CurrentPath))
            return "File browser — no location selected.";

        // The entries are named, not only counted: "delete the folder called old" is answered from what is listed.
        var listed  = entries.ToList();
        var folders = listed.Where(e => e.IsDirectory).Select(e => e.Name).ToList();
        var files   = listed.Where(e => !e.IsDirectory).Select(e => e.Name).ToList();
        var sb = new StringBuilder($"File browser at '{Location.CurrentPath}'");
        if (_model.Listing.IsLoadingEntries) sb.Append(" (still listing)");
        sb.Append('\n').Append(Listed("Folders", folders)).Append('\n').Append(Listed("Files", files));

        var selection = _model.Selection;
        if (selection.Count > 0)
        {
            const int cap = 20;
            sb.Append("\nSelected: ").Append(string.Join(", ", selection.Take(cap).Select(e => e.Name)));
            if (selection.Count > cap) sb.Append($" and {selection.Count - cap} more");
        }

        // Append each active viewlet's context line (a .NET solution's build state, a git repo's status…).
        foreach (var surface in _viewletSurfaces)
        {
            var line = surface.GetContext();
            if (!string.IsNullOrWhiteSpace(line))
                sb.Append('\n').Append(line);
        }

        return sb.ToString();
    }

    /// <summary>How many entries of a kind are named before the rest are counted: enough for any folder a request names
    /// things in, few enough that a huge one does not crowd out the request.</summary>
    private const int ListedNames = 150;

    private static string Listed(string kind, IReadOnlyList<string> names)
    {
        if (names.Count == 0) return $"{kind}: none";
        var text = $"{kind} ({names.Count}): " + string.Join(", ", names.Take(ListedNames));
        return names.Count > ListedNames ? text + $" and {names.Count - ListedNames} more — find_files_by_name finds any of them" : text;
    }

    /// <summary>The tools that read and change what this tab shows, and those of its active viewlets.</summary>
    public IReadOnlyList<IClientTool> GetClientTools()
    {
        var tools = new List<IClientTool>
        {
            new GetFileListTool(_model),
            new FindFilesByNameTool(_model),
            new GetFileContentsTool(_model),
            new GetLineCountTool(_model),
            new GetFileStatsTool(_model),
            new CreateTextFileTool(_model),
            new CreateDirectoryTool(_model),
            new CopyTool(_model),
            new MoveTool(_model),
            new RenameTool(_model),
            new DeleteTool(_model),
        };

        // Tools contributed by the active viewlets (dotnet build/test, git status/diff…).
        foreach (var surface in _viewletSurfaces)
            tools.AddRange(surface.GetClientTools());

        return tools;
    }

    /// <summary>The folder these tools resolve relative names against (their confinement boundary).
    /// Distinguishes one file-system tab from another when both are pinned as AI context.</summary>
    public string? GetSecurityContext()
        => Location.IsThisPc                     ? "This PC"
         : string.IsNullOrEmpty(Location.CurrentPath) ? null
         : Location.CurrentPath;

    /// <summary>The folder this tab shows, with everything under it — where the bytes are, for a mounted one; on This PC, every
    /// drive it lists.</summary>
    public ContextGrant GetContextGrant()
        => Location.IsThisPc ? ContextGrant.OfFolders(DriveInfo.GetDrives().Select(d => d.RootDirectory.FullName))
         : string.IsNullOrEmpty(Location.CurrentPath) ? ContextGrant.None
         : ContextGrant.OfFolder(_model.Vfs.TryResolveReal(Location.CurrentPath));

    /// <summary>
    /// Risk of letting the AI act here: High when unconfined or system-critical (This PC, the system
    /// drive root, or inside the Windows folder), Medium for a whole non-system drive root, else Low.
    /// </summary>
    public ContextSecurityRisk GetContextSecurityRisk()
    {
        var vfs  = _model.Vfs;
        var path = Location.CurrentPath;
        if (Location.IsThisPc) return ContextSecurityRisk.High;             // unconfined — AI can go anywhere
        if (string.IsNullOrEmpty(path)) return ContextSecurityRisk.High;

        // A location reached only through its session is another machine: what is read there leaves the host it lives on. One
        // whose host is this PC (a WSL distro) is judged as the folder this disk reaches it as, below.
        if (vfs.GetBacking(path) == VirtualBacking.Remote
            && (vfs.GetCapabilities(path) & LocationCapabilities.SameMachine) == 0)
            return ContextSecurityRisk.High;

        // Classify where the bytes REALLY are. A mounted path is "::id\…", which Path.GetFullPath cannot
        // parse: the throw would land in the catch below and report Low — so a mount pointed at
        // C:\Windows would read as safe. Inside an archive nothing resolves to a directory, so judge the
        // archive file's own location instead. An unclassifiable path is High, not Low: not knowing where
        // we are is precisely the case not to hand the AI.
        string full;
        try
        {
            var real = vfs.TryResolveReal(path) ?? vfs.SplitOutermostContainer(path).RealContainer;
            full = Path.GetFullPath(real).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch { return ContextSecurityRisk.High; }

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrEmpty(windowsDir) &&
            (full.Equals(windowsDir, StringComparison.OrdinalIgnoreCase) ||
             full.StartsWith(windowsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            return ContextSecurityRisk.High;                          // the Windows system folder

        var driveRoot  = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrEmpty(driveRoot) && full.Equals(driveRoot, StringComparison.OrdinalIgnoreCase))
            return string.Equals(driveRoot, systemRoot, StringComparison.OrdinalIgnoreCase)
                ? ContextSecurityRisk.High                            // system drive root (e.g. C:\)
                : ContextSecurityRisk.Medium;                         // other drive root (e.g. D:\)

        return ContextSecurityRisk.Low;
    }

    public IContext? GetContextObject()
    {
        if (Location.IsThisPc)
            return new FileSystemContext
            {
                RootPath        = string.Empty,
                CurrentPath     = string.Empty,
                AvailableDrives = DriveInfo.GetDrives()
                                           .Select(d => d.RootDirectory.FullName)
                                           .ToList()
            };
        if (string.IsNullOrEmpty(Location.RootPath))
        {
            return new FileSystemContext
            {
                RootPath = Location.CurrentPath,
                CurrentPath = "",
                SelectedItems = _model.Selection.Select(e => e.FullPath).ToList()
            };
        }
        return new FileSystemContext
        {
            RootPath      = Location.RootPath,
            CurrentPath   = Location.CurrentPath,
            SelectedItems = _model.Selection.Select(e => e.FullPath).ToList()
        };
    }

    // ── Searching ─────────────────────────────────────────────────────────────

    /// <summary>Hits handed to the agent in one corpus query.</summary>
    private const int SearchHitCap = 200;

    private IFileCorpusSearch? _corpusSearch;

    // Resolved lazily from whichever feature owns a file index — never referenced directly, so the browser
    // stays independent of the search feature (same seam as the archive codecs).
    private IFileCorpusSearch? CorpusSearch =>
        _corpusSearch ??= (_model.Shell.DiscoverImplementations<IFileCorpusSearch>() ?? [])
            .Select(t => { try { return Activator.CreateInstance(t) as IFileCorpusSearch; } catch { return null; } })
            .FirstOrDefault(e => e is not null);

    /// <summary>
    /// This page searches files, so filename globs mean something here — plus whatever extra syntax the
    /// index backing it understands (property constraints like <c>kind:document</c>), which arrives with
    /// the backend rather than by referencing the feature that owns it.
    /// <para>
    /// Deliberately not cached: the corpus resolves lazily, and a snapshot taken before it existed would
    /// leave the browser silently unable to parse constraints it can in fact enforce.
    /// </para>
    /// </summary>
    public IReadOnlyList<ISearchTermRecognizer> TermRecognizers =>
        [new GlobTermRecognizer(), .. CorpusSearch?.TermRecognizers ?? []];

    public string SearchTargetDescription =>
        Location.IsThisPc                         ? "files across this PC"
        : string.IsNullOrEmpty(Location.RootPath) ? "files under the current folder"
                                                  : $"files under '{Location.RootPath}'";

    /// <summary>
    /// Globs, quoted terms and property filters are strong evidence of a search; a bare path is
    /// navigation and belongs to the <c>&gt;</c> handler, so it is damped rather than claimed.
    /// </summary>
    public float ScoreQuery(string input)
    {
        var score = SearchQueryScorer.Score(input);
        var trimmed = input.Trim();
        if (score > 0 && IsUnquotedAbsolutePath(trimmed)) score *= 0.25f;
        return score;
    }

    private static bool IsUnquotedAbsolutePath(string input)
    {
        if (input.Length < 2) return false;
        if (input[0] == '"' || input[0] == '\'') return false;
        if (input.Contains('*') || input.Contains('?')) return false;   // globs stay as search
        return Path.IsPathRooted(input);
    }

    // The browser can't settle a content regex itself — the verifier that reads candidate files lives with
    // the search feature. So the agent is told which rows are proven and which are only possible, rather
    // than being handed a set that silently mixes the two.
    private static string RegexScopeNote(int confirmed, int possible, string pattern) =>
        $"{confirmed} match(es) confirmed by file name; {possible} more came back from the index and " +
        "may match inside the file — open a Search tab to have those checked.";

    /// <summary>The query as the user wrote it, for a message about it.</summary>
    private static string Described(SearchRequest request) =>
        request.IsRegex ? $"/{request.Text}/" : $"'{request.Text}'";

    public async Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (request.IsRegex && !request.TryCompileRegex(out _, out var error))
            return SearchOutcome.Unsupported(Str.Format("WindowsFileSystem.Search.InvalidRegexFormat", error));

        var context = GetContextObject() as FileSystemContext;
        var scoped  = new FileSearchRequest(request, context?.RootPath ?? Location.RootPath, context?.AvailableDrives ?? []);
        if (!scoped.HasScope)
            return SearchOutcome.Unsupported(Str.Get("WindowsFileSystem.Search.NoScope"));

        if (!display)
        {
            // Agent-side read: query the index directly and leave the browser exactly as it was. This can't
            // go through HandleObject — that seam is fire-and-forget UI ("do the default thing with this"),
            // and the agent needs the hits back.
            if (CorpusSearch is not { } engine)
                return SearchOutcome.Unsupported("No file search index is available.");

            var hits = await engine.SearchAsync(request, scoped.Root, scoped.Drives, SearchHitCap, ct);

            // Nothing from the index isn't proof of absence — this folder may simply not be indexed. Say
            // so, rather than letting the agent report "no such files". The manual scan reads every file
            // in the tree, so it stays the user's call and is not something an agent starts.
            if (hits.Count == 0)
                return SearchOutcome.Narrowed(hits,
                    $"Nothing matched {Described(request)} here. The Windows index returned nothing, which " +
                    "may mean this location isn't indexed rather than that no file matches — opening a " +
                    "Search tab offers a manual folder scan.");

            if (!request.IsRegex)
                return SearchOutcome.Found(hits);

            // Some of these are proven (the name matched) and some are only index candidates. Reporting the
            // split keeps the agent from presenting "might match" rows as findings.
            var confirmed = hits.Count(h => h.State == SearchHitState.Verified);
            return SearchOutcome.Narrowed(hits, RegexScopeNote(confirmed, hits.Count - confirmed, request.Text));
        }

        // Hand the search off and let whichever feature owns file search present it — the browser doesn't
        // know, or need to know, that a Search tab is what appears.
        return _model.Shell.HandleObject(scoped)
            ? SearchOutcome.None()      // the results surface is the feedback; nothing to say in chat
            : SearchOutcome.Unsupported(Str.Get("WindowsFileSystem.Search.NoResultsSurface"));
    }
}
