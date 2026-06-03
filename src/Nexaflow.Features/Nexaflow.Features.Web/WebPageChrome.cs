using Nexaflow.Features.Common;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.Web;

/// <summary>
/// Derives a Web tab's title and breadcrumb segments from the current URL (and, when known, the
/// page title). The breadcrumb is built like a file path — host crumb plus one crumb per path
/// segment — and handed to the shared breadcrumb bar, which applies the same truncation/collapse
/// rules used by the file explorer.
/// </summary>
public static class WebPageChrome
{
    /// <summary>
    /// The tab title: the page title (≤14 chars + …) when known, otherwise
    /// <c>web:/.../{last path segment, ≤10 chars}</c> (e.g. <c>web:/.../source.py</c>).
    /// Local files (.html/.url) fall back to the file name.
    /// </summary>
    public static string Title(string url, string? pageTitle)
    {
        if (!string.IsNullOrWhiteSpace(pageTitle))
            return Ellipsize(pageTitle!.Trim(), 14);

        if (TryHttp(url, out var uri))
            return "web:/.../" + Clip(LastSegment(uri) ?? uri.Host, 10);

        return FileName(url);
    }

    /// <summary>
    /// Breadcrumb segments: the host (clicking it navigates to <c>scheme://host/</c>) followed by
    /// one crumb per path segment (clicking navigates to that point). Query and fragment are
    /// stripped. Local files yield a single file-name crumb.
    /// </summary>
    public static IReadOnlyList<BreadcrumbSegment> Crumbs(string url, Action<string> navigate)
    {
        if (!TryHttp(url, out var uri))
            return [new BreadcrumbSegment { Label = FileName(url) }];

        var crumbs = new List<BreadcrumbSegment>();
        var root   = $"{uri.Scheme}://{uri.Host}";
        crumbs.Add(new BreadcrumbSegment { Label = uri.Host, Navigate = () => navigate(root + "/") });

        var built = root;
        foreach (var part in uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            built += "/" + part;
            var target = built;   // capture per-iteration
            crumbs.Add(new BreadcrumbSegment
            {
                Label    = Uri.UnescapeDataString(part),
                Navigate = () => navigate(target)
            });
        }
        return crumbs;
    }

    private static string? LastSegment(Uri uri)
    {
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? Uri.UnescapeDataString(parts[^1]) : null;
    }

    private static bool TryHttp(string url, out Uri uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var u)
            && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
        {
            uri = u;
            return true;
        }
        uri = null!;
        return false;
    }

    private static string FileName(string url)
    {
        try
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var u) && u.IsFile
                ? Path.GetFileName(u.LocalPath)
                : Path.GetFileName(url);
        }
        catch { return url; }
    }

    private static string Ellipsize(string s, int max) => s.Length <= max ? s : s[..max] + "…";
    private static string Clip(string s, int max)      => s.Length <= max ? s : s[..max];
}
