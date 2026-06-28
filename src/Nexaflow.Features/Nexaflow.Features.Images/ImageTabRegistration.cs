using Nexaflow.Features.Common;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Features.Images.Views;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.Images;

/// <summary>
/// Registers the image viewer page with <see cref="FeatureManager"/>.
/// Accepts a "paths" page parameter containing pipe-separated image file paths, and an optional
/// "view" param selecting the initial sub-view (carousel / album / explore / collage).
/// </summary>
public sealed class ImageTabRegistration : IPageRegistration
{
    private readonly IShellServices _shell;

    public ImageTabRegistration(IShellServices shell) => _shell = shell;

    public static string StaticPageKind => "Images";
    public string PageKind => StaticPageKind;

    /// <summary>Maps a "view" page param to its sub-view (carousel is the default; "slideshow" is an alias).</summary>
    public static ImageViewMode ParseView(string? view) => view?.ToLowerInvariant() switch
    {
        "album"   => ImageViewMode.Album,
        "explore" => ImageViewMode.Explore,
        "collage" => ImageViewMode.Collage,
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
            : $"Images ({paths.Count})";

        var page = new Page
        {
            Title       = title,
            Icon        = "🖼",
            ContentFactory = () => new ImageView(new ImageViewModel(paths, _shell, mode))
        };

        // Single image → "folder › name"; a folder view → "folder › all image files"; a selection → "folder › N images".
        if (paths.Count > 1)
            page.SetMultiFileBreadcrumbs(paths, wholeFolder ? "all image files" : $"{paths.Count} images");
        else
            page.SetFileBreadcrumbs(paths.Count == 1 ? paths[0] : string.Empty, paths.Count == 1 ? title : "Images");

        return page;
    }
}
