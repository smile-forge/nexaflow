using System.Text;

namespace Nexaflow.Visuals.Text.Markdown.Graphs;

/// <summary>The C4 abstraction level an element sits at — what its stereotype says it is.</summary>
public enum C4ElementKind { Person, System, Container, Component, DeploymentNode }

/// <summary>
/// The outline a C4 element is drawn with.  Independent of <see cref="C4ElementKind"/> because the
/// two vary separately: a <c>ContainerDb</c> is a Container drawn as a cylinder, and
/// <c>SHOW_PERSON_PORTRAIT()</c> redraws every Person without changing what any of them are.
/// </summary>
public enum C4ElementShape
{
    Box,             // the default rounded card
    Person,          // card with a head bump above it
    PersonOutline,   // head + shoulders outline (SHOW_PERSON_OUTLINE)
    PersonPortrait,  // taller card with a framed head (SHOW_PERSON_PORTRAIT)
    Database,        // cylinder — SystemDb / ContainerDb / ComponentDb
    Queue,           // stadium — SystemQueue / ContainerQueue / ComponentQueue
}

/// <summary>
/// The content and styling of one C4 element card: what it is, what it is built with, and what it
/// does.  Carried by a graph <see cref="Node"/> (as <see cref="Node.C4"/>) for structural diagrams
/// and by a sequence participant for C4 sequence diagrams, so one painter serves both.
///
/// Colours are strings, not brushes, so this type stays WPF-free and can be built by a parser: they
/// are the literal <c>$bgColor</c>/<c>$fontColor</c>/<c>$borderColor</c> of an
/// <c>UpdateElementStyle</c> or a tag, and null means "the theme decides".
/// </summary>
public sealed class C4ElementInfo
{
    public C4ElementKind  Kind  { get; set; } = C4ElementKind.System;
    public C4ElementShape Shape { get; set; } = C4ElementShape.Box;

    /// <summary>True for the <c>_Ext</c> macros — drawn in the muted "someone else's problem" colour.</summary>
    public bool External { get; set; }

    /// <summary>The <c>$techn</c> argument (<c>"Java, Spring MVC"</c>), shown inside the stereotype.</summary>
    public string? Technology { get; set; }

    /// <summary>The <c>$descr</c> argument — the sentence under the title.</summary>
    public string? Description { get; set; }

    /// <summary>A <c>$type</c> that replaces the derived stereotype wholesale (<c>Boundary</c>, <c>Node</c>).</summary>
    public string? StereotypeOverride { get; set; }

    /// <summary>Set by <c>HIDE_STEREOTYPE()</c> — the card shows title and description only.</summary>
    public bool HideStereotype { get; set; }

    /// <summary>Tag names from <c>$tags="a+b"</c>, in order; the styles they name are resolved by the parser.</summary>
    public List<string> Tags { get; } = [];

    public string? FillColor   { get; set; }
    public string? FontColor   { get; set; }
    public string? BorderColor { get; set; }
    public EdgeStyle BorderStyle { get; set; } = EdgeStyle.Solid;
    public double BorderThickness { get; set; } = 1.5;

    /// <summary>True when the element carries any literal colour of its own.</summary>
    public bool HasLiteralColours => FillColor is not null || FontColor is not null || BorderColor is not null;

    /// <summary>
    /// The bracketed line under the title — C4's stereotype. <c>[Container: Spring MVC]</c>,
    /// <c>[Person (external)]</c>, <c>[Software System]</c>.  Empty when hidden, which is how
    /// <c>HIDE_STEREOTYPE()</c> and a sequence participant with no room both suppress it.
    /// </summary>
    public string Stereotype()
    {
        if (HideStereotype) return string.Empty;

        var sb = new StringBuilder("[");
        sb.Append(StereotypeOverride ?? KindLabel(Kind));
        if (External) sb.Append(" (external)");
        if (!string.IsNullOrWhiteSpace(Technology)) sb.Append(": ").Append(Technology!.Trim());
        return sb.Append(']').ToString();
    }

    private static string KindLabel(C4ElementKind kind) => kind switch
    {
        C4ElementKind.Person         => "Person",
        C4ElementKind.System         => "Software System",
        C4ElementKind.Container      => "Container",
        C4ElementKind.Component      => "Component",
        C4ElementKind.DeploymentNode => "Deployment Node",
        _                            => "Element",
    };

    /// <summary>A copy carrying every property — used when a graph view is derived from the parsed one.</summary>
    public C4ElementInfo Copy()
    {
        var c = new C4ElementInfo
        {
            Kind = Kind, Shape = Shape, External = External,
            Technology = Technology, Description = Description,
            StereotypeOverride = StereotypeOverride, HideStereotype = HideStereotype,
            FillColor = FillColor, FontColor = FontColor, BorderColor = BorderColor,
            BorderStyle = BorderStyle, BorderThickness = BorderThickness,
        };
        c.Tags.AddRange(Tags);
        return c;
    }
}

