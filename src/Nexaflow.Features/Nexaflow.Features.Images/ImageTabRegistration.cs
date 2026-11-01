using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Features.Images.Views;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Drafts;
using Nexaflow.Visuals.Common.Localization;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.Images;

/// <summary>
/// Registers the image viewer page with <see cref="FeatureManager"/>.
/// Accepts a "paths" page parameter containing pipe-separated image file paths, and an optional
/// "view" param selecting the initial sub-view (carousel / album / explore / collage / edit). A "draft" param marks one
/// picture made for the user and not saved yet, naming the folder its Save as starts in.
/// </summary>
public sealed class ImageTabRegistration : IPageRegistration
{
    private readonly IShellServices _shell;
    private readonly IAIService _ai;
    private readonly IPictureTools? _pictures;
    private readonly IPictureSelection? _selection;

    private readonly IPageReading? _reading;
    private readonly IPictureErasing? _erasing;

    /// <summary>The editing engine every Images tab of this runtime shares: codecs, renderer and the recipe store.</summary>
    private readonly ImageEditingServices _editing;

    /// <summary>The Images settings every tab shares.</summary>
    private readonly ImagesConfig _config;

    /// <param name="ai">The workspace's AI, which makes a video from the picture on screen where it has a model for it.</param>
    /// <param name="pictures">The picture tools, which restore a photo — AI enhance and upscale — where the app has them.</param>
    /// <param name="selection">Picture selection, which picks things out of a photo — the editor's cut-out — where the app has it.</param>
    /// <param name="reading">Page reading, which reads a picture of a page into Markdown, where the app has it.</param>
    /// <param name="editing">The runtime's editing engine, shared by its Images tabs: codecs, renderer and the recipe store.</param>
    /// <param name="config">The Images settings: what a layered picture's history may hold.</param>
    public ImageTabRegistration(IShellServices shell, IAIService ai, ImageEditingServices editing, IPictureTools? pictures = null,
                                IPictureSelection? selection = null, IPageReading? reading = null, IPictureErasing? erasing = null,
                                ImagesConfig? config = null)
    {
        _config    = config ?? new ImagesConfig();
        _editing   = editing;
        _shell     = shell;
        _ai        = ai;
        _pictures  = pictures;
        _selection = selection;
        _reading   = reading;
        _erasing   = erasing;
    }

