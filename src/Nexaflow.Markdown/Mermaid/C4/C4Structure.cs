using System.Globalization;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>One element: a card saying what it is, what it is built with and what it does.</summary>
public sealed record C4Node(ContentPart Part, string Id, int Order)
{
    /// <summary>What is written across the top of it — its label, or its own name where it was given none.</summary>
    public ContentPart? Said { get; init; }

    /// <summary>The line in brackets under that, worked out from what it is rather than written as one run.</summary>
    public string Stereotype { get; init; } = string.Empty;

    /// <summary>What it is built with, which the stereotype says as well — kept so a press on it means what was written.</summary>
    public ContentPart? Technology { get; init; }

    /// <summary>The sentence under it.</summary>
    public ContentPart? Describes { get; init; }

    public C4Level Level { get; init; }

    public C4Shape Shape { get; init; }

    public bool External { get; init; }

    /// <summary>Which band of C4's grading it takes — see <see cref="C4Elements.Banded"/>.</summary>
    public int Tone { get; init; }

    /// <summary>What it is filled, written in and outlined with, where anything says — otherwise the theme's own.</summary>
    public string? Fill { get; init; }
    public string? Ink { get; init; }
    public string? Border { get; init; }

    /// <summary>The boundary it was written inside, or null for one written outside them all.</summary>
    public string? Box { get; init; }

    /// <summary>Where a <c>$link</c> leads, where one was written.</summary>
    public string? Href { get; init; }
}

/// <summary>
/// A boundary or a deployment node — one type, because the two are the same thing: a named box holding elements and other
/// boundaries. They differ in how they draw, which is what <see cref="Physical"/> says.
/// </summary>
public sealed record C4Bound(ContentPart Part, string Key, int Order)
{
    /// <summary>The name it was given, which an <c>UpdateElementStyle</c> may colour it by.</summary>
    public string? Alias { get; init; }

    public ContentPart? Said { get; init; }

    /// <summary>The line in brackets under its name — its <c>$type</c>, or what a deployment node runs on.</summary>
    public string? Says { get; init; }

    /// <summary>The boundary this one was written inside, or null for one written outside them all.</summary>
    public string? Parent { get; init; }

    /// <summary>Whether it is a deployment node, which is a real box and so is drawn solid rather than dashed.</summary>
    public bool Physical { get; init; }

    public string? Fill { get; init; }
    public string? Ink { get; init; }
    public string? Border { get; init; }

    /// <summary>Where a <c>$link</c> leads, where one was written.</summary>
    public string? Href { get; init; }

    /// <summary>Everything from the line that opened it through the line that closed it, which is what a press on it means.</summary>
    public SourceSpan Whole { get; init; }
}

/// <summary>A relationship: a line from one element to another, carrying what it is done with and what it is for.</summary>
public sealed record C4Link(ContentPart Part, string From, string To, int Order)
{
    public ContentPart? Said { get; init; }

    /// <summary>What is written under the label in smaller type — what it is done with, and what it is for.</summary>
    public IReadOnlyList<ContentPart> Under { get; init; } = [];

    /// <summary>A <c>BiRel</c>, which draws a head at each end.</summary>
    public bool Both { get; init; }

    public bool Dotted { get; init; }

    public string? Ink { get; init; }
    public string? SaidInk { get; init; }

    /// <summary>Its number where the diagram counts its relationships, and null where it does not.</summary>
    public string? Number { get; init; }
}

/// <summary>
/// A <c>C4Context</c>, <c>C4Container</c>, <c>C4Component</c>, <c>C4Dynamic</c> or <c>C4Deployment</c> block, read: the
/// elements, the boundaries holding them, and the relationships between them.
///
/// <para>
/// The five are one model because they are one language: what differs between them is which macros a reader is likely to
/// write, not what any of them means. <c>C4Dynamic</c> numbers its relationships without being asked, which is the only
/// thing the keyword itself decides.
/// </para>
///
/// <para>
/// <strong>This is the logical shape, not the drawn one.</strong> A boundary holds the elements written inside it by name,
/// and every relationship stands beside them in the order it was written; where each of them lands on the page is
/// <c>C4Builder</c>'s, which lays them out as the graph they are.
/// </para>
/// </summary>
public sealed class C4Structure
{
    private C4Structure(MermaidBlock block, C4Metrics config, IReadOnlyList<C4Node> nodes, IReadOnlyList<C4Bound> boxes,
                        IReadOnlyList<C4Link> links, IReadOnlyList<C4Key> legend, C4Way way)
    {
        this.Block = block;
        this.Config = config;
        this.Nodes = nodes;
        this.Boxes = boxes;
        this.Links = links;
        this.Legend = legend;
        this.Way = way;
    }

    /// <summary>Reads a block: parsed, then worked over by its stages (<see cref="MermaidParser.Read"/>).</summary>
    public static C4Structure Read(string? block) => Of(MermaidParser.Read(block));

