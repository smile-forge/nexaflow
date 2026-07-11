using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Nexaflow.Features.Email.Model;

namespace Nexaflow.Features.Email.Services;

/// <summary>
/// Materialises an email's inline parts to a temp folder and rewrites <c>cid:</c> references to the sibling
/// filenames, for two consumers: the in-tab markdown view (relative image paths resolved against
/// <see cref="InlineBaseDirectory"/>) and the full-fidelity "open in browser" escape hatch
/// (<see cref="ExportBrowserHtml"/> → a <c>message.html</c> the shell opens in the WebView2 tab). The temp
/// folder is created lazily (nothing on disk until the body actually references an inline image or the user
/// opens the browser view) and cleaned up at process exit.
/// </summary>
internal sealed partial class HtmlBodyExporter : IDisposable
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "nexaflow-email");
    private static int _cleanupHooked;

    private readonly EmailDocument _doc;
    private string? _dir;
    private Dictionary<string, string>? _cidToFile;
    private bool _browserExported;

    public HtmlBodyExporter(EmailDocument doc) => _doc = doc;

    public bool HasHtmlBody => !string.IsNullOrEmpty(_doc.HtmlBody);

    /// <summary>The folder inline images are written to, so the markdown view can resolve relative image
    /// names against it. Null when the message has no inline parts.</summary>
    public string? InlineBaseDirectory
    {
        get
        {
            EnsureDir();
            return _cidToFile is { Count: > 0 } ? _dir : null;
        }
    }

    /// <summary>Rewrites every <c>cid:ID</c> in <paramref name="html"/> to the (URL-escaped) local filename
    /// of the matching inline part. The markdown renderer un-escapes when resolving, and a browser resolves
    /// the escaped relative path directly — so one form serves both.</summary>
    public string RewriteCidToLocal(string html)
    {
        EnsureDir();
        if (_cidToFile is null || _cidToFile.Count == 0) return html;
        return CidPattern().Replace(html, m =>
        {
            var id = m.Groups[1].Value.Trim();
            return _cidToFile.TryGetValue(id, out var file) ? Uri.EscapeDataString(file) : m.Value;
        });
    }

    /// <summary>Writes <c>message.html</c> (HTML body, or the plain-text body wrapped in <c>&lt;pre&gt;</c>)
    /// plus the inline images into the temp folder and returns the html path for the shell to open.</summary>
    public string ExportBrowserHtml()
    {
        EnsureDir();
        string html;
        if (!string.IsNullOrEmpty(_doc.HtmlBody))
            html = RewriteCidToLocal(_doc.HtmlBody);
        else
            html = $"<!doctype html><meta charset=\"utf-8\"><pre style=\"white-space:pre-wrap;" +
                   $"font-family:system-ui,sans-serif\">{WebUtility.HtmlEncode(_doc.TextBody ?? string.Empty)}</pre>";

        var path = Path.Combine(_dir!, "message.html");
        File.WriteAllText(path, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _browserExported = true;
        return path;
    }

    private void EnsureDir()
    {
        if (_dir is not null) return;
        _dir = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        HookCleanup();

        _cidToFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in _doc.Attachments.Where(a => a.IsInline && a.ContentId is not null))
        {
            try
            {
                File.WriteAllBytes(Path.Combine(_dir, part.EntryName), part.Content);
                _cidToFile[part.ContentId!] = part.EntryName;
            }
            catch { /* a malformed inline part shouldn't sink the whole body */ }
        }
    }

    public void Dispose()
    {
        // Leave the folder in place if the browser view is showing it (deleting would break a reload); the
        // process-exit sweep collects it either way.
        if (_browserExported || _dir is null) return;
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort; process-exit sweeps it */ }
    }

    private static void HookCleanup()
    {
        if (System.Threading.Interlocked.Exchange(ref _cleanupHooked, 1) != 0) return;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); } catch { }
        };
    }

    [GeneratedRegex(@"cid:([^""'>\s\)]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CidPattern();
}
