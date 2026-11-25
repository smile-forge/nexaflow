using Nexaflow.Icons;

using Nexaflow.Interaction;


namespace Nexaflow.Core.Models;

/// <summary>
/// One button, half-height group or separator of a workspace's ribbon, as its <c>ribbon.json</c> keeps it
/// (<see cref="Services.RibbonLayoutService"/>). It names no UI toolkit: its icon is named, each colour is its saved text — a
/// swatch (<c>swatch:Teal</c>), a hex, or null for the theme's — and a window draws it. It opens nothing itself: a window routes
/// its page kind and parameters when it is pressed there.
/// </summary>
public sealed class RibbonEntry
{
    public RibbonItemKind Kind { get; set; } = RibbonItemKind.Button;
    public string Label { get; set; } = string.Empty;
    public IconRef Icon { get; set; }

    /// <summary>The picture drawn instead of <see cref="Icon"/>, as a <c>DefaultIcon</c> string: <c>file,index</c>.</summary>
    public string? IconResource { get; set; }
    public bool IsHalf { get; set; }

    /// <summary>The icon and label's colour as saved, or null for the theme's.</summary>
    public string? Foreground { get; set; }

    /// <summary>The fill inside <see cref="Shape"/> as saved, or null for the theme's.</summary>
    public string? Background { get; set; }

    /// <summary>The outline colour as saved, drawn at <see cref="BorderWeight"/>, or null for the theme's.</summary>
    public string? BorderColor { get; set; }
    public RibbonBorderWeight BorderWeight { get; set; }
    public RibbonButtonShape Shape { get; set; }

    /// <summary>Version 1's one colour, read only: it is the icon and label's, and <see cref="Foreground"/> takes it on load.</summary>
    public string? AccentColor { get; set; }

    /// <summary>The page kind the button opens.</summary>
    public string? PageKind { get; set; }

    /// <summary>What the page is opened on — a folder for a file-system page.</summary>
    public Dictionary<string, string>? PageParams { get; set; }

    /// <summary>For a <see cref="RibbonItemKind.HalfGroup"/>: the two stacked buttons.</summary>
    public List<RibbonEntry>? HalfItems { get; set; }
}

public enum RibbonItemKind { Button, HalfGroup, Separator }

/// <summary>
/// The outline of a ribbon button. The first four frame the whole button; <see cref="Circle"/>,
/// <see cref="Squircle"/> and <see cref="Hexagon"/> are badges drawn behind the icon, with the label beneath or beside.
/// </summary>
public enum RibbonButtonShape { Standard, Rounded, Pill, Leaf, Circle, Squircle, Hexagon }

/// <summary>How heavy a ribbon button's outline is; <see cref="None"/> draws none.</summary>
public enum RibbonBorderWeight { None, Thin, Medium, Thick }

/// <summary>
/// Carries a tab drag-and-drop pin request, optionally with an insert index.
/// </summary>
public record TabPinRequest(Page Tab, int InsertIndex = -1);

/// <summary>
/// Carries a handler-based pin request: a payload tagged with the drag-data <c>Format</c> it arrived as.
/// Used for drag-drop of non-tab content (e.g. file actions, a browser URL) onto the ribbon; the
/// matching <see cref="Nexaflow.Features.Common.IRibbonPinHandler"/> is resolved by the kinds the payload carries.
/// </summary>
public record RibbonPinRequest(IDragPayload Payload, int InsertIndex = -1);