    public static string StaticPageKind => "Images";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("paths", "Pipe-separated full paths of the pictures to show. Omit for New picture: a blank canvas, a picture drawn into one, the clipboard's picture, or pictures opened.", Required: false, OnDisk: true),
        new("view", "How to show them: carousel (the default), album, explore, collage or edit.", Required: false),
        new("scope", "\"folder\" when the pictures are a whole folder.", Required: false),
        new("tool", "The image editor's tool to open on, with view edit: crop, adjust, retouch, cutout, markup or resize.", Required: false),
    ];

    /// <summary>New picture opens with no picture at all, so it is offered wherever pages are launched.</summary>
    public bool CanBeContextItem => true;

    /// <summary>Maps a "view" page param to its sub-view (carousel is the default; "slideshow" is an alias).</summary>
    public static ImageViewMode ParseView(string? view) => view?.ToLowerInvariant() switch
    {
        "album"   => ImageViewMode.Album,
        "explore" => ImageViewMode.Explore,
        "collage" => ImageViewMode.Collage,
        "edit"    => ImageViewMode.Edit,
        _         => ImageViewMode.Carousel,
    };

    /// <summary>Maps a "tool" page param to the editor's tool it opens on — "cutout" for Cut out; none, the editor's own.</summary>
    public static EditTool? ParseTool(string? tool) => Enum.TryParse<EditTool>(tool, ignoreCase: true, out var parsed) ? parsed : null;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var paths = pageParams?.GetValueOrDefault("paths")?
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .ToList() ?? [];

        var mode = ParseView(pageParams?.GetValueOrDefault("view"));

        // "folder" = the whole-folder actions (slideshow/album); otherwise it's an explicit file selection.
        var wholeFolder = pageParams?.GetValueOrDefault("scope") == "folder";

        var draft = DraftOffered(pageParams, paths);
        var page = new Page
        {
            Title = TitleOf(paths),
            Icon  = "🖼",
        };
        page.ContentFactory = () =>
        {
            ImageViewModel? viewer = null;
            viewer = new ImageViewModel(paths, _shell, mode, _editing, _ai, _pictures, _selection, _reading, _erasing, _config)
            {
                MakeDraft = (path, offered) => Drafted(page, viewer!, path, offered),
            };
            if (draft is var (draftPath, offered)) viewer.Draft = Drafted(page, viewer, draftPath, offered);
            if (ParseTool(pageParams?.GetValueOrDefault("tool")) is { } tool) viewer.Editor.Tool = tool;
            viewer.PicturesReplaced += shown => Follow(page, shown);
            return new ImageView(viewer);
        };
        SetBreadcrumbs(page, paths, wholeFolder);
        return page;
    }

    /// <summary>A single picture by its name, a set by its count, none as New picture.</summary>
    private static string TitleOf(IReadOnlyList<string> paths) => paths.Count switch
    {
        0 => Str.Get("Images.Page.NewPicture"),
        1 => Path.GetFileName(paths[0]),
        _ => Str.Format("Images.Page.TitleFormat", paths.Count),
    };

    /// <summary>Single image → "folder › name"; a folder view → "folder › all image files"; a selection → "folder › N images".</summary>
    private static void SetBreadcrumbs(Page page, IReadOnlyList<string> paths, bool wholeFolder)
    {
        if (paths.Count > 1)
            page.SetMultiFileBreadcrumbs(paths, wholeFolder ? Str.Get("Images.Page.AllImageFiles") : Str.Format("Images.Page.SelectionFormat", paths.Count));
        else
            page.SetFileBreadcrumbs(paths.Count == 1 ? paths[0] : string.Empty, paths.Count == 1 ? Path.GetFileName(paths[0]) : Str.Get("Images.Page.Fallback"));
    }

    /// <summary>The page follows what its start screen opened: titled and found by it, and opened on it again.</summary>
    private static void Follow(Page page, ShownPictures shown)
    {
        page.Title = TitleOf(shown.Paths);
        SetBreadcrumbs(page, shown.Paths, wholeFolder: false);
        var pageParams = new Dictionary<string, string> { ["paths"] = string.Join('|', shown.Paths) };
        if (shown.Mode == ImageViewMode.Edit) pageParams["view"] = "edit";
        if (shown.IsDraft) pageParams[DraftFile.PageParameterName] = shown.DraftFolder ?? string.Empty;
        page.PageParams = pageParams;
    }

    /// <summary>The draft a page opens on — one picture made for the user in a temp folder, opened with a "draft" parameter
    /// naming the folder its Save as starts in — or null for pictures opened where they live.</summary>
    private static (string Path, string? Offered)? DraftOffered(Dictionary<string, string>? pageParams, IReadOnlyList<string> paths)
        => paths.Count == 1 && pageParams?.TryGetValue(DraftFile.PageParameterName, out var offered) == true
            ? (paths[0], offered is { Length: > 0 } ? offered : null)
            : null;

    /// <summary>
    /// The Save as of a draft shown in <paramref name="page"/>: kept by the viewer — a layered picture written as it stands, a
    /// picture copied with its edits — and that file from then on: it opens there, and the draft's tab goes.
    /// </summary>
    private DraftSaving Drafted(Page page, ImageViewModel viewer, string path, string? offered)
    {
        var draft = new DraftSaving(path, offered, (name, extensions, folder) => _shell.PickSaveFileAsync(name, extensions, folder), _shell.ShowError,
                                    viewer.KeepDraftAsync);
        draft.Kept += kept =>
        {
            _shell.OpenTab(StaticPageKind, new Dictionary<string, string> { ["paths"] = kept });
            _shell.CloseTab(page);
        };
        return draft;
    }
}
