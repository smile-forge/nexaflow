using System;
using System.Windows.Controls;
using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Features.Common;

/// <summary>
/// Optional capability a page ViewModel implements to host AI responses inline — in a small banner
/// within the page — instead of the shell's modal overlay. The shell owns the reusable banner chrome
/// (progress, approval, final answer, "open in conversation", dissolve) and renders it into the control
/// returned by <see cref="GetHostUserControl"/>; the page only supplies the real estate (plus an optional
/// row cap) and is handed the banner's response handler so it can drive it itself — e.g. to post the
/// result of a command that finished in the background. A view-model that does NOT implement this falls
/// back to the shell's modal overlay, exactly as before.
/// </summary>
public interface IChatEngagement
{
    /// <summary>
    /// The empty host the shell injects its banner control into (it sets the host's <c>Content</c>). The
    /// shell also reads the host's available size to derive how many rows fit before a response escalates
    /// to the modal overlay. Created by the page's View (not the view-model), so the VM builds no WPF chrome.
    /// </summary>
    ContentControl GetHostUserControl();

    /// <summary>
    /// Optional hard cap on banner rows. Null (default) lets the shell derive the budget from the host's
    /// available height.
    /// </summary>
    int? MaxResponseRows => null;

    /// <summary>
    /// Raised when the page itself takes an action that should dissolve any visible banner (e.g. the user
    /// runs a command in the terminal). The shell subscribes while this page's banner is wired.
    /// </summary>
    event Action? EngagementInvalidated;

    /// <summary>
    /// The shell hands the page its banner-bound response handler (a Common-interface type — no Core
    /// dependency) when it wires the engagement, so the page can drive the banner directly — e.g. to render
    /// an asynchronous background-command result whether or not the page is the active tab. Default: no-op.
    /// </summary>
    void SetResponseHandler(IAIResponseHandler handler) { }
}
