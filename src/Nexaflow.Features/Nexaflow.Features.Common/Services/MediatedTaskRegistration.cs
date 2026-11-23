using System;


namespace Nexaflow.Features.Common;

/// <summary>
/// A user-mediated background task: a small control a page hands to the shell to be shown just left of
/// the background-activity ticker, so the user can keep driving the page's work (e.g. audio transport)
/// after leaving its tab. Registered via <c>IShellServices.RegisterMediatedTask</c>; the returned handle
/// is disposed to remove it (typically when the page reactivates or closes). Reference identity is the
/// registration token — the shell removes the exact instance it was handed.
/// </summary>
/// <param name="description">Short text describing the task, surfaced as the control's tooltip / accessibility label.</param>
/// <param name="model">What the shell shows for the task, drawn by the view marked as drawing its type.</param>
public sealed class MediatedTaskRegistration(string description, object model)
{
    /// <summary>Short text describing the task, surfaced as the control's tooltip / accessibility label.</summary>
    public string Description { get; } = description;

    /// <summary>
    /// What the shell shows for the task: drawn once per window that shows it, by the view marked as drawing its type, so
    /// it holds the live state and each view binds to it.
    /// </summary>
    public object Model { get; } = model;
}
