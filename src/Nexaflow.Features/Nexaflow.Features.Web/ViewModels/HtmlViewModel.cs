using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Web.ViewModels;

/// <summary>
/// Backing view-model for the <see cref="Views.HtmlView"/> tab.
/// Resolves a local .html or .url file path to a navigable URI and exposes
/// metadata for the tab title / toolbar.
/// </summary>
public partial class HtmlViewModel : ObservableObject, IPageViewModel
{
    /// <summary>The URI that WebView2 should navigate to.</summary>
    public Uri NavigationUri { get; }

    /// <summary>Display name shown in the toolbar (file name without path).</summary>
    public string FileName { get; }

    [ObservableProperty] private string _currentUrl = string.Empty;
    [ObservableProperty] private bool   _isLoading  = true;

    /// <summary>The loaded document's title (from WebView2), or empty before it's known.
    /// Drives the tab title when present; otherwise the URL-derived form is used.</summary>
    [ObservableProperty] private string _pageTitle = string.Empty;

    /// <summary>False when the embedded WebView2 control couldn't be started on this machine
    /// (e.g. the Edge WebView2 runtime is missing). The view swaps to a fallback panel that offers
    /// to open the page in the user's default browser instead.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WebViewVisible))]
    private bool _webViewAvailable = true;

    /// <summary>True while a shell-level modal overlay (AI response, Options, confirmation…) covers the
    /// page. The WebView's native HWND renders above all WPF content, so it must hide to let the overlay
    /// show. Driven by the view via <see cref="IAirspaceContent"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WebViewVisible))]
    private bool _isCovered;

    /// <summary>The WebView is shown only when it's available AND not hidden behind a shell overlay.</summary>
    public bool WebViewVisible => WebViewAvailable && !IsCovered;

    /// <summary>Set by the view: captures the live page as a downscaled PNG (null if unavailable).
    /// Used to feed the AI a screenshot of what the user is looking at.</summary>
    public Func<CancellationToken, Task<byte[]?>>? CaptureScreenshotAsync { get; set; }

    /// <summary>Set by the view: reads the current scroll position and content/viewport heights, so the AI
    /// can tell how much of the page is visible and whether there's more above/below. Null if unavailable.</summary>
    public Func<CancellationToken, Task<WebScrollInfo?>>? GetScrollInfoAsync { get; set; }

    /// <summary>Set by the view: scrolls by a signed fraction of the viewport height (e.g. +0.75 down,
    /// -0.75 up), then settles, so the AI can walk through a page taller than the viewport.</summary>
    public Func<double, CancellationToken, Task>? ScrollByViewportFractionAsync { get; set; }

    /// <summary>Human-readable reason the embedded browser is unavailable; shown on the fallback panel.</summary>
    [ObservableProperty] private string _failureMessage = string.Empty;

    /// <summary>True when the failure is specifically a missing Edge WebView2 runtime — drives the
    /// "install the runtime" link on the fallback panel.</summary>
    [ObservableProperty] private bool _runtimeMissing;

    public HtmlViewModel(string filePath)
    {
        FileName = Path.GetFileName(filePath);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        NavigationUri = ext == ".url"
            ? ResolveUrlShortcut(filePath)
            : new Uri(RealizeLocalFile(filePath));

        CurrentUrl = NavigationUri.ToString();
    }

    /// <summary>A real, WebView2-navigable path. A file inside a disk image / archive is virtual — WebView2
    /// can't load it, so materialise it to a temp copy first; a real on-disk file passes straight through.</summary>
    private static string RealizeLocalFile(string filePath)
    {
        try { return VirtualFileSystem.Instance.MaterializeFile(filePath); }
        catch { return filePath; }
    }

