using Nexaflow.Features.Common;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Features.Images.Views;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.Images;

/// <summary>
/// Registers the image viewer page with <see cref="FeatureManager"/>.
/// Accepts a "paths" page parameter containing pipe-separated image file paths.
/// </summary>
public sealed class ImageTabRegistration : IPageRegistration
{
    public static string StaticPageKind => "Images";
    public string PageKind => StaticPageKind;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var paths = pageParams?.GetValueOrDefault("paths")?
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .ToList() ?? [];

        var title = paths.Count == 1
            ? Path.GetFileName(paths[0])
            : $"Images ({paths.Count})";

        return new Page
        {
            Title       = title,
            Icon        = "🖼",
            Breadcrumbs = {new BreadcrumbSegment { Label = title }},
            ContentFactory = () => new ImageView(new ImageViewModel(paths))
        };
    }
}
