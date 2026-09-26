using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;
using Nexaflow.Visuals.Common.Theming;
using Nexaflow.Visuals.Icons;
using Nexaflow.Icons;

namespace Nexaflow.Core.Models;

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
/// matching <see cref="Nexaflow.Features.Common.IRibbonPinHandler"/> is resolved by that format.
/// </summary>
public record RibbonPinRequest(string Format, object Payload, int InsertIndex = -1);

/// <summary>
/// Represents one item in the customisable ribbon. Every look property defaults to the theme's own, so an item
/// nobody styled follows the theme entirely.
/// </summary>
public partial class RibbonItem : ObservableObject
{
    public RibbonItemKind Kind { get; set; } = RibbonItemKind.Button;

    [ObservableProperty] private string  _label = string.Empty;
    [ObservableProperty] private IconRef _icon;
    [ObservableProperty] private bool    _isActive;

    /// <summary>When true the button renders compact (icon + label side-by-side) instead of full-height stacked.</summary>
    [ObservableProperty] private bool _isHalf;

    /// <summary>The icon and label colour. Observable so a live recolour flows through the ribbon's
    /// PropertyChanged→Save path.</summary>
    [ObservableProperty] private ColorSpec _foreground;

    /// <summary>The fill inside <see cref="Shape"/>.</summary>
    [ObservableProperty] private ColorSpec _background;

    /// <summary>The outline colour, drawn at <see cref="BorderWeight"/>.</summary>
    [ObservableProperty] private ColorSpec _borderColor;

    [ObservableProperty] private RibbonBorderWeight _borderWeight;

    [ObservableProperty] private RibbonButtonShape _shape;

    /// <summary>For Kind==HalfGroup: the two stacked sub-buttons.</summary>
    public List<RibbonItem>? HalfItems { get; set; }

    /// <summary>Command executed when the ribbon button is clicked.</summary>
    public ICommand? Command { get; set; }

    /// <summary>Runtime-only tab factory, re-attached after deserialisation.</summary>
    public Func<Page>? TabFactory { get; set; }

    /// <summary>
    /// Identifies the page type this button opens (see <see cref="PageKinds"/>).
    /// Persisted to ribbon.json; the runtime factory is restored on load via the page-kind lookup.
    /// </summary>
    public string? PageKind { get; set; }

    /// <summary>
    /// Page-type-specific initialisation parameters (e.g. {"Path":"C:\\Users\\..."}
    /// for a FileSystem page). Persisted alongside <see cref="PageKind"/>.
    /// </summary>
    public Dictionary<string, string>? PageParams { get; set; }

    /// <summary>A deep copy — the editor works on these, so nothing reaches the live ribbon until Done.</summary>
    public RibbonItem Clone() => new()
    {
        Kind         = Kind,
        Label        = Label,
        Icon         = Icon,
        IsActive     = IsActive,
        IsHalf       = IsHalf,
        Foreground   = Foreground,
        Background   = Background,
        BorderColor  = BorderColor,
        BorderWeight = BorderWeight,
        Shape        = Shape,
        HalfItems    = HalfItems?.Select(h => h.Clone()).ToList(),
        Command      = Command,
        TabFactory   = TabFactory,
        PageKind     = PageKind,
        PageParams   = PageParams is null ? null : new(PageParams),
    };
}
