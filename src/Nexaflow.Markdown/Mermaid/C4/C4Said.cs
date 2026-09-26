using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>The colours and the outline a style asks for; null anywhere means the theme decides.</summary>
public sealed record C4Paint
{
    public string? Fill { get; init; }
    public string? Ink { get; init; }
    public string? Border { get; init; }
    public string? Shape { get; init; }
    public string? Says { get; init; }

    /// <summary>Lays another over this one, where what it sets wins and what it leaves alone does not.</summary>
    public C4Paint Over(C4Paint over) => new()
    {
        Fill = over.Fill ?? this.Fill,
        Ink = over.Ink ?? this.Ink,
        Border = over.Border ?? this.Border,
        Shape = over.Shape ?? this.Shape,
        Says = over.Says ?? this.Says,
    };
}

/// <summary>And what a relationship's line is drawn with.</summary>
public sealed record C4Stroke
{
    public string? Said { get; init; }
    public string? Ink { get; init; }
    public bool Dotted { get; init; }
    public string? Says { get; init; }
}

/// <summary>
/// One row of the key a <c>SHOW_LEGEND()</c> asks for: what it says, what is written for its colour, and which band of the
/// grading it takes where nothing is — so its swatch is the colour the cards it explains are actually drawn in.
/// </summary>
public sealed record C4Key(string Says, string? Fill, string? Border)
{
    public int? Tone { get; init; }
}

/// <summary>Which way a <c>LAYOUT_TOP_DOWN()</c> or a <c>LAYOUT_LEFT_RIGHT()</c> runs the diagram.</summary>
public enum C4Way { Down, Right }

/// <summary>
/// What the whole block says about itself: what it is switched to show, and what everything in it is styled with.
///
/// <para>
/// Both may be written anywhere — an <c>UpdateElementStyle</c> under the elements it is about, a <c>SHOW_INDEX()</c> at the
/// foot — so neither can be settled while the lines are being walked. This is the pass that settles them, and it is the same
/// pass for a structural diagram and a C4 sequence, since a switch means the same thing in both.
/// </para>
/// </summary>
internal sealed class C4Said
{
    /// <summary>What every switch and every style in a block comes to.</summary>
    public static C4Said Read(ContentNode tree)
    {
        var said = new C4Said();

        foreach (var macro in Macros(tree))
        {
            switch (macro.Name.ToLowerInvariant())
            {
                case "show_legend":
                case "layout_with_legend":
                    said.Legended = true;

                    // C4-PlantUML declares SHOW_LEGEND($hideStereotype="true", …): asking for a key hides the stereotypes,
                    // because the key now carries what they said.
                    if (macro.Flag(0, "hideStereotype")) said.Hidden = true;
                    break;

                case "hide_stereotype": said.Hidden = true; break;
                case "show_element_descriptions": said.Described = macro.Flag(0, "show"); break;
                case "show_index": said.Numbered = macro.Flag(0, "show"); break;
                case "show_foot_boxes": said.FootBoxes = macro.Flag(0, "show"); break;

                case "layout_top_down": said.Way = C4Way.Down; break;
                case "layout_left_right": said.Way = C4Way.Right; break;

                case "updateelementstyle":
                    if (macro.Said(0, "elementName") is { } target) Over(said.Styles, target, Styled(macro, 1));
                    break;

                case "addelementtag":
                case "addboundarytag":
                    if (macro.Said(0, "tagStereo") is { } tag) Over(said.Tags, tag, Styled(macro, 1));
                    break;

                case "updateboundarystyle":
                    Over(said.Tags, macro.Said(0, "elementName") ?? "boundary", Styled(macro, 1));
                    break;

                case "addreltag":
                    if (macro.Said(0, "tagStereo") is { } named) said.RelTags[named] = Lined(macro, 1);
                    break;

                case "updaterelstyle":
                    if (macro.Said(0, "from") is { } from && macro.Said(1, "to") is { } to)
                        said.RelStyles[(from, to)] = Lined(macro, 2);
                    break;
            }
        }

        if (said.Legended) said.Keyed(tree);

        return said;
    }

