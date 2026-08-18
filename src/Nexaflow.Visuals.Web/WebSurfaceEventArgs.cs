using System;

namespace Nexaflow.Visuals.Web;

/// <summary>A navigation the surface started or finished.</summary>
/// <param name="Uri">The document being navigated to (or the one that just settled).</param>
public sealed class WebSurfaceNavigationEventArgs(string uri) : EventArgs
{
    public string Uri { get; } = uri;
}

/// <summary>
/// The embedded browser couldn't be created on this machine. Carries enough for the host to say
/// something useful — most often the Evergreen WebView2 runtime simply isn't installed.
/// </summary>
public sealed class WebSurfaceUnavailableEventArgs(Exception error, bool runtimeMissing, string message)
    : EventArgs
{
    public Exception Error          { get; } = error;
    public bool      RuntimeMissing { get; } = runtimeMissing;

    /// <summary>A sentence fit to show a user, already distinguishing "runtime missing" from "wouldn't start".</summary>
    public string    Message        { get; } = message;
}