    /// <summary>
    /// The same diagram to different measures — what one too wide for the room it is given is laid out again as. Nothing is
    /// read again: it is the same nodes, boundaries and relationships, drawn smaller.
    /// </summary>
    public C4Structure Sized(C4Metrics config) =>
        new(this.Block, config, this.Nodes, this.Boxes, this.Links, this.Legend, this.Way);

    /// <summary>Reads a tree the stages have already been over.</summary>
    public static C4Structure Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    public static C4Structure Of(MermaidBlock block)
    {
        var said = C4Said.Read(block);
        var counter = new C4Counter();
        var numbered = said.Numbered || Dynamic(block);

        var nodes = new List<C4Node>();
        var boxes = new List<C4Bound>();
        var links = new List<C4Link>();
        var open = new Stack<C4Bound>();

        foreach (var line in block.Reading.Root.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Line))
        {
            if (line.Stated() is not { } stated) continue;

            var inside = stated.Fact(C4Roles.Inside) is { Length: > 0 } holder ? holder : null;

            switch (stated.Kind)
            {
                case C4Kinds.Boundary:
                    if (Bounded(stated, said, inside, boxes.Count) is { } bound)
                    {
                        boxes.Add(bound);
                        open.Push(bound);
                    }

                    break;

                // The line closing a boundary is where the whole of it stops, which is what a press on the box means.
                case C4Kinds.Ends when open.Count > 0:
                    var shut = open.Pop();
                    boxes[boxes.IndexOf(shut)] = shut with
                    {
                        Whole = new SourceSpan(shut.Part.Start, Math.Max(0, stated.End() - shut.Part.Start)),
                    };

                    break;

                case C4Kinds.Macro:
                    Macroed(stated, said, counter, numbered, inside, nodes, links);
                    break;
            }
        }

        // An end that nobody wrote leaves its boundary reaching to the end of what it holds, rather than to nothing at all.
        foreach (var never in open)
            boxes[boxes.IndexOf(never)] = never with
            {
                Whole = new SourceSpan(never.Part.Start, Math.Max(0, never.Part.End() - never.Part.Start)),
            };

        // A relationship may name something nobody declared, which still has to stand somewhere or the line would vanish.
        foreach (var link in links)
            foreach (var end in new[] { link.From, link.To })
                if (!nodes.Any(node => string.Equals(node.Id, end, StringComparison.Ordinal)))
                    nodes.Add(new C4Node(link.Part, end, nodes.Count) { Tone = C4Elements.Banded(C4Level.System, false) });

