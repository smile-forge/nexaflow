using System.Globalization;
using System.Text;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Sequence;

namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>
/// A <c>C4Sequence</c> block, read into the very same <see cref="SequenceDiagram"/> a <c>sequenceDiagram</c> is read into.
/// It says what a sequence says in C4-PlantUML's words: an element macro is a participant whose box is a card, a
/// <c>Boundary</c> is the box grouping the lifelines it holds, and a <c>Rel</c> is a message carrying what it is done with.
///
/// <para>
/// <strong>Only the macros are read here.</strong> Everything else on the timeline — <c>alt</c>, <c>loop</c>,
/// <c>note over</c>, <c>activate</c> — falls through to the sequence diagram's own reading
/// (<see cref="SequenceDiagram.Read(MermaidBlock, SequenceConfig, Func{SequenceReading, ContentPart, string, bool}, IReadOnlyList{SequenceLegend})"/>),
/// so the two nest round each other correctly without a second copy of how a sequence is read.
/// </para>
///
/// <para>
/// <strong>What is switched on and what is styled is read first.</strong> <c>SHOW_INDEX()</c>, <c>HIDE_STEREOTYPE()</c>
/// and an <c>UpdateElementStyle</c> may be written under the elements they are about, so they are facts about the whole
/// block; the timeline is then read with them already known.
/// </para>
/// </summary>
public static class C4Sequence
{
    /// <summary>Reads a block: parsed, then worked over by its stages (<see cref="MermaidParser.Read"/>).</summary>
    public static SequenceDiagram Read(string? block) => Of(MermaidParser.Read(block));

    /// <summary>Reads a tree the stages have already been over.</summary>
    public static SequenceDiagram Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    public static SequenceDiagram Of(MermaidBlock block)
    {
        var said = Gathered(block);
        var counter = new Counter();

        var config = C4Config.Read(block.Config) with { Mirrored = said.FootBoxes };

        return SequenceDiagram.Read(block, config, (read, stated, inside) => Claimed(read, stated, inside, said, counter),
                                    said.Legend);
    }

    // ── What the whole block says ───────────────────────────────────────────

    /// <summary>
    /// The first pass: what the diagram is switched to show, and what everything in it is styled with. Both may be written
    /// anywhere, so neither can be settled while the timeline is being walked.
    /// </summary>
    private static Told Gathered(MermaidBlock block)
    {
        var said = new Told();

        foreach (var line in block.Reading.Root.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Line))
        {
            if (line.Stated() is not { Kind: C4Kinds.Macro } stated) continue;

            var macro = Called(stated);

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

        if (said.Legended) Keyed(block, said);

        return said;
    }

