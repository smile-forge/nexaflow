namespace Nexaflow.Features.Common;

/// <summary>
/// Optional marker a feature puts on its shell-overlay view-model (shown via
/// <see cref="IShellServices.ShowOverlay"/>) to opt into backdrop-click dismissal. Overlays that don't
/// implement it (including the shell's built-in modals) dismiss only via their own buttons / Escape.
/// </summary>
public interface IShellOverlay
{
    /// <summary>When true, clicking the dimmed backdrop behind the overlay closes it.</summary>
    bool DismissOnBackdrop => false;
}
