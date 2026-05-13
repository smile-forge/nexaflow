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
public sealed class ImageTabRegistration : ITabRegistration
{
    public string PageKind => "Images";

    public TabEntry CreateTab(Dictionary<string, string>? pageParams = null)
    {
        var paths = pageParams?.GetValueOrDefault("paths")?
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .ToList() ?? [];

        var title = paths.Count == 1
            ? Path.GetFileName(paths[0])
            : $"Images ({paths.Count})";

        return new TabEntry
        {
            Title       = title,
            Icon        = "🖼",
            Breadcrumbs = [new BreadcrumbSegment { Label = title }],
            PageFactory = () => new ImageView(new ImageViewModel(paths))
        };
    }
}
