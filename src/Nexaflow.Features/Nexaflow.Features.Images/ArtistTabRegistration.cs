using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Features.Images.Views;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Drafts;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.Images;

/// <summary>
/// Registers Artist, the image editor, with <see cref="FeatureManager"/>: one picture edited — a "path" page parameter — or, with
/// none, its start screen. A "draft" parameter marks a picture made for the user and not saved yet, naming the folder its Save
/// as starts in; a "tool" one the editor's tool it opens on.
/// </summary>
/// <remarks>Shares the runtime's editing engine, a feature service, with the viewer's registration, as both pages draw and edit
/// the same pictures.</remarks>
public sealed class ArtistTabRegistration : IPageRegistration
{
    private readonly IShellServices _shell;
    private readonly IAIService _ai;
    private readonly IPictureTools? _pictures;
    private readonly IPictureSelection? _selection;
    private readonly IPictureErasing? _erasing;
    private readonly ImageEditingServices _editing;
    private readonly ImagesConfig _config;

    /// <param name="ai">The workspace's AI, which draws a picture into a canvas where it has a model for it.</param>
    /// <param name="editing">The runtime's editing engine — codecs, renderer and the recipe store — shared with its viewer tabs.</param>
    /// <param name="pictures">The picture tools, which restore a photo — AI enhance and upscale — where the app has them.</param>
    /// <param name="selection">Picture selection, which picks things out of a picture — Cut out, Erase — where the app has it.</param>
    /// <param name="erasing">Picture erasing, which fills what Erase takes out, where the app has it.</param>
    /// <param name="config">The Images settings: what a layered picture's history may hold, the canvases opened lately.</param>
    public ArtistTabRegistration(IShellServices shell, IAIService ai, ImageEditingServices editing, IPictureTools? pictures = null,
                                 IPictureSelection? selection = null, IPictureErasing? erasing = null, ImagesConfig? config = null)
    {
        _shell     = shell;
        _ai        = ai;
        _editing   = editing;
        _config    = config ?? new ImagesConfig();
        _pictures  = pictures;
        _selection = selection;
        _erasing   = erasing;
    }

    public static string StaticPageKind => "Artist";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Full path of the picture to edit — a photo, or an .ora layered picture. Omit for the start screen: a blank canvas, a picture drawn into one, the clipboard's picture, or a picture opened.", Required: false, OnDisk: true),
        new("tool", "The editor's tool to open on: reshape, adjust, retouch, cutout, markup, resize, paint or move.", Required: false),
    ];

    /// <summary>The start screen opens with no picture at all, so Artist is offered wherever pages are launched.</summary>
    public bool CanBeContextItem => true;

    /// <summary>Maps a "tool" page param to the editor's tool it opens on — "select" for Select, "reshape" (or "crop") for Reshape;
    /// none, the editor's own.</summary>
    public static EditTool? ParseTool(string? tool) =>
        string.Equals(tool, "reshape", StringComparison.OrdinalIgnoreCase) ? EditTool.Crop
        : Enum.TryParse<EditTool>(tool, ignoreCase: true, out var parsed) ? parsed : null;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path = pageParams?.GetValueOrDefault("path") is { Length: > 0 } given ? given : null;
        var draft = path is not null && pageParams?.TryGetValue(DraftFile.PageParameterName, out var offered) == true
            ? (Path: path, Offered: offered is { Length: > 0 } ? offered : null)
            : ((string Path, string? Offered)?)null;
        var page = new Page { Icon = "🎨" };
        NameAfter(page, path, isDraft: draft is not null);
        page.ContentFactory = () =>
        {
            ArtistViewModel? artist = null;
            artist = new ArtistViewModel(path, _shell, _editing, _ai, _pictures, _selection, _erasing, _config)
            {
                MakeDraft = (picture, folder) => Drafted(page, artist!, picture, folder),
            };
            if (draft is var (draftPath, draftFolder)) artist.Draft = Drafted(page, artist, draftPath, draftFolder);
            if (ParseTool(pageParams?.GetValueOrDefault("tool")) is { } tool) artist.Editor.Tool = tool;
            artist.PictureReplaced += shown => Follow(page, shown);
            return new ArtistView(artist);
        };
        return page;
    }

    /// <summary>
    /// Titles the page and its breadcrumbs for what it edits: a picture by its name and where it lives; a draft — made for the user,
    /// kept in a temporary folder until saved — as a new picture, with no folder to point at; nothing yet, Artist.
    /// </summary>
    private static void NameAfter(Page page, string? path, bool isDraft)
    {
        page.Title = isDraft ? Str.Get("Images.Start.Title") : path is null ? Str.Get("Images.Artist.Title") : Path.GetFileName(path);
        page.SetFileBreadcrumbs(isDraft ? string.Empty : path ?? string.Empty, page.Title);
    }

    /// <summary>The page follows what its start screen opened: titled and found by it, and opened on it again.</summary>
    private static void Follow(Page page, ArtistPicture shown)
    {
        NameAfter(page, shown.Path, shown.IsDraft);
        var pageParams = new Dictionary<string, string> { ["path"] = shown.Path };
        if (shown.IsDraft) pageParams[DraftFile.PageParameterName] = shown.DraftFolder ?? string.Empty;
        page.PageParams = pageParams;
    }

    /// <summary>
    /// The Save as of a draft edited in <paramref name="page"/>: kept by the page — a layered picture written as it stands, a picture
    /// copied with its edits — and that file from then on: it opens there, and the draft's tab goes.
    /// </summary>
    private DraftSaving Drafted(Page page, ArtistViewModel artist, string path, string? offered)
    {
        var draft = new DraftSaving(path, offered, (name, extensions, folder) => _shell.PickSaveFileAsync(name, extensions, folder), _shell.ShowError,
                                    artist.KeepDraftAsync);
        draft.Kept += kept =>
        {
            _shell.OpenTab(StaticPageKind, new Dictionary<string, string> { ["path"] = kept });
            _shell.CloseTab(page);
        };
        return draft;
    }
}
