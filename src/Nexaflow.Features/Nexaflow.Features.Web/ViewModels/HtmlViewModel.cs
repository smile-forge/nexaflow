using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using System.IO;

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
    [ObservableProperty] private bool _webViewAvailable = true;

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
            : new Uri(filePath);

        CurrentUrl = NavigationUri.ToString();
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
}
