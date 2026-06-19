using Nexaflow.Features.Common.Ribbon;

namespace Nexaflow.Features.Common;

/// <summary>
/// Snapshots a dragged tab into a ribbon button — e.g. baking the current folder, registry key or
/// transform chain into the button so it always re-opens the exact state the user was viewing.
/// Resolved by <see cref="TabPageKind"/> (a tab already has a clean internal kind), distinct from the
/// drag-format matching used by <see cref="IRibbonPinHandler"/>. Auto-discovered by <see cref="FeatureManager"/>.
/// </summary>
public interface ITabPinHandler
{
    /// <summary>The page kind of tabs this handler snapshots.</summary>
    string TabPageKind { get; }

    /// <summary>Creates ribbon item metadata from a dragged tab, or returns <c>null</c> to reject.</summary>
    RibbonPinResult? Pin(Page tab, int insertIndex = -1);
}
