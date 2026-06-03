using Nexaflow.Features.Common;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Scratchpad.Services;

/// <summary>
/// Turns a dropped URL into a rich post-it. If the URL is itself an image (e.g. an image dragged
/// out of a browser, which hands over the image URL rather than a bitmap), the image is downloaded
/// and embedded. Otherwise Open Graph / HTML metadata is fetched to build a preview card (title,
/// description, and a preview image). Downloaded images live in the note's attachment folder so they
/// follow its lifetime and work offline. The result is exposed via <see cref="PreviewMarkdown"/> for
/// the caller to apply on the UI thread once the task completes.
/// </summary>
public sealed class UrlPreviewTask(string url, string attachmentDir) : IBackgroundTask
{
    private static readonly HttpClient _http = CreateClient();

    /// <summary>The built preview markdown, or null if nothing useful was extracted.</summary>
    public string? PreviewMarkdown { get; private set; }

    public string Description => $"Fetching preview: {url}";

    public async Task RunAsync(CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();

        var mediaType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;

        // The URL points straight at an image → embed it instead of a preview card.
        if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            var saved = await SaveImageAsync(bytes, "image", ExtForImage(mediaType, url), ct);
            if (saved is not null) PreviewMarkdown = $"![]({saved})";
            return;
        }

        var html = await resp.Content.ReadAsStringAsync(ct);

        var (title, description, imageUrl) = ExtractMetadata(html);

        string? localImage = null;
        if (!string.IsNullOrWhiteSpace(imageUrl)
            && Uri.TryCreate(new Uri(url), imageUrl, out var abs)     // resolve relative og:image
            && (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
        {
            localImage = await TryDownloadImageAsync(abs, ct);
        }

        // Nothing worth showing — leave the bare URL the user already sees.
        if (title is null && description is null && localImage is null) return;

        var sb = new StringBuilder();
        if (localImage is not null) sb.Append("![](").Append(localImage).Append(")\n\n");
        if (!string.IsNullOrWhiteSpace(title)) sb.Append("**").Append(title!.Trim()).Append("**\n\n");
        if (!string.IsNullOrWhiteSpace(description)) sb.Append(description!.Trim()).Append("\n\n");
        var host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;
        sb.Append('[').Append(host).Append("](").Append(url).Append(')');

        PreviewMarkdown = sb.ToString();
    }

    private async Task<string?> TryDownloadImageAsync(Uri imageUri, CancellationToken ct)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(imageUri, ct);
            return await SaveImageAsync(bytes, "preview", ExtForImage(null, imageUri.AbsolutePath), ct);
        }
        catch { return null; }
    }

    /// <summary>Writes image <paramref name="bytes"/> into the attachment folder as
    /// <c>{baseName}{ext}</c>; returns the file name (relative to the editor's BaseDirectory) or null.</summary>
    private async Task<string?> SaveImageAsync(byte[] bytes, string baseName, string ext, CancellationToken ct)
    {
        try
        {
            var name = baseName + ext;
            Directory.CreateDirectory(attachmentDir);
            await File.WriteAllBytesAsync(Path.Combine(attachmentDir, name), bytes, ct);
            return name;
        }
        catch { return null; }
    }

    /// <summary>Picks an image extension from the content-type, falling back to the URL path's
    /// extension, then <c>.img</c> (the renderer decodes by content, so this is only cosmetic).</summary>
    private static string ExtForImage(string? mediaType, string urlOrPath)
    {
        var ext = mediaType?.ToLowerInvariant() switch
        {
            "image/png"                                  => ".png",
            "image/jpeg"                                 => ".jpg",
            "image/gif"                                  => ".gif",
            "image/webp"                                 => ".webp",
            "image/bmp"                                  => ".bmp",
            "image/svg+xml"                              => ".svg",
            "image/tiff"                                 => ".tiff",
            "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
            _                                            => null
        };
        if (ext is not null) return ext;

        var fromPath = Path.GetExtension(urlOrPath);
        return !string.IsNullOrWhiteSpace(fromPath) && fromPath.Length <= 5 ? fromPath : ".img";
    }

    // ── HTML extraction (tolerant, regex-based) ───────────────────────────

    /// <summary>Pulls the preview fields from a page's HTML: Open Graph title/description/image,
    /// falling back to the <c>&lt;title&gt;</c> tag and the <c>description</c> meta. Pure and tolerant
    /// of attribute order; exposed for testing.</summary>
    public static (string? Title, string? Description, string? ImageUrl) ExtractMetadata(string html) =>
    (
        Meta(html, "og:title")       ?? TitleTag(html),
        Meta(html, "og:description") ?? Meta(html, "description"),
        Meta(html, "og:image")
    );

    private static string? Meta(string html, string property)
    {
        // Matches <meta property="og:title" content="..."> or name="..."; attribute order varies.
        var m = Regex.Match(html,
            $"<meta[^>]+(?:property|name)\\s*=\\s*[\"']{Regex.Escape(property)}[\"'][^>]*?content\\s*=\\s*[\"']([^\"']*)[\"']",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success)
            m = Regex.Match(html,
                $"<meta[^>]+content\\s*=\\s*[\"']([^\"']*)[\"'][^>]*?(?:property|name)\\s*=\\s*[\"']{Regex.Escape(property)}[\"']",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
    }

    private static string? TitleTag(string html)
    {
        var m = Regex.Match(html, "<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value).Trim() : null;
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // Some sites gate metadata behind a browser-like UA.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Nexaflow/1.0");
        return c;
    }
}