    /// <summary>
    /// Parses an Internet Shortcut (.url) file and extracts the URL= value.
    /// Falls back to about:blank on any error.
    /// </summary>
    private static Uri ResolveUrlShortcut(string filePath)
    {
        try
        {
            using var stream = VirtualFileSystem.Instance.OpenRead(filePath);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = line[4..].Trim();
                    if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                        return uri;
                }
            }
        }
        catch { /* fall through */ }

        return new Uri("about:blank");
    }

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext()
    {
        var loading = IsLoading ? " (loading)" : string.Empty;
        return $"Web view: '{CurrentUrl}'{loading}.";
    }

    /// <summary>
    /// Tools the AI can call to <em>see</em> the page (the URL and title alone don't convey the content):
    /// capture the current view, and scroll up/down to read a page taller than the viewport. Both are
    /// read-only (no approval prompt); each returns a screenshot plus how much of the page is visible.
    /// </summary>
    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new DelegateClientTool(
            "capture_web_page",
            "Capture a screenshot of the part of the page currently visible in this web view so you can " +
            "see what's actually rendered (text, layout, images). The result also tells you how much of " +
            "the whole page this view covers. Call it when the user asks about the page's content or appearance.",
            [],
            ToolSafety.ReadOnly,
            (_, ct) => BuildCaptureResultAsync(ct),
            parallelizable: false),

        new DelegateClientTool(
            "scroll_web_page",
            "Scroll the web view by about three quarters of its height, then return a fresh screenshot. " +
            "Use this to read a page taller than the viewport — scroll \"down\" repeatedly to walk to the " +
            "bottom (the result says when you've reached it), or \"up\" to go back.",
            [new ClientToolParameter("direction", "Which way to scroll: \"down\" (default) or \"up\".", Required: false)],
            ToolSafety.ReadOnly,
            ScrollWebPageToolAsync,
            parallelizable: false,
            exemptFromRepeatGuard: true),   // repeated "scroll down" is real progress, not spinning
    ];

    private async Task<ToolResult> ScrollWebPageToolAsync(JsonObject args, CancellationToken ct)
    {
        if (!WebViewAvailable || ScrollByViewportFractionAsync is null)
            return ToolResult.Error("The web page can't be scrolled right now.");

        var up = string.Equals(args["direction"]?.GetValue<string>()?.Trim(), "up", StringComparison.OrdinalIgnoreCase);
        await ScrollByViewportFractionAsync(up ? -0.75 : 0.75, ct);
        return await BuildCaptureResultAsync(ct);
    }

    /// <summary>Captures the current view and reports how much of the page it covers, as one tool result.</summary>
    private async Task<ToolResult> BuildCaptureResultAsync(CancellationToken ct)
    {
        if (!WebViewAvailable || CaptureScreenshotAsync is null)
            return ToolResult.Error("The web page can't be captured right now.");

        var png = await CaptureScreenshotAsync(ct);
        if (png is not { Length: > 0 })
            return ToolResult.Error("Couldn't capture the page image.");

        var info    = GetScrollInfoAsync is not null ? await GetScrollInfoAsync(ct) : null;
        var metrics = DescribeScroll(info);
        var text    = "A screenshot of the current web view is attached for you to look at."
                    + (metrics.Length > 0 ? " " + metrics : string.Empty);

        return ToolResult.Ok($"Captured the page ({CurrentUrl}).", text)
            with { Images = [new ContextImage(png, "image/png", CurrentUrl)] };
    }

    /// <summary>Describes, for the model, how much of the page the current view covers and where to scroll.</summary>
    private static string DescribeScroll(WebScrollInfo? info)
    {
        if (info is null || info.ViewportHeight <= 0) return string.Empty;

        double total = info.ContentHeight, view = info.ViewportHeight, y = info.ScrollY;
        if (total <= view + 1) return "The whole page fits in this view.";

        var startPct  = y / total * 100;
        var endPct    = Math.Min(100, (y + view) / total * 100);
        var viewports = total / view;
        var atTop     = y <= 1;
        var atBottom  = y + view >= total - 1;

        var hints = new List<string>();
        if (!atBottom) hints.Add("scroll down to see more below");
        if (!atTop)    hints.Add("scroll up to see content above");

        return $"This view covers about {startPct:0}–{endPct:0}% of the full page "
             + $"(the page is ~{viewports:0.0}× the viewport height). You can {string.Join(", or ", hints)}.";
    }
}

/// <summary>A snapshot of the web view's scroll state, read from the page via JavaScript.</summary>
/// <param name="ScrollY">Current vertical scroll offset, in CSS pixels.</param>
/// <param name="ContentHeight">Total scrollable content height (the loaded document), in CSS pixels.</param>
/// <param name="ViewportHeight">Visible viewport height, in CSS pixels.</param>
public sealed record WebScrollInfo(double ScrollY, double ContentHeight, double ViewportHeight);
