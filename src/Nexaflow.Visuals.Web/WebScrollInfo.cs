namespace Nexaflow.Visuals.Web;

/// <summary>A snapshot of a web view's scroll state, read from the page via JavaScript.</summary>
/// <param name="ScrollY">Current vertical scroll offset, in CSS pixels.</param>
/// <param name="ContentHeight">Total scrollable content height (the loaded document), in CSS pixels.</param>
/// <param name="ViewportHeight">Visible viewport height, in CSS pixels.</param>
public sealed record WebScrollInfo(double ScrollY, double ContentHeight, double ViewportHeight);