    /// <summary>One row of the key for each kind of element written, and one for each tag that named its own.</summary>
    private static void Keyed(MermaidBlock block, Told said)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in block.Reading.Root.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Line))
        {
            if (line.Stated() is not { Kind: C4Kinds.Macro } stated) continue;

            var macro = Called(stated);
            if (!C4Grammar.Elemental(macro.Name)) continue;

            var (kind, shape, external) = Sorted(macro.Name);
            var says = Stereotyped(kind, external, technology: null, over: null, hidden: false).Trim('[', ']');

            if (!seen.Add(says)) continue;

            var style = Styles(said, kind, shape, external, alias: null, []);
            said.Legend.Add(new SequenceLegend(says, style.Fill, style.Border));
        }

        foreach (var (_, style) in said.Tags)
            if (style.Says is { Length: > 0 } text && seen.Add(text))
                said.Legend.Add(new SequenceLegend(text, style.Fill, style.Border));

        foreach (var (_, style) in said.RelTags)
            if (style.Says is { Length: > 0 } text && seen.Add(text))
                said.Legend.Add(new SequenceLegend(text, style.Ink, style.Ink));
    }

    // ── What each line is ───────────────────────────────────────────────────

    /// <summary>Whether this line is one of C4's, and what it puts on the timeline where it is.</summary>
    private static bool Claimed(SequenceReading read, ContentPart stated, string? inside, Told said, Counter counter)
    {
        switch (stated.Kind)
        {
            case C4Kinds.Aside:
                return true;

            case C4Kinds.Boundary:
                var boundary = Called(stated);
                read.Opens(stated, boundary.Part(1, "label") ?? boundary.Part(0, "alias"), Tinted(said, boundary), inside);

                return true;

            case C4Kinds.Macro:
                Macroed(read, stated, inside, said, counter, Called(stated));
                return true;

            default:
                return false;
        }
    }

    private static void Macroed(SequenceReading read, ContentPart stated, string? inside, Told said, Counter counter,
                                Macro macro)
    {
        var word = macro.Name.ToLowerInvariant();

        if (C4Grammar.Elemental(macro.Name))
        {
            Standing(read, stated, inside, said, macro);
            return;
        }

        if (word.StartsWith("relindex", StringComparison.Ordinal)) Related(read, stated, said, counter, macro, 1, false, false);
        else if (word.StartsWith("rel_back", StringComparison.Ordinal)) Related(read, stated, said, counter, macro, 0, true, false);
        else if (word.StartsWith("birel", StringComparison.Ordinal)) Related(read, stated, said, counter, macro, 0, false, true);
        else if (word.StartsWith("rel", StringComparison.Ordinal)) Related(read, stated, said, counter, macro, 0, false, false);
        else if (word == "increment") counter.Increment(Number(macro.Said(0, "offset")) ?? 1);
        else if (word == "setindex" && Number(macro.Said(0, "new_index")) is { } at) counter.Set(at);
    }

    /// <summary>An element: a participant whose box is a card saying what it is and what it does.</summary>
    private static void Standing(SequenceReading read, ContentPart stated, string? inside, Told said, Macro macro)
    {
        if (macro.Part(0, "alias") is not { Length: > 0 } alias) return;

        var (kind, shape, external) = Sorted(macro.Name);

        // A Person and a System take (alias, label, descr); a Container and a Component put what they are built with at 2
        // and push the description to 3. That asymmetry is C4-PlantUML's.
        var built = kind is "container" or "component";
        var technology = macro.Part(built ? 2 : -1, "techn");
        var described = macro.Part(built ? 3 : 2, "descr");
        var tags = Tagged(macro.Said(built ? 5 : 4, "tags"));

        var one = read.Called(alias, alias.Text);
        var style = Styles(said, kind, shape, external, alias.Text, tags);

        one.Said = macro.Part(1, "label") ?? alias;
        one.Card = new SequenceCard
        {
            Stereotype = Stereotyped(kind, external, technology?.Text, macro.Named("type"), said.Hidden),
            Technology = technology,
            Said = said.Described ? described : null,
            Shape = Shaped(kind, shape, style.Shape),
            Fill = style.Fill,
            Ink = style.Ink,
            Border = style.Border,
        };

        if (inside is not null && read.Boxes.Any(box => string.Equals(box.Key, inside, StringComparison.Ordinal)))
            one.Box ??= inside;
    }

    /// <summary>A relationship: a message from one lifeline to another, carrying what it is done with.</summary>
    private static void Related(SequenceReading read, ContentPart stated, Told said, Counter counter, Macro macro,
                                int offset, bool back, bool both)
    {
        if (macro.Part(offset, "from") is not { Length: > 0 } one) return;
        if (macro.Part(offset + 1, "to") is not { Length: > 0 } other) return;

        // Rel_Back points the other way, so the message is simply built reversed.
        var from = read.Called(back ? other : one, (back ? other : one).Text);
        var to = read.Called(back ? one : other, (back ? one : other).Text);

        from.Reached = true;
        to.Reached = true;

        var style = Relating(said, one.Text, other.Text, Tagged(macro.Said(offset + 6, "tags")));
        var number = offset == 1 ? counter.Resolve(macro.Said(0, "index")) : counter.Resolve(macro.Named("index"));

        read.Items.Add(new SequenceMessage(stated, from.Id, to.Id, read.Items.Count)
        {
            Said = macro.Part(offset + 2, "label"),
            Under = [.. new[] { macro.Part(offset + 3, "techn"), macro.Part(offset + 4, "descr") }.OfType<ContentPart>()],
            Near = both ? SequenceHead.Arrow : SequenceHead.None,
            Far = SequenceHead.Arrow,
            Dotted = style.Dotted,
            Ink = style.Ink,
            SaidInk = style.Said,
            Number = said.Numbered ? (number ?? counter.Next()).ToString(CultureInfo.InvariantCulture) : null,
        });
    }

    // ── What a macro says ───────────────────────────────────────────────────

    /// <summary>One macro call, read back: its name and its arguments in the order they were written.</summary>
    private static Macro Called(ContentPart stated)
    {
        var name = stated.SelfAndDescendants()
                         .FirstOrDefault(part => part.Kind == MermaidKinds.Key && part.Role == C4Roles.Macro)?.Text
                   ?? string.Empty;

        var arguments = new List<Argument>();

        foreach (var property in stated.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Property))
        {
            var key = Inner(property, MermaidKinds.Words, C4Roles.Key);
            var value = Inner(property, MermaidKinds.Words, C4Roles.Value)
                        ?? Inner(property, MermaidKinds.Words, SequenceRoles.Id);

            arguments.Add(new Argument(key?.Text, value));
        }

        return new Macro(name, arguments);
    }

    private static ContentPart? Inner(ContentPart property, string kind, string role) =>
        property.SelfAndDescendants().FirstOrDefault(part => part.Kind == kind && part.Role == role);

    // ── What it comes to ────────────────────────────────────────────────────

    /// <summary>What an element macro's name says it is: the C4 level, the outline, and whether it is somebody else's.</summary>
    private static (string Kind, string Shape, bool External) Sorted(string name)
    {
        var word = name.ToLowerInvariant();
        var external = word.EndsWith("_ext", StringComparison.Ordinal);
        if (external) word = word[..^4];

        foreach (var kind in new[] { "person", "system", "container", "component" })
        {
            if (!word.StartsWith(kind, StringComparison.Ordinal)) continue;

            return (kind, word[kind.Length..] switch { "db" => "database", "queue" => "queue", _ => "box" }, external);
        }

        return ("system", "box", external);
    }

    /// <summary>The line in brackets under a card's name — C4's stereotype, which nobody writes as one run.</summary>
    private static string Stereotyped(string kind, bool external, string? technology, string? over, bool hidden)
    {
        if (hidden) return string.Empty;

        var says = new StringBuilder("[");
        says.Append(over ?? Worded(kind));

        if (external) says.Append(" (external)");
        if (technology is { Length: > 0 }) says.Append(": ").Append(technology.Trim());

        return says.Append(']').ToString();
    }

    private static string Worded(string kind) => kind switch
    {
        "person" => "Person",
        "container" => "Container",
        "component" => "Component",
        _ => "Software System",
    };

    /// <summary>
    /// The outline it is drawn with: what a style asks for where one does, and otherwise what its macro said. A person is a
    /// card with a head above it — the three <c>SHOW_PERSON_*</c> styles are one shape here, since a lifeline's head is a
    /// column heading and the three differ only in how much of a figure they draw above the same card.
    /// </summary>
    private static SequenceCardShape Shaped(string kind, string shape, string? over) => over switch
    {
        "database" or "db" => SequenceCardShape.Database,
        "queue" => SequenceCardShape.Queue,
        "rounded" or "roundedboxshape" or "eightsidedshape" => SequenceCardShape.Box,
        _ => shape switch
        {
            "database" => SequenceCardShape.Database,
            "queue" => SequenceCardShape.Queue,
            _ => kind == "person" ? SequenceCardShape.Person : SequenceCardShape.Box,
        },
    };

    /// <summary>
    /// The colours an element is drawn with: the tags it names first, then an <c>UpdateElementStyle</c> naming its C4 type as
    /// C4-PlantUML writes it, then one naming the element itself as Mermaid does.
    /// </summary>
    private static Paint Styles(Told said, string kind, string shape, bool external, string? alias, IReadOnlyList<string> tags)
    {
        var style = new Paint();

        foreach (var tag in tags)
            if (said.Tags.TryGetValue(tag, out var byTag)) style = style.Over(byTag);

        foreach (var key in Keys(kind, shape, external))
            if (said.Styles.TryGetValue(key, out var byType)) style = style.Over(byType);

        if (alias is not null && said.Styles.TryGetValue(alias, out var byAlias)) style = style.Over(byAlias);

        return style;
    }

    /// <summary>The keys an <c>UpdateElementStyle</c> may have named this element by, in C4-PlantUML's vocabulary.</summary>
    private static IEnumerable<string> Keys(string kind, string shape, bool external)
    {
        var suffix = shape switch { "database" => "_db", "queue" => "_queue", _ => string.Empty };

        yield return kind;
        if (suffix.Length > 0) yield return kind + suffix;

        if (!external) yield break;

        yield return "external_" + kind;
        if (suffix.Length > 0) yield return "external_" + kind + suffix;
    }

    /// <summary>And the colours a relationship is drawn with: its tags first, then a style naming this exact pair of ends.</summary>
    private static Stroke Relating(Told said, string from, string to, IReadOnlyList<string> tags)
    {
        var style = new Stroke();

        foreach (var tag in tags)
            if (said.RelTags.TryGetValue(tag, out var byTag)) style = byTag;

        if (said.RelStyles.TryGetValue((from, to), out var byPair))
            style = new Stroke
            {
                Said = byPair.Said ?? style.Said,
                Ink = byPair.Ink ?? style.Ink,
                Dotted = byPair.Dotted || style.Dotted,
                Says = byPair.Says ?? style.Says,
            };

        return style;
    }

    private static Paint Styled(Macro macro, int first) => new()
    {
        Fill = macro.Said(first, "bgColor"),
        Ink = macro.Said(first + 1, "fontColor"),
        Border = macro.Said(first + 2, "borderColor"),
        Shape = macro.Named("shape")?.ToLowerInvariant(),
        Says = macro.Named("legendText"),
    };

    private static Stroke Lined(Macro macro, int first) => new()
    {
        Said = macro.Said(first, "textColor"),
        Ink = macro.Said(first + 1, "lineColor"),
        // C4-PlantUML writes the style as a call — DashedLine() — so it is what the name starts with that says which it is.
        Dotted = macro.Named("lineStyle")?.ToLowerInvariant() is { } style
                 && (style.StartsWith("dashed", StringComparison.Ordinal) || style.StartsWith("dotted", StringComparison.Ordinal)),
        Says = macro.Named("legendText"),
    };

    private static string? Tinted(Told said, Macro macro)
    {
        foreach (var tag in Tagged(macro.Named("tags")))
            if (said.Tags.TryGetValue(tag, out var style) && style.Fill is { Length: > 0 } fill) return fill;

        return null;
    }

    private static void Over(Dictionary<string, Paint> into, string key, Paint style) =>
        into[key] = into.TryGetValue(key, out var already) ? already.Over(style) : style;

    private static IReadOnlyList<string> Tagged(string? tags) =>
        tags is { Length: > 0 } ? [.. tags.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)] : [];

    private static int? Number(string? said) => MermaidNumber.Read(said) is { } value ? (int)value : null;

    // ── What is read while the block is walked ──────────────────────────────

    /// <summary>One macro call and its arguments, in the order written.</summary>
    private sealed record Macro(string Name, IReadOnlyList<Argument> Arguments)
    {
        /// <summary>
        /// The argument at a position or under a name: a name wins, which is how C4-PlantUML lets a caller skip the middle of
        /// a long signature. Null where it was given neither way, or given empty.
        /// </summary>
        public ContentPart? Part(int position, string name)
        {
            foreach (var argument in this.Arguments)
                if (string.Equals(argument.Key, name, StringComparison.OrdinalIgnoreCase))
                    return argument.Value is { Length: > 0 } ? argument.Value : null;

            var at = 0;
            foreach (var argument in this.Arguments)
            {
                if (argument.Key is not null) continue;
                if (at++ != position) continue;

                return argument.Value is { Length: > 0 } ? argument.Value : null;
            }

            return null;
        }

        public string? Said(int position, string name) => this.Part(position, name)?.Text;

        public string? Named(string name)
        {
            foreach (var argument in this.Arguments)
                if (string.Equals(argument.Key, name, StringComparison.OrdinalIgnoreCase))
                    return argument.Value is { Length: > 0 } said ? said.Text : null;

            return null;
        }

        /// <summary>A flag argument: absent means yes, and only <c>false</c> or <c>0</c> means no.</summary>
        public bool Flag(int position, string name)
        {
            if (this.Said(position, name) is not { } said) return true;

            return !said.Equals("false", StringComparison.OrdinalIgnoreCase) && said != "0";
        }
    }

    private sealed record Argument(string? Key, ContentPart? Value);

    /// <summary>The colours and the outline a style asks for; null anywhere means the theme decides.</summary>
    private sealed record Paint
    {
        public string? Fill { get; init; }
        public string? Ink { get; init; }
        public string? Border { get; init; }
        public string? Shape { get; init; }
        public string? Says { get; init; }

        public Paint Over(Paint over) => new()
        {
            Fill = over.Fill ?? this.Fill,
            Ink = over.Ink ?? this.Ink,
            Border = over.Border ?? this.Border,
            Shape = over.Shape ?? this.Shape,
            Says = over.Says ?? this.Says,
        };
    }

    /// <summary>And what a relationship's line is drawn with.</summary>
    private sealed record Stroke
    {
        public string? Said { get; init; }
        public string? Ink { get; init; }
        public bool Dotted { get; init; }
        public string? Says { get; init; }
    }

    /// <summary>What the whole block says about itself, which any line of it may say.</summary>
    private sealed class Told
    {
        public bool Legended { get; set; }
        public bool Hidden { get; set; }
        public bool Described { get; set; }
        public bool Numbered { get; set; }
        public bool FootBoxes { get; set; } = true;

        public List<SequenceLegend> Legend { get; } = [];
        public Dictionary<string, Paint> Styles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Paint> Tags { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Stroke> RelTags { get; } = new(StringComparer.Ordinal);
        public Dictionary<(string From, string To), Stroke> RelStyles { get; } = [];
    }

    /// <summary>
    /// The running number behind C4-PlantUML's numbering: <c>Index()</c> takes the next, <c>LastIndex()</c> repeats the one
    /// just given, and <c>SetIndex(n)</c> and <c>increment(n)</c> move it without drawing anything.
    /// </summary>
    private sealed class Counter
    {
        private int _next = 1;

        public int Last { get; private set; }

        public int Next(int offset = 1)
        {
            this.Last = this._next;
            this._next += Math.Max(1, offset);

            return this.Last;
        }

        public void Set(int value) => this._next = value;

        public void Increment(int offset) => this._next += offset;

        /// <summary>What an <c>$index</c> comes to, or null where none is written.</summary>
        public int? Resolve(string? said)
        {
            if (said is not { Length: > 0 }) return null;

            var expression = said.Trim();

            if (expression.StartsWith("LastIndex", StringComparison.OrdinalIgnoreCase))
                return this.Last > 0 ? this.Last : this.Next();

            if (expression.StartsWith("SetIndex", StringComparison.OrdinalIgnoreCase))
            {
                if (Inside(expression) is { } at) this.Set(at);
                return this.Next();
            }

            if (expression.StartsWith("Index", StringComparison.OrdinalIgnoreCase))
                return this.Next(Inside(expression) ?? 1);

            return Number(expression);
        }

        /// <summary>The number between a call's brackets — the <c>2</c> of <c>Index(2)</c>.</summary>
        private static int? Inside(string call)
        {
            var open = call.IndexOf('(');
            var close = call.LastIndexOf(')');

            return open < 0 || close <= open ? null : Number(call[(open + 1)..close].Trim());
        }
    }
}
