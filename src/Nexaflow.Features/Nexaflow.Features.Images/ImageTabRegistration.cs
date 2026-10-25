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

    /// <summary>The editing engine every Images tab of this workspace shares: codecs, renderer and the recipe store.</summary>
    private readonly ImageEditingServices _editing;

    /// <param name="ai">The workspace's AI, which makes a video from the picture on screen where it has a model for it.</param>
    /// <param name="pictures">The picture tools, which restore a photo — AI enhance and upscale — where the app has them.</param>
    /// <param name="selection">Picture selection, which picks things out of a photo — the editor's cut-out — where the app has it.</param>
    /// <param name="reading">Page reading, which reads a picture of a page into Markdown, where the app has it.</param>
    /// <param name="vfs">The runtime's file system the pictures are read and saved through.</param>
    public ImageTabRegistration(IShellServices shell, IAIService ai, IVirtualFileSystem vfs, IPictureTools? pictures = null,
                                IPictureSelection? selection = null, IPageReading? reading = null, IPictureErasing? erasing = null)
    {
        _editing   = new ImageEditingServices(vfs);
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
        new("paths", "Pipe-separated full paths of the pictures to show.", OnDisk: true),
        new("view", "How to show them: carousel (the default), album, explore, collage or edit.", Required: false),
        new("scope", "\"folder\" when the pictures are a whole folder.", Required: false),
        new("tool", "The image editor's tool to open on, with view edit: crop, adjust, retouch, cutout, markup or resize.", Required: false),
    ];

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

        var title = paths.Count == 1
            ? Path.GetFileName(paths[0])
            : Str.Format("Images.Page.TitleFormat", paths.Count);

        var draft = Draft(pageParams, paths);
        var page = new Page
        {
            Title       = title,
            Icon        = "🖼",
            ContentFactory = () =>
            {
                var viewer = new ImageViewModel(paths, _shell, mode, _editing, _ai, _pictures, _selection, _reading, _erasing) { Draft = draft };
                if (ParseTool(pageParams?.GetValueOrDefault("tool")) is { } tool) viewer.Editor.Tool = tool;
                return new ImageView(viewer);
            }
        };

        // Single image → "folder › name"; a folder view → "folder › all image files"; a selection → "folder › N images".
        if (paths.Count > 1)
            page.SetMultiFileBreadcrumbs(paths, wholeFolder ? Str.Get("Images.Page.AllImageFiles") : Str.Format("Images.Page.SelectionFormat", paths.Count));
        else
            page.SetFileBreadcrumbs(paths.Count == 1 ? paths[0] : string.Empty, paths.Count == 1 ? title : Str.Get("Images.Page.Fallback"));

        // A draft kept somewhere is that file from then on: it opens there, and the draft's tab goes.
        if (draft is not null)
            draft.Kept += kept =>
            {
                _shell.OpenTab(StaticPageKind, new Dictionary<string, string> { ["paths"] = kept });
                _shell.CloseTab(page);
            };

        return page;
    }

    /// <summary>The Save as of a draft — one picture made for the user in a temp folder, opened with a "draft" parameter
    /// naming the folder its Save as starts in — or null for pictures opened where they live.</summary>
    private DraftSaving? Draft(Dictionary<string, string>? pageParams, IReadOnlyList<string> paths)
        => paths.Count == 1 && pageParams?.TryGetValue(DraftFile.PageParameterName, out var offered) == true
            ? new DraftSaving(paths[0], offered is { Length: > 0 } ? offered : null,
                              (name, extensions, folder) => _shell.PickSaveFileAsync(name, extensions, folder), _shell.ShowError,
                              (from, to) => ImageDraftHandler.KeepAsync(_editing, from, to))
            : null;
}