/// <summary>
/// How big a C4 element card has to be to hold its title, stereotype and description. Shared by the
/// Sugiyama layout (which reserves the footprint), the sequence renderer (which sizes its participant
/// columns) and the painter (which draws into it), so none of the three can disagree — the same
/// contract <see cref="ClassBoxMetrics"/> holds for class boxes.
///
/// A char-width heuristic rather than <c>FormattedText</c>, deliberately: the layout runs without a
/// dispatcher and must give the same answer every time, so the card measures the way the rest of the
/// layout measures. The painter wraps real text into the box this reserves, so the estimate is
/// generous rather than exact.
/// </summary>
public static class C4ElementMetrics
{
    /// <summary>Narrowest a card is drawn, so a one-word element still reads as a card.</summary>
    public const double MinW = 150;

    /// <summary>Widest a card grows before its text wraps instead — the reason a long description
    /// does not stretch its whole layer (see <see cref="NodeLabelMetrics"/>).</summary>
    public const double MaxW = 240;

    public const double PadX = 12;
    public const double PadY = 8;

    /// <summary>Height of one line of the bold title.</summary>
    public const double TitleRowH = 18;

    /// <summary>Height of the bracketed stereotype line.</summary>
    public const double MetaRowH = 14;

    /// <summary>Height of one line of the description.</summary>
    public const double DescRowH = 15;

    /// <summary>Gap between the stereotype and the description.</summary>
    public const double DescGap = 4;

    /// <summary>Advance width per character of the title (12pt semibold).</summary>
    public const double CharW = 6.8;

    /// <summary>Advance width per character of the stereotype and description (smaller faces).</summary>
    public const double MetaCharW = 5.6;
    public const double DescCharW = 6.0;

    /// <summary>Head bump drawn above a <see cref="C4ElementShape.Person"/> card.</summary>
    public const double PersonHeadH = 22;

    /// <summary>Framed head band of a <see cref="C4ElementShape.PersonPortrait"/> card.</summary>
    public const double PortraitH = 34;

    /// <summary>Cylinder cap of a <see cref="C4ElementShape.Database"/> card, top and bottom.</summary>
    public const double DbCapH = 8;

    /// <summary>Extra width a <see cref="C4ElementShape.Queue"/> stadium needs for its rounded ends.</summary>
    public const double QueueEndW = 16;

    /// <summary>The (width, height) a card needs — the footprint a layout must reserve.</summary>
    public static (double w, double h) Measure(string label, C4ElementInfo info)
    {
        string stereotype = info.Stereotype();
        string descr = info.Description ?? string.Empty;

        // Width: the widest piece of content, capped so long prose wraps instead of widening the layer.
        double cap = MaxW - 2 * PadX;
        double natural = Math.Max(
            Widest(label, CharW * 1.15),                     // the title renders semibold → a touch wider
            Math.Max(Widest(stereotype, MetaCharW), Widest(descr, DescCharW)));
        double content = Math.Min(natural, cap);
        double w = Math.Clamp(content + 2 * PadX, MinW, MaxW);
        if (info.Shape == C4ElementShape.Queue) w = Math.Min(MaxW, w + QueueEndW);

        // Height: the rows that content wraps into at that width, plus whatever the shape adds.
        double usable = Math.Max(1, w - 2 * PadX - (info.Shape == C4ElementShape.Queue ? QueueEndW : 0));
        double h = 2 * PadY
                 + Lines(label, CharW * 1.15, usable) * TitleRowH
                 + (stereotype.Length > 0 ? MetaRowH : 0)
                 + (descr.Length > 0 ? DescGap + Lines(descr, DescCharW, usable) * DescRowH : 0)
                 + ShapeAllowance(info.Shape);

        return (w, h);
    }

    /// <summary>Vertical room the outline needs beyond the text — a person's head, a cylinder's caps.</summary>
    public static double ShapeAllowance(C4ElementShape shape) => shape switch
    {
        C4ElementShape.Person         => PersonHeadH,
        C4ElementShape.PersonOutline  => PersonHeadH,
        C4ElementShape.PersonPortrait => PortraitH,
        C4ElementShape.Database       => 2 * DbCapH,
        _                             => 0,
    };

    /// <summary>Natural width of the widest explicit line of <paramref name="text"/>.</summary>
    private static double Widest(string text, double charWidth)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        double w = 0;
        foreach (var line in text.Split('\n')) w = Math.Max(w, line.Length * charWidth);
        return w;
    }

    /// <summary>How many rendered lines <paramref name="text"/> wraps into at <paramref name="usable"/> width.</summary>
    private static int Lines(string text, double charWidth, double usable)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int lines = 0;
        foreach (var line in text.Split('\n'))
            lines += Math.Max(1, (int)Math.Ceiling(line.Length * charWidth / usable));
        return lines;
    }
}
