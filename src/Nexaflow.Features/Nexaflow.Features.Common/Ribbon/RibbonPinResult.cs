using System.Collections.Generic;

namespace Nexaflow.Features.Common;

/// <summary>
/// Data returned by <see cref="IRibbonPinHandler.Pin"/> describing how to render
/// the new ribbon button. Avoids coupling Common to the Core-level RibbonItem type.
/// </summary>
public sealed class RibbonPinResult
{
    public required string Label { get; init; }
    public required string Icon  { get; init; }
    public string? AccentColor   { get; init; }
    public Dictionary<string, string>? PageParams { get; init; }
}
