using System.Collections.Generic;

namespace Nexaflow.Features.Common.Ribbon;

/// <summary>
/// Data returned by a pin handler describing how to render the new ribbon button.
/// Avoids coupling Common to the Core-level RibbonItem type.
/// </summary>
public sealed class RibbonPinResult
{
    /// <summary>
    /// Internal page kind the created button carries — persisted as the ribbon item's <c>PageKind</c>
    /// and used to route clicks (open a tab, or an <see cref="IRibbonItemExecutor"/>). This is the
    /// button's identity and is deliberately independent of whatever drag format triggered the pin.
    /// </summary>
    public required string PageKind { get; init; }
    public required string Label { get; init; }
    public required string Icon  { get; init; }
    public string? AccentColor   { get; init; }
    public Dictionary<string, string>? PageParams { get; init; }
}
