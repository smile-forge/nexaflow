namespace Nexaflow.Core.Models;

/// <summary>
/// One monitor as the desktop lays it out. <see cref="Id"/> is the monitor's device path, which survives reboots,
/// docking and <c>\\.\DISPLAYn</c> renumbering; where Windows gives none (a remote session, some virtual displays)
/// it is the display device name. <see cref="Bounds"/> and <see cref="WorkArea"/> (the bounds less the taskbar and
/// docked toolbars) are in screen pixels.
/// </summary>
public sealed record MonitorArea(string Id, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary);
