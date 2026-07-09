using System.Windows.Media;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Core.Controls;

/// <summary>A pickable colour from the shared swatch bank: the brush to show + the hex to store.</summary>
public sealed record SwatchOption(string Hex, Brush Brush);

/// <summary>
/// The shared categorical swatch bank resolved to brush+hex pairs for the per-workspace colour pickers
/// (the Options workspaces list and the Configure panel's identity page). Delegates to the single source
/// of truth, <see cref="SwatchPalette"/> in Visuals.Common, so the workspace pickers, the ribbon picker,
/// and the project status colours all offer the same palette from one definition.
/// </summary>
public static class WorkspaceSwatches
{
    /// <summary>Resolves the bank against the live application resources (throws on a missing key).</summary>
    public static IReadOnlyList<SwatchOption> Build()
        => [.. SwatchPalette.Build().Select(o => new SwatchOption(o.Hex, o.Brush))];
}
