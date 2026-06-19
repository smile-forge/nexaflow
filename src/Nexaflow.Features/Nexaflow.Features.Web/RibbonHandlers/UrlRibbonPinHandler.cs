using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Ribbon;

namespace Nexaflow.Features.Web.RibbonHandlers;

/// <summary>
/// Pins a URL dragged from a browser onto the ribbon as a button that opens it in the HTML viewer.
/// Matches the native drag formats browsers expose for a link drag — Chromium/Edge advertise
/// <c>UniformResourceLocatorW</c>/<c>UniformResourceLocator</c>, Firefox <c>text/x-moz-url</c> — so the
/// ribbon only lights up for an actual link, not arbitrary dragged text. The button it produces carries
/// the internal <c>Html</c> page kind, so clicking it just opens an HTML tab (no executor needed).
/// </summary>
public sealed class UrlRibbonPinHandler : IRibbonPinHandler
{
    // CFSTR_INETURLW / CFSTR_INETURLA (Chromium, Edge, IE) + Firefox's moz-url.
    private const string InetUrlW = "UniformResourceLocatorW";
    private const string InetUrlA = "UniformResourceLocator";
    private const string MozUrl   = "text/x-moz-url";

    public IReadOnlyList<string> AcceptedFormats { get; } = [InetUrlW, InetUrlA, MozUrl];

    public RibbonPinResult? Pin(object payload, int insertIndex = -1)
    {
        var url = ExtractUrl(payload);
        if (url is null) return null;

        var label = Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host is { Length: > 0 } host
            ? host
            : url;

        return new RibbonPinResult
        {
            PageKind   = HtmlTabRegistration.StaticPageKind,
            Label      = label,
            Icon       = "🌐",
            PageParams = new() { ["path"] = url }
        };
    }

    /// <summary>
    /// Decodes the dropped payload to an http(s) URL. Browsers hand these formats over as a
    /// <see cref="MemoryStream"/> of (usually UTF-16) bytes; the moz-url payload is "url\ntitle".
    /// </summary>
    private static string? ExtractUrl(object payload)
    {
        var raw = payload switch
        {
            string s        => s,
            MemoryStream ms => Decode(ms),
            _               => null
        };
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // moz-url (and a stray terminator) — take the first line only.
        var url = raw.Split('\n', '\r')[0].Trim();
        return Uri.TryCreate(url, UriKind.Absolute, out var u)
               && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
            ? url
            : null;
    }

    private static string Decode(MemoryStream ms)
    {
        var bytes = ms.ToArray();
        // INETURLW and moz-url are UTF-16; the ANSI variant is ASCII-compatible, so UTF-16 detection
        // by a NUL in an even position covers the common case. Fall back to UTF-8 otherwise.
        var text = bytes.Length >= 2 && bytes[1] == 0
            ? Encoding.Unicode.GetString(bytes)
            : Encoding.UTF8.GetString(bytes);
        var nul = text.IndexOf('\0');
        return nul >= 0 ? text[..nul] : text;
    }
}