        return new C4Structure(block, C4Config.Laid(block.Config), nodes, boxes, links, said.Legend,
                               said.Way ?? C4Way.Down);
    }

    /// <summary>The block this was read from — its front matter, its header, its title, everything written in it.</summary>
    public MermaidBlock Block { get; }

    public C4Metrics Config { get; }

    /// <summary>Which way the diagram runs, which a <c>LAYOUT_LEFT_RIGHT()</c> turns.</summary>
    public C4Way Way { get; }

    public IReadOnlyList<C4Node> Nodes { get; }

    public IReadOnlyList<C4Bound> Boxes { get; }

    public IReadOnlyList<C4Link> Links { get; }

    /// <summary>The rows of the key, where <c>SHOW_LEGEND()</c> asked for one.</summary>
    public IReadOnlyList<C4Key> Legend { get; }

    /// <summary>The boundaries written directly inside one, in the order they were written.</summary>
    public IEnumerable<C4Bound> Within(string? key) =>
        this.Boxes.Where(box => string.Equals(box.Parent, key, StringComparison.Ordinal));

    /// <summary>And the elements written directly inside one.</summary>
    public IEnumerable<C4Node> Inside(string? key) =>
        this.Nodes.Where(node => string.Equals(node.Box, key, StringComparison.Ordinal));

    /// <summary>The element that name was given to, or null.</summary>
    public C4Node? Find(string id) => this.Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal));

    /// <summary>Whether the block declared nothing worth drawing.</summary>
    public bool Empty => this.Nodes.Count == 0 && this.Boxes.Count == 0;

    /// <summary>A <c>C4Dynamic</c> numbers its relationships without being asked; the other four do not.</summary>
    private static bool Dynamic(MermaidBlock block) =>
        string.Equals(block.Keyword?.Text, "C4Dynamic", StringComparison.OrdinalIgnoreCase);

    private static void Macroed(ContentPart stated, C4Said said, C4Counter counter, bool numbered, string? inside,
                                List<C4Node> nodes, List<C4Link> links)
    {
        var macro = C4Macro.Of(stated);
        var word = macro.Name.ToLowerInvariant();

        if (C4Grammar.Elemental(macro.Name))
        {
            if (Standing(stated, macro, said, inside, nodes.Count) is { } node) nodes.Add(node);
            return;
        }

        if (word.StartsWith("relindex", StringComparison.Ordinal)) Related(stated, macro, said, counter, numbered, links, 1, false, false);
        else if (word.StartsWith("rel_back", StringComparison.Ordinal)) Related(stated, macro, said, counter, numbered, links, 0, true, false);
        else if (word.StartsWith("birel", StringComparison.Ordinal)) Related(stated, macro, said, counter, numbered, links, 0, false, true);
        else if (word.StartsWith("rel", StringComparison.Ordinal)) Related(stated, macro, said, counter, numbered, links, 0, false, false);
        else if (word == "increment") counter.Increment(C4Macro.Number(macro.Said(0, "offset")) ?? 1);
        else if (word == "setindex" && C4Macro.Number(macro.Said(0, "new_index")) is { } at) counter.Set(at);
    }

    private static C4Node? Standing(ContentPart stated, C4Macro macro, C4Said said, string? inside, int order)
    {
        if (macro.Part(0, "alias") is not { Length: > 0 } alias) return null;

        var (level, shape, external) = C4Elements.Sorted(macro.Name);

        // A Person and a System take (alias, label, descr); a Container and a Component put what they are built with at 2
        // and push the description to 3. That asymmetry is C4-PlantUML's.
        var built = level is C4Level.Container or C4Level.Component;
        var technology = macro.Part(built ? 2 : -1, "techn");
        var tags = C4Macro.Tagged(macro.Said(built ? 5 : 4, "tags"));
        var style = said.Painted(level, shape, external, alias.Text, tags);

        return new C4Node(stated, alias.Text, order)
        {
            Said = macro.Part(1, "label") ?? alias,
            Stereotype = C4Elements.Stereotyped(level, external, technology?.Text, macro.Named("type"), said.Hidden),
            Technology = technology,
            Describes = macro.Part(built ? 3 : 2, "descr"),
            Level = level,
            Shape = C4Elements.Shaped(shape, style.Shape),
            External = external,
            Tone = C4Elements.Banded(level, external),
            Fill = style.Fill,
            Ink = style.Ink,
            Border = style.Border,
            Box = inside,
            Href = macro.Named("link"),
        };
    }

    private static C4Bound? Bounded(ContentPart stated, C4Said said, string? inside, int order)
    {
        var macro = C4Macro.Of(stated);
        if (macro.Part(0, "alias") is not { Length: > 0 } alias) return null;

        var word = macro.Name.ToLowerInvariant();
        var physical = word is "deployment_node" or "node" or "node_l" or "node_r";

        var type = word switch
        {
            "enterprise_boundary" => "Enterprise",
            "system_boundary" => "System",
            "container_boundary" => "Container",
            _ => macro.Said(2, "type"),
        };

        var style = said.Bounded(macro, alias.Text);

        return new C4Bound(stated, stated.Fact(C4Roles.Opened) ?? " " + stated.Start, order)
        {
            Alias = alias.Text,
            Said = macro.Part(1, "label") ?? alias,
            Says = physical
                ? type is { Length: > 0 } runs ? $"[Deployment Node: {runs}]" : "[Deployment Node]"
                : type is { Length: > 0 } named ? $"[{named}]" : null,
            Parent = inside,
            Physical = physical,
            Fill = style.Fill,
            Ink = style.Ink,
            Border = style.Border,
            Href = macro.Named("link"),
            Whole = new SourceSpan(stated.Start, Math.Max(0, stated.End() - stated.Start)),
        };
    }

    private static void Related(ContentPart stated, C4Macro macro, C4Said said, C4Counter counter, bool numbered,
                                List<C4Link> links, int offset, bool back, bool both)
    {
        if (macro.Part(offset, "from") is not { Length: > 0 } one) return;
        if (macro.Part(offset + 1, "to") is not { Length: > 0 } other) return;

        // Rel_Back declares from→to and points the other way, so the line is simply built reversed.
        var style = said.Relating(one.Text, other.Text, C4Macro.Tagged(macro.Said(offset + 6, "tags")));
        var number = offset == 1 ? counter.Resolve(macro.Said(0, "index")) : counter.Resolve(macro.Named("index"));

        links.Add(new C4Link(stated, back ? other.Text : one.Text, back ? one.Text : other.Text, links.Count)
        {
            Said = macro.Part(offset + 2, "label"),
            Under = [.. new[] { macro.Part(offset + 3, "techn"), macro.Part(offset + 4, "descr") }.OfType<ContentPart>()],
            Both = both,
            Dotted = style.Dotted,
            Ink = style.Ink,
            SaidInk = style.Said,
            Number = numbered ? (number ?? counter.Next()).ToString(CultureInfo.InvariantCulture) : null,
        });
    }
}
