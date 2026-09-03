namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>
/// Which C4 diagram was declared. The first five are Mermaid's keywords (and C4-PlantUML's
/// includes); <see cref="Sequence"/> is the Nexaflow extension mirroring <c>C4_Sequence.puml</c>,
/// which Mermaid has no keyword for.
/// </summary>
public enum C4DiagramKind { Context, Container, Component, Dynamic, Deployment, Sequence }

/// <summary>How people are drawn, set once for the whole diagram by the <c>SHOW_PERSON_*</c> macros.</summary>
public enum C4PersonStyle { Default, Outline, Portrait }

/// <summary>One element declaration — a Person, System, Container, Component or their variants.</summary>
public sealed class C4Element
{
    public required string Alias { get; init; }
    public string Label { get; set; } = string.Empty;
    public C4ElementKind Kind { get; set; } = C4ElementKind.System;
    public C4ElementShape Shape { get; set; } = C4ElementShape.Box;
    public bool External { get; set; }
    public string? Technology { get; set; }
    public string? Description { get; set; }
    /// <summary>The <c>$type</c> argument, which replaces the derived stereotype when given.</summary>
    public string? Type { get; set; }
    public string? Link { get; set; }
    public List<string> Tags { get; } = [];
    /// <summary>The boundary this element was declared inside, or null at the top level.</summary>
    public string? OwnerId { get; set; }
}

/// <summary>
/// A boundary or deployment node — the two are one type because they are the same thing structurally
/// (a named box that contains elements and other boundaries) and differ only in how they draw.
/// </summary>
public sealed class C4Boundary
{
    public required string Alias { get; init; }
    public string Label { get; set; } = string.Empty;
    /// <summary>The <c>$type</c>: <c>Enterprise</c>, <c>System</c>, <c>Container</c>, or a deployment
    /// node's technology (<c>Ubuntu 16.04 LTS</c>).</summary>
    public string? Type { get; set; }
    public string? Description { get; set; }
    public string? Link { get; set; }
    public bool IsDeploymentNode { get; set; }
    public string? ParentId { get; set; }
    public List<string> Tags { get; } = [];
    public List<string> MemberIds { get; } = [];
}

/// <summary>A relationship between two elements.</summary>
public sealed class C4Relationship
{
    public required string From { get; init; }
    public required string To { get; init; }
    public string Label { get; set; } = string.Empty;
    public string? Technology { get; set; }
    public string? Description { get; set; }
    public string? Link { get; set; }
    public List<string> Tags { get; } = [];
    /// <summary>The sequence number from <c>$index</c>/<c>RelIndex</c>; null when unnumbered.</summary>
    public int? Index { get; set; }
    /// <summary>A <c>BiRel</c> — an arrowhead at both ends.</summary>
    public bool Bidirectional { get; set; }
    /// <summary>A <c>Rel_Back</c> — declared from→to but pointing back the other way.</summary>
    public bool Back { get; set; }
}

/// <summary>
/// Colours and shape a <c>UpdateElementStyle</c> / <c>AddElementTag</c> / <c>AddBoundaryTag</c> sets.
/// Every field is optional: a tag that names only a background leaves everything else to the theme.
/// </summary>
public sealed class C4Style
{
    public string? BgColor { get; set; }
    public string? FontColor { get; set; }
    public string? BorderColor { get; set; }
    public C4ElementShape? Shape { get; set; }
    public string? Technology { get; set; }
    public string? LegendText { get; set; }
    public EdgeStyle? BorderStyle { get; set; }
    public double? BorderThickness { get; set; }

    /// <summary>Layers <paramref name="over"/> on top of this one; set fields win.</summary>
    public C4Style Merge(C4Style over) => new()
    {
        BgColor         = over.BgColor         ?? BgColor,
        FontColor       = over.FontColor       ?? FontColor,
        BorderColor     = over.BorderColor     ?? BorderColor,
        Shape           = over.Shape           ?? Shape,
        Technology      = over.Technology      ?? Technology,
        LegendText      = over.LegendText      ?? LegendText,
        BorderStyle     = over.BorderStyle     ?? BorderStyle,
        BorderThickness = over.BorderThickness ?? BorderThickness,
    };
}

/// <summary>Line and text colour a <c>UpdateRelStyle</c> / <c>AddRelTag</c> sets.</summary>
public sealed class C4RelStyle
{
    public string? TextColor { get; set; }
    public string? LineColor { get; set; }
    public EdgeStyle? LineStyle { get; set; }
    public string? LegendText { get; set; }
}

