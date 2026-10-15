using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Features.Images.Views;
using Nexaflow.Visuals.Common.Localization;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.Images;

/// <summary>
/// Registers the image viewer page with <see cref="FeatureManager"/>.
/// Accepts a "paths" page parameter containing pipe-separated image file paths, and an optional
/// "view" param selecting the initial sub-view (carousel / album / explore / collage / edit).
/// </summary>
public sealed class ImageTabRegistration : IPageRegistration
{
    private readonly IShellServices _shell;
    private readonly IAIService _ai;
    private readonly IPictureTools? _pictures;
    private readonly IPictureSelection? _selection;

    /// <summary>The editing engine every Images tab of this workspace shares: codecs, renderer and the recipe store.</summary>
    private readonly ImageEditingServices _editing = new();

    /// <param name="ai">The workspace's AI, which makes a video from the picture on screen where it has a model for it.</param>
    /// <param name="pictures">The picture tools, which restore a photo — AI enhance and upscale — where the app has them.</param>
    /// <param name="selection">Picture selection, which picks things out of a photo — the editor's cut-out — where the app has it.</param>
    public ImageTabRegistration(IShellServices shell, IAIService ai, IPictureTools? pictures = null, IPictureSelection? selection = null)
    {
        _shell     = shell;
        _ai        = ai;
        _pictures  = pictures;
        _selection = selection;
    }

    public static string StaticPageKind => "Images";
    public string PageKind => StaticPageKind;

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

        var page = new Page
        {
            Title       = title,
            Icon        = "🖼",
            ContentFactory = () =>
            {
                var viewer = new ImageViewModel(paths, _shell, mode, _editing, _ai, _pictures, _selection);
                if (ParseTool(pageParams?.GetValueOrDefault("tool")) is { } tool) viewer.Editor.Tool = tool;
                return new ImageView(viewer);
            }
        };

        // Single image → "folder › name"; a folder view → "folder › all image files"; a selection → "folder › N images".
        if (paths.Count > 1)
            page.SetMultiFileBreadcrumbs(paths, wholeFolder ? Str.Get("Images.Page.AllImageFiles") : Str.Format("Images.Page.SelectionFormat", paths.Count));
        else
            page.SetFileBreadcrumbs(paths.Count == 1 ? paths[0] : string.Empty, paths.Count == 1 ? title : Str.Get("Images.Page.Fallback"));

        return page;
    }
}
