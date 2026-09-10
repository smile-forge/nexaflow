using System;
using System.Windows;
using System.Windows.Input;

namespace Nexaflow.Features.WindowsFileSystem;

/// <summary>
/// A press that may become a drag: where it landed, and whether it is still live.
/// <para>
/// It exists because "still live" is the part that kept going wrong. Arming was a bare bool set on
/// mouse-down and cleared only when a drag actually fired, so it outlived the gesture that set it —
/// and a press whose release the control never saw left it armed indefinitely, pointing at a
/// position the cursor had long since left. A menu holding mouse capture swallows exactly that
/// release, so dismissing the drop-choice menu re-armed nothing but woke what was already armed:
/// the next mouse move was instantly "far enough" from a stale origin, a drag began that nobody
/// started, and it landed a copy wherever the button came up.
/// </para>
/// <para>
/// So the rule is stated once, here, rather than at each of the three surfaces that need it: arming
/// does not survive the button coming back up, whether or not the up was seen.
/// <see cref="ObserveButton"/> is what makes an unseen release harmless, because a move reporting
/// the button up says the same thing the release would have.
/// </para>
/// </summary>
internal sealed class DragArming
{
    private Point _origin;

    /// <summary>True while a press could still turn into a drag.</summary>
    public bool IsArmed { get; private set; }

    /// <summary>Notes a press at <paramref name="at"/>, in whatever coordinates the caller compares in.</summary>
    public void Arm(Point at)
    {
        _origin = at;
        IsArmed = true;
    }

    public void Disarm() => IsArmed = false;

    /// <summary>
    /// Takes the button's word for it. Anything but pressed disarms — this is the guard that a
    /// release consumed by something else cannot get past.
    /// </summary>
    public void ObserveButton(MouseButtonState state)
    {
        if (state != MouseButtonState.Pressed) IsArmed = false;
    }

    /// <summary>
    /// True exactly once: when the pointer has travelled far enough from the press for Windows to
    /// call it a drag. Disarms as it says so, so one press cannot start two drags.
    /// </summary>
    public bool ShouldStart(Point now)
    {
        if (!IsArmed) return false;

        if (Math.Abs(now.X - _origin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(now.Y - _origin.Y) < SystemParameters.MinimumVerticalDragDistance)
            return false;

        IsArmed = false;
        return true;
    }
}
