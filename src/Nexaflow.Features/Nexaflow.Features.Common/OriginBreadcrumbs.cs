using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Nexaflow.Features.Common;

/// <summary>
/// Process-wide map of "this on-disk file was extracted from another page, so its viewer's breadcrumb
/// should point back to that page — not to where the bytes happen to live."
/// <para>
/// A feature that materialises derived content to a throwaway temp file (a DICOM encapsulated report, an
/// archived document) registers the temp path here <b>before</b> opening it. Every file viewer that builds
/// its trail through <see cref="FileBreadcrumbs.SetFileBreadcrumbs"/> then gets the origin crumb as its
/// parent automatically — so the temp directory is never shown as a clickable folder, and clicking the
/// parent re-opens the originating page. Register unique (e.g. GUID) temp names so entries never collide;
/// <see cref="Clear"/> them when the originating page closes.
/// </para>
/// </summary>
public static class OriginBreadcrumbs
{
    private sealed record Origin(string Kind, Dictionary<string, string> Params, string Label);

    private static readonly ConcurrentDictionary<string, Origin> Map = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>Marks <paramref name="filePath"/> as originating from the <paramref name="originKind"/> page
    /// (opened with <paramref name="originParams"/>), labelled <paramref name="originLabel"/> in the crumb.</summary>
    public static void Register(string filePath, string originKind,
                                IReadOnlyDictionary<string, string> originParams, string originLabel)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        Map[filePath] = new Origin(originKind, new Dictionary<string, string>(originParams), originLabel);
    }

    /// <summary>Forgets a single registered path.</summary>
    public static void Clear(string filePath)
    {
        if (!string.IsNullOrEmpty(filePath)) Map.TryRemove(filePath, out _);
    }

    /// <summary>The origin parent crumb for <paramref name="filePath"/>, or null if none is registered.</summary>
    public static BreadcrumbSegment? ParentCrumbFor(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !Map.TryGetValue(filePath, out var o)) return null;
        return new BreadcrumbSegment
        {
            Label = o.Label,
            TargetPageKind = o.Kind,
            TargetPageParams = new Dictionary<string, string>(o.Params),
        };
    }
}
