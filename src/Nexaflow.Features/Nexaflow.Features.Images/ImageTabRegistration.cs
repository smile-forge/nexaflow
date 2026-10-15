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

    /// <summary>The editing engine every Images tab of this workspace shares: codecs, renderer and the recipe store.</summary>
    private readonly ImageEditingServices _editing = new();

    /// <param name="ai">The workspace's AI, which makes a video from the picture on screen where it has a model for it.</param>
    public ImageTabRegistration(IShellServices shell, IAIService ai)
    {
        _shell = shell;
        _ai    = ai;
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
            ContentFactory = () => new ImageView(new ImageViewModel(paths, _shell, mode, _editing, _ai))
        };

        // Single image → "folder › name"; a folder view → "folder › all image files"; a selection → "folder › N images".
        if (paths.Count > 1)
            page.SetMultiFileBreadcrumbs(paths, wholeFolder ? Str.Get("Images.Page.AllImageFiles") : Str.Format("Images.Page.SelectionFormat", paths.Count));
        else
            page.SetFileBreadcrumbs(paths.Count == 1 ? paths[0] : string.Empty, paths.Count == 1 ? title : Str.Get("Images.Page.Fallback"));

        return page;
    }
}