/// <summary>Front-matter <c>config: c4:</c> values. Recorded whether or not the renderer acts on them.</summary>
public sealed class C4Config
{
    public bool? Wrap { get; set; }
    public int? C4ShapeInRow { get; set; }
    public int? C4BoundaryInRow { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
}

/// <summary>Base of the ordered statement stream — see <see cref="C4Diagram.Statements"/>.</summary>
public abstract class C4Statement { }

public sealed class C4ElementStatement : C4Statement { public required C4Element Element { get; init; } }
public sealed class C4BoundaryBegin  : C4Statement { public required C4Boundary Boundary { get; init; } }
public sealed class C4BoundaryEnd    : C4Statement { }
public sealed class C4RelStatement   : C4Statement { public required C4Relationship Relationship { get; init; } }

/// <summary>A line the C4 reader did not claim, kept in order so a C4 sequence can hand it to the
/// native sequence parser (<c>alt</c>, <c>note over</c>, <c>activate</c>…).</summary>
public sealed class C4RawLine : C4Statement { public required string Line { get; init; } }

/// <summary>
/// Data model for a C4 diagram, shared by every kind: the structural kinds project it onto the graph
/// pipeline, and <see cref="C4DiagramKind.Sequence"/> projects it onto the sequence renderer.
///
/// Both the flat collections and the ordered <see cref="Statements"/> are kept. A structural diagram
/// only cares about the collections; a sequence diagram is *about* order, and also needs to know
/// where a boundary opened and closed relative to the elements inside it.
/// </summary>
public sealed class C4Diagram
{
    public C4DiagramKind Kind { get; set; } = C4DiagramKind.Context;
    public string Title { get; set; } = string.Empty;

    public List<C4Element> Elements { get; } = [];
    public List<C4Boundary> Boundaries { get; } = [];
    public List<C4Relationship> Relationships { get; } = [];

    /// <summary>Every statement in source order, including lines the reader did not claim.</summary>
    public List<C4Statement> Statements { get; } = [];

    /// <summary>Styles from <c>UpdateElementStyle</c>, keyed by the element type name it targeted
    /// (<c>person</c>, <c>external_system</c>, <c>container</c>…).</summary>
    public Dictionary<string, C4Style> ElementStyles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Styles from <c>AddElementTag</c> / <c>AddBoundaryTag</c>, keyed by tag name.</summary>
    public Dictionary<string, C4Style> Tags { get; } = new(StringComparer.Ordinal);

    /// <summary>Styles from <c>AddRelTag</c>, keyed by tag name.</summary>
    public Dictionary<string, C4RelStyle> RelTags { get; } = new(StringComparer.Ordinal);

    /// <summary>Styles from <c>UpdateRelStyle(from, to, …)</c>, keyed by the pair of endpoints.</summary>
    public Dictionary<(string From, string To), C4RelStyle> RelStyles { get; } = [];

    /// <summary>Set by <c>LAYOUT_TOP_DOWN()</c> / <c>LAYOUT_LEFT_RIGHT()</c>; null leaves the default.</summary>
    public GraphDirection? Direction { get; set; }

    public bool ShowLegend { get; set; }
    public bool HideStereotype { get; set; }
    public C4PersonStyle PersonStyle { get; set; } = C4PersonStyle.Default;

    /// <summary>C4 sequence hides element descriptions unless <c>SHOW_ELEMENT_DESCRIPTIONS()</c> asks.</summary>
    public bool ShowElementDescriptions { get; set; }

    /// <summary>C4 sequence numbers its messages only when <c>SHOW_INDEX()</c> asks.</summary>
    public bool ShowIndex { get; set; }

    public bool ShowFootBoxes { get; set; } = true;

    public C4Config Config { get; set; } = new();

    public C4Element? FindElement(string alias) =>
        Elements.FirstOrDefault(e => string.Equals(e.Alias, alias, StringComparison.Ordinal));

    public C4Boundary? FindBoundary(string alias) =>
        Boundaries.FirstOrDefault(b => string.Equals(b.Alias, alias, StringComparison.Ordinal));

    /// <summary>True when the diagram declared nothing worth drawing.</summary>
    public bool IsEmpty => Elements.Count == 0 && Boundaries.Count == 0 && Relationships.Count == 0;
}
