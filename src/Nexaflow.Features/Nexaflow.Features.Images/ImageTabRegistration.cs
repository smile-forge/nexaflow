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
/// "view" param selecting the initial sub-view (carousel / album / explore / collage). Editing a picture is Artist's
/// (<see cref="ArtistTabRegistration"/>), which the viewer's Edit opens.
/// </summary>
public sealed class ImageTabRegistration : IPageRegistration
{
    private readonly IShellServices _shell;
    private readonly IAIService _ai;
    private readonly IPictureTools? _pictures;
    private readonly IPictureSelection? _selection;

    private readonly IPageReading? _reading;
    private readonly IPictureErasing? _erasing;

    /// <summary>The editing engine this workspace's Images and Artist tabs share: codecs, renderer and the recipe store.</summary>
    private readonly ImageEditingServices _editing;

    /// <param name="ai">The workspace's AI, which makes a video from the picture on screen where it has a model for it.</param>
    /// <param name="pictures">The picture tools, which restore a photo whose edits ask for it, where the app has them.</param>
    /// <param name="selection">Picture selection, which removes the background a photo's edits ask to, where the app has it.</param>
    /// <param name="reading">Page reading, which reads a picture of a page into Markdown, where the app has it.</param>
    /// <param name="erasing">Picture erasing, which fills what a photo's edits erase, where the app has it.</param>
    /// <param name="editing">The runtime's editing engine, shared by its Images and Artist tabs: codecs, renderer and the recipe store.</param>
    public ImageTabRegistration(IShellServices shell, IAIService ai, ImageEditingServices editing, IPictureTools? pictures = null,
                                IPictureSelection? selection = null, IPageReading? reading = null, IPictureErasing? erasing = null)
    {
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
        new("paths", "Pipe-separated full paths of the pictures to show.", OnDisk: true),
        new("view", "How to show them: carousel (the default), album, explore or collage.", Required: false),
        new("scope", "\"folder\" when the pictures are a whole folder.", Required: false),
    ];

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

        var page = new Page
        {
            Title = paths.Count == 1 ? Path.GetFileName(paths[0]) : Str.Format("Images.Page.TitleFormat", paths.Count),
            Icon  = "🖼",
            ContentFactory = () => new ImageView(new ImageViewModel(paths, _shell, mode, _editing, _ai, _pictures, _selection, _reading, _erasing)),
        };

        // Single image → "folder › name"; a folder view → "folder › all image files"; a selection → "folder › N images".
        if (paths.Count > 1)
            page.SetMultiFileBreadcrumbs(paths, wholeFolder ? Str.Get("Images.Page.AllImageFiles") : Str.Format("Images.Page.SelectionFormat", paths.Count));
        else
            page.SetFileBreadcrumbs(paths.Count == 1 ? paths[0] : string.Empty, paths.Count == 1 ? Path.GetFileName(paths[0]) : Str.Get("Images.Page.Fallback"));
        return page;
    }
}
