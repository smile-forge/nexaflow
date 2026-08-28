using Microsoft.Web.WebView2.Core;

namespace Nexaflow.Visuals.Web;

/// <summary>
/// Asks whether the Evergreen WebView2 runtime is installed, WITHOUT starting a browser.
/// <para>
/// This exists so a host can answer "is the runtime here?" before it builds a <see cref="Controls.WebSurface"/>.
/// It stays here rather than in the hosting feature because the WebView2 package is deliberately fenced
/// inside this assembly — <c>WebSurface</c> exposes no <c>CoreWebView2</c> escape hatch precisely so
/// consumers never take a reference on it. A plain version string crosses the boundary instead.
/// </para>
/// </summary>
public static class WebView2Runtime
{
    /// <summary>
    /// The installed Evergreen runtime's version, or null when it isn't installed. Never throws:
    /// the underlying call throws rather than returning null when no runtime is found, and a machine
    /// missing the native loader entirely fails at a lower level again.
    /// </summary>
    public static string? TryGetVersion()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch
        {
            // WebView2RuntimeNotFoundException for "not installed", but also DllNotFoundException /
            // BadImageFormatException when WebView2Loader.dll is missing or the wrong architecture.
            // Every one of them means the same thing to a caller: you cannot render with it here.
            return null;
        }
    }
}
