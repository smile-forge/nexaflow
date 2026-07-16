using System;
using System.Windows;

namespace Nexaflow.Features.Common;

/// <summary>
/// A user-mediated background task: a small control a page hands to the shell to be shown just left of
/// the background-activity ticker, so the user can keep driving the page's work (e.g. audio transport)
/// after leaving its tab. Registered via <c>IShellServices.RegisterMediatedTask</c>; the returned handle
/// is disposed to remove it (typically when the page reactivates or closes). Reference identity is the
/// registration token — the shell removes the exact instance it was handed.
/// </summary>
public sealed class MediatedTaskRegistration(string description, Func<FrameworkElement> createControl)
{
    /// <summary>Short text describing the task, surfaced as the control's tooltip / accessibility label.</summary>
    public string Description { get; } = description;

    /// <summary>
    /// Builds the chrome control. Invoked once per hosting window (a <see cref="FrameworkElement"/> has a single
    /// visual parent), so it must return a fresh element each call — bind it to the page's view-model for live state.
    /// </summary>
    public Func<FrameworkElement> CreateControl { get; } = createControl;
}
