using System;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.Common;

/// <summary>
/// Builds the standard breadcrumb trail for a file/media <b>viewer</b> tab: one clickable parent-directory
/// crumb (which opens — or focuses — a file-explorer tab rooted at that folder) followed by a leaf crumb
/// naming what's on screen. Keeps every viewer consistent, e.g. <c>D:\temp › cat.jpg</c> for a single file
/// or <c>D:\temp › 6 images</c> for a multi-file view.
/// <para>
/// Reuse this from every viewer's <see cref="IPageRegistration.CreatePageDefinition"/> (and from any
/// <c>Reinitialize</c> path that opens a different file) instead of hand-rolling breadcrumb segments.
/// </para>
/// </summary>
public static class FileBreadcrumbs
{
    /// <summary>
    /// Page kind of the file-explorer tab the parent crumb targets. Loose string coupling — the
    /// WindowsFileSystem feature owns the real <see cref="IPageRegistration"/>; the params below mirror
    /// the <c>{mode:"path", path, label}</c> set it accepts.
    /// </summary>
    public const string FileSystemPageKind = "FileSystem";

    /// <summary>A clickable crumb that opens (or focuses) a file-explorer tab rooted at <paramref name="directory"/>.
    /// It stands for the folder's whole path, which the shell draws as a person names it and shortens from the middle;
    /// the opened tab names the folder itself, as its runtime's file system names it.</summary>
    public static BreadcrumbSegment ForDirectory(string directory) =>
        new()
        {
            Label            = directory,
            Path             = directory,
            Content          = new PlaceContent(directory, PlaceNaming.WholePath),
            TargetPageKind   = FileSystemPageKind,
            TargetPageParams = new Dictionary<string, string>
            {
                ["mode"] = "path",
                ["path"] = directory,
            },
        };

    /// <summary>
    /// Replaces <paramref name="page"/>'s breadcrumbs with "[parent dir] › [leaf]" for a single file. The
    /// parent crumb is clickable and is omitted when <paramref name="filePath"/> has no directory.
    /// <paramref name="leaf"/> defaults to the file's own name, which the shell draws as a person names it; pass the
    /// page title so the empty-path case still shows a sensible label — a leaf given is the page's own.
    /// </summary>
    public static void SetFileBreadcrumbs(this Page page, string filePath, string? leaf = null)
    {
        var naming = leaf is null ? PlaceNaming.OwnName : PlaceNaming.PageLabel;
        leaf ??= string.IsNullOrEmpty(filePath) ? string.Empty : Path.GetFileName(filePath);

        // Derived content (a DICOM report, an archived doc extracted to a temp file) points back to the
        // page it came from rather than its throwaway directory.
        if (OriginBreadcrumbs.ParentCrumbFor(filePath) is { } origin)
        {
            page.Breadcrumbs.Clear();
            page.Breadcrumbs.Add(origin);
            page.Breadcrumbs.Add(new BreadcrumbSegment { Label = leaf, Path = filePath, Content = new PlaceContent(filePath, naming) });
            return;
        }

        var dir = string.IsNullOrEmpty(filePath) ? null : Path.GetDirectoryName(filePath);
        SetBreadcrumbs(page, dir, leaf, string.IsNullOrEmpty(filePath) ? null : new PlaceContent(filePath, naming));
    }

    /// <summary>
    /// Replaces <paramref name="page"/>'s breadcrumbs with "[origin page] › [leaf]" — a parent crumb that
    /// re-opens/focuses <paramref name="originKind"/> (opened with <paramref name="originParams"/>) rather
    /// than a file-system folder. The general "this page was opened <em>from</em> another page" trail; use
    /// it directly, or register the file with <see cref="OriginBreadcrumbs"/> and let
    /// <see cref="SetFileBreadcrumbs"/> apply it for you.
    /// </summary>
    public static void SetOriginBreadcrumb(this Page page, string leaf, string originKind,
                                           IReadOnlyDictionary<string, string> originParams, string originLabel)
    {
        page.Breadcrumbs.Clear();
        page.Breadcrumbs.Add(new BreadcrumbSegment
        {
            Label = originLabel,
            TargetPageKind = originKind,
            TargetPageParams = new Dictionary<string, string>(originParams),
        });
        page.Breadcrumbs.Add(new BreadcrumbSegment { Label = leaf });
    }

    /// <summary>
    /// Replaces <paramref name="page"/>'s breadcrumbs with "[common dir] › [summary]" for a multi-file view
    /// (e.g. an image selection or an audio queue). The parent crumb appears only when every path shares one
    /// directory; <paramref name="summary"/> is the leaf label (e.g. <c>"6 images"</c>).
    /// </summary>
    public static void SetMultiFileBreadcrumbs(this Page page, IReadOnlyList<string> paths, string summary)
        => SetBreadcrumbs(page, CommonDirectory(paths), summary);

    /// <summary>The directory shared by every path, or null when they differ, are empty, or have no folder.</summary>
    public static string? CommonDirectory(IReadOnlyList<string> paths)
    {
        if (paths is null || paths.Count == 0) return null;
        string? dir = null;
        foreach (var p in paths)
        {
            var d = Path.GetDirectoryName(p);
            if (string.IsNullOrEmpty(d)) return null;
            if (dir is null) dir = d;
            else if (!string.Equals(dir, d, StringComparison.OrdinalIgnoreCase)) return null;
        }
        return dir;
    }

    private static void SetBreadcrumbs(Page page, string? directory, string leafLabel, PlaceContent? leaf = null)
    {
        page.Breadcrumbs.Clear();
        if (!string.IsNullOrEmpty(directory))
            page.Breadcrumbs.Add(ForDirectory(directory));
        page.Breadcrumbs.Add(new BreadcrumbSegment { Label = leafLabel, Path = leaf?.Path, Content = leaf });
    }
}