    /// <summary>Every macro called in a block, in the order they were written.</summary>
    public static IEnumerable<C4Macro> Macros(ContentNode tree)
    {
        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Line))
            if (line.Stated() is { Kind: C4Kinds.Macro } stated)
                yield return C4Macro.Of(stated);
    }

    public bool Legended { get; private set; }

    public bool Hidden { get; private set; }

    public bool Described { get; private set; }

    public bool Numbered { get; private set; }

    public bool FootBoxes { get; private set; } = true;

    /// <summary>Which way the diagram runs, or null where it says nothing and the reader's own default stands.</summary>
    public C4Way? Way { get; private set; }

    public List<C4Key> Legend { get; } = [];

    public Dictionary<string, C4Paint> Styles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, C4Paint> Tags { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, C4Stroke> RelTags { get; } = new(StringComparer.Ordinal);

    public Dictionary<(string From, string To), C4Stroke> RelStyles { get; } = [];

    /// <summary>
    /// The colours an element is drawn with: the tags it names first, then an <c>UpdateElementStyle</c> naming its C4 type as
    /// C4-PlantUML writes it, then one naming the element itself as Mermaid does — the most particular wins.
    /// </summary>
    public C4Paint Painted(C4Level level, C4Shape shape, bool external, string? alias, IReadOnlyList<string> tags)
    {
        var style = new C4Paint();

        foreach (var tag in tags)
            if (this.Tags.TryGetValue(tag, out var byTag)) style = style.Over(byTag);

        foreach (var key in C4Elements.Keys(level, shape, external))
            if (this.Styles.TryGetValue(key, out var byType)) style = style.Over(byType);

        if (alias is not null && this.Styles.TryGetValue(alias, out var byAlias)) style = style.Over(byAlias);

        return style;
    }

    /// <summary>And the colours a relationship is drawn with: its tags first, then a style naming this exact pair of ends.</summary>
    public C4Stroke Relating(string from, string to, IReadOnlyList<string> tags)
    {
        var style = new C4Stroke();

        foreach (var tag in tags)
            if (this.RelTags.TryGetValue(tag, out var byTag)) style = byTag;

        if (this.RelStyles.TryGetValue((from, to), out var byPair))
            style = new C4Stroke
            {
                Said = byPair.Said ?? style.Said,
                Ink = byPair.Ink ?? style.Ink,
                Dotted = byPair.Dotted || style.Dotted,
                Says = byPair.Says ?? style.Says,
            };

        return style;
    }

    /// <summary>What a boundary is painted with, which is whatever the first of its tags to name a colour says.</summary>
    public C4Paint Bounded(C4Macro macro, string? alias)
    {
        var style = new C4Paint();

        foreach (var tag in C4Macro.Tagged(macro.Named("tags")))
            if (this.Tags.TryGetValue(tag, out var byTag)) style = style.Over(byTag);

        if (this.Tags.TryGetValue("boundary", out var all)) style = all.Over(style);
        if (alias is not null && this.Styles.TryGetValue(alias, out var byAlias)) style = style.Over(byAlias);

        return style;
    }

    /// <summary>One row of the key for each kind of element written, and one for each tag that named its own.</summary>
    private void Keyed(ContentNode tree)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var macro in Macros(tree))
        {
            if (!C4Grammar.Elemental(macro.Name)) continue;

            var (level, shape, external) = C4Elements.Sorted(macro.Name);
            var says = C4Elements.Stereotyped(level, external, technology: null, over: null, hidden: false).Trim('[', ']');

            if (shape == C4Shape.Database) says += " (database)";
            else if (shape == C4Shape.Queue) says += " (queue)";

            if (!seen.Add(says)) continue;

            var style = this.Painted(level, shape, external, alias: null, []);
            this.Legend.Add(new C4Key(says, style.Fill, style.Border) { Tone = C4Elements.Banded(level, external) });
        }

        foreach (var (_, style) in this.Tags)
            if (style.Says is { Length: > 0 } text && seen.Add(text))
                this.Legend.Add(new C4Key(text, style.Fill, style.Border));

        foreach (var (_, style) in this.RelTags)
            if (style.Says is { Length: > 0 } text && seen.Add(text))
                this.Legend.Add(new C4Key(text, style.Ink, style.Ink));
    }

    private static C4Paint Styled(C4Macro macro, int first) => new()
    {
        Fill = macro.Said(first, "bgColor"),
        Ink = macro.Said(first + 1, "fontColor"),
        Border = macro.Said(first + 2, "borderColor"),
        Shape = macro.Named("shape")?.ToLowerInvariant(),
        Says = macro.Named("legendText"),
    };

    private static C4Stroke Lined(C4Macro macro, int first) => new()
    {
        Said = macro.Said(first, "textColor"),
        Ink = macro.Said(first + 1, "lineColor"),
        // C4-PlantUML writes the style as a call — DashedLine() — so it is what the name starts with that says which it is.
        Dotted = macro.Named("lineStyle")?.ToLowerInvariant() is { } style
                 && (style.StartsWith("dashed", StringComparison.Ordinal) || style.StartsWith("dotted", StringComparison.Ordinal)),
        Says = macro.Named("legendText"),
    };

    private static void Over(Dictionary<string, C4Paint> into, string key, C4Paint style) =>
        into[key] = into.TryGetValue(key, out var already) ? already.Over(style) : style;
}
