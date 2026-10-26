using System.Text.Json.Serialization;

namespace Nexaflow.Core.Models;

/// <summary>
/// Where a window is meant to be: its normal (restored) rectangle, whether it is maximised over that, and the
/// monitor both belong to. <see cref="Normal"/> is in Win32 workspace coordinates, as
/// <c>WINDOWPLACEMENT.rcNormalPosition</c> holds them: screen coordinates less the offset of the monitor's work
/// area from its bounds, so relative to the monitor's origin they are relative to its work area. Minimised is
/// never a placement — a window minimised from maximised is <see cref="Maximized"/>, any other is normal.
/// Kept live by <see cref="Services.WindowPlacementTracker"/> and saved as
/// <see cref="Workspace.LastWindowPlacement"/>.
/// </summary>
public sealed record WindowPlacement(PixelRect Normal, bool Maximized, MonitorArea Monitor)
{
    internal const int SW_SHOWNORMAL          = 1;
    internal const int SW_SHOWMINIMIZED       = 2;
    internal const int SW_SHOWMAXIMIZED       = 3;
    internal const int WPF_RESTORETOMAXIMIZED = 0x0002;

    /// <summary>A hand-edited or half-written entry is never applied: fitting it needs a size and a monitor.</summary>
    [JsonIgnore]
    public bool IsUsable => !Normal.IsEmpty
                         && Monitor is { Id.Length: > 0 } monitor
                         && !monitor.Bounds.IsEmpty
                         && !monitor.WorkArea.IsEmpty;

    /// <summary>
    /// The placement a window's <c>WINDOWPLACEMENT</c> describes, on <paramref name="monitor"/>.
    /// <paramref name="shown"/> is the window's live rectangle in screen pixels, used only in the normal state: a
    /// window snapped to half a monitor shows a different rectangle from its restore one, and the one it shows is
    /// where the user put it.
    /// </summary>
    internal static WindowPlacement FromNative(int showCmd, int flags, PixelRect normalPosition, PixelRect? shown,
                                               MonitorArea monitor)
    {
        bool maximized = showCmd == SW_SHOWMAXIMIZED
                      || (showCmd == SW_SHOWMINIMIZED && (flags & WPF_RESTORETOMAXIMIZED) != 0);
        bool normalState = showCmd is not (SW_SHOWMINIMIZED or SW_SHOWMAXIMIZED);

        var normal = normalState && shown is { IsEmpty: false } live
            ? live.Offset(-(monitor.WorkArea.Left - monitor.Bounds.Left), -(monitor.WorkArea.Top - monitor.Bounds.Top))
            : normalPosition;
        return new WindowPlacement(normal, maximized, monitor);
    }

    /// <summary>True when <paramref name="other"/> puts the window in the same place: same rectangle, same state, same monitor.</summary>
    internal bool SamePlaceAs(WindowPlacement other)
        => Normal == other.Normal && Maximized == other.Maximized && Monitor.Id == other.Monitor.Id;

    /// <summary>
    /// This placement put onto the monitors <paramref name="attached"/> now. On its own monitor laid out as it was,
    /// it is returned unchanged — a window the user stretched across two monitors stays stretched. Otherwise it is
    /// carried to its own monitor where that has moved or changed size (another resolution, scale or taskbar), or,
    /// with that monitor missing, to the monitor it overlapped most (whichever now occupies its place on the
    /// desktop) or the primary — its position and size in proportion to the work area, and wholly inside it.
    /// <paramref name="onOwnMonitor"/> says whether its own monitor was there.
    /// </summary>
    internal WindowPlacement FitTo(IReadOnlyList<MonitorArea> attached, out bool onOwnMonitor)
    {
        var own = attached.FirstOrDefault(m => m.Id == Monitor.Id);
        onOwnMonitor = own is not null;
        if (own is not null && own.Bounds == Monitor.Bounds && own.WorkArea == Monitor.WorkArea)
            return this with { Monitor = own };

        var target = own ?? MostOverlapped(attached);
        if (target is null) return this;   // nothing enumerated: SetWindowPlacement keeps it on screen itself

        var from = Monitor.WorkArea;
        var to   = target.WorkArea;
        double scaleX = (double)to.Width  / from.Width;
        double scaleY = (double)to.Height / from.Height;

        int width  = Math.Min((int)Math.Round(Normal.Width  * scaleX), to.Width);
        int height = Math.Min((int)Math.Round(Normal.Height * scaleY), to.Height);
        int x = Math.Clamp((int)Math.Round((Normal.Left - Monitor.Bounds.Left) * scaleX), 0, to.Width  - width);
        int y = Math.Clamp((int)Math.Round((Normal.Top  - Monitor.Bounds.Top)  * scaleY), 0, to.Height - height);

        var normal = new PixelRect(target.Bounds.Left + x, target.Bounds.Top + y,
                                   target.Bounds.Left + x + width, target.Bounds.Top + y + height);
        return new WindowPlacement(normal, Maximized, target);
    }

    private MonitorArea? MostOverlapped(IReadOnlyList<MonitorArea> attached)
    {
        var onScreen = Normal.Offset(Monitor.WorkArea.Left - Monitor.Bounds.Left, Monitor.WorkArea.Top - Monitor.Bounds.Top);
        return attached.Where(m => m.Bounds.OverlapArea(onScreen) > 0)
                       .MaxBy(m => m.Bounds.OverlapArea(onScreen))
            ?? attached.FirstOrDefault(m => m.IsPrimary)
            ?? attached.FirstOrDefault();
    }
}
