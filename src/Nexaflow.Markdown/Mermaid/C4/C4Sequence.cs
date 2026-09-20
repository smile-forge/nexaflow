using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Sequence;

namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>
/// A <c>C4Sequence</c> block, read into the very same <see cref="SequenceDiagram"/> a <c>sequenceDiagram</c> is read into.
/// It says what a sequence says in C4-PlantUML's words: an element macro is a participant whose box is a card, a
/// <c>Boundary</c> is the box grouping the lifelines it holds, and a <c>Rel</c> is a message carrying what it is done with.
///
/// <para>
/// <strong>Only what a macro means to a timeline is here.</strong> What a macro <em>is</em> — its arguments, what its name
/// says the element is, what everything is styled with and how the numbering counts — is C4's own
/// (<see cref="C4Macro"/>, <see cref="C4Elements"/>, <see cref="C4Said"/>, <see cref="C4Counter"/>), shared with the
/// structural diagrams that say the same things about a graph.
/// </para>
///
/// <para>
/// <strong>Everything that is not a macro is the sequence diagram's own.</strong> <c>alt</c>, <c>loop</c>,
/// <c>note over</c> and <c>activate</c> fall through to
/// <see cref="SequenceDiagram.Read(MermaidBlock, SequenceConfig, Func{SequenceReading, ContentPart, string, bool}, IReadOnlyList{SequenceLegend})"/>,
/// so the two nest round each other correctly without a second copy of how a sequence is read.
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
        var said = C4Said.Read(block);
        var counter = new C4Counter();

        var config = C4Config.Read(block.Config) with { Mirrored = said.FootBoxes };

        return SequenceDiagram.Read(block, config, (read, stated, inside) => Claimed(read, stated, inside, said, counter),
                                    [.. said.Legend.Select(key => new SequenceLegend(key.Says, key.Fill, key.Border)
                                    {
                                        Tone = key.Tone,
                                    })]);
    }

    /// <summary>Whether this line is one of C4's, and what it puts on the timeline where it is.</summary>
    private static bool Claimed(SequenceReading read, ContentPart stated, string? inside, C4Said said, C4Counter counter)
    {
        switch (stated.Kind)
        {
            case C4Kinds.Aside:
                return true;

            // A } closes a boundary where an end closes a frame, and one stack holds both — so C4's own closing word is
            // C4's to act on, and the sequence diagram never has to know a second word for the same thing.
            case C4Kinds.Ends:
                if (read.Open.Count > 0) read.Shuts(stated);
                return true;

            case C4Kinds.Boundary:
                var boundary = C4Macro.Of(stated);
                read.Opens(stated, boundary.Part(1, "label") ?? boundary.Part(0, "alias"),
                           said.Bounded(boundary, boundary.Said(0, "alias")).Fill, inside);

                return true;

            case C4Kinds.Macro:
                Macroed(read, stated, inside, said, counter, C4Macro.Of(stated));
                return true;

            default:
                return false;
        }
    }

    private static void Macroed(SequenceReading read, ContentPart stated, string? inside, C4Said said, C4Counter counter,
                                C4Macro macro)
    {
        var word = macro.Name.ToLowerInvariant();

        if (C4Grammar.Elemental(macro.Name))
        {
            Standing(read, inside, said, macro);
            return;
        }

        if (word.StartsWith("relindex", StringComparison.Ordinal)) Related(read, stated, said, counter, macro, 1, false, false);
        else if (word.StartsWith("rel_back", StringComparison.Ordinal)) Related(read, stated, said, counter, macro, 0, true, false);
        else if (word.StartsWith("birel", StringComparison.Ordinal)) Related(read, stated, said, counter, macro, 0, false, true);
        else if (word.StartsWith("rel", StringComparison.Ordinal)) Related(read, stated, said, counter, macro, 0, false, false);
        else if (word == "increment") counter.Increment(C4Macro.Number(macro.Said(0, "offset")) ?? 1);
        else if (word == "setindex" && C4Macro.Number(macro.Said(0, "new_index")) is { } at) counter.Set(at);
    }

    /// <summary>An element: a participant whose box is a card saying what it is and what it does.</summary>
    private static void Standing(SequenceReading read, string? inside, C4Said said, C4Macro macro)
    {
        if (macro.Part(0, "alias") is not { Length: > 0 } alias) return;

        var (level, shape, external) = C4Elements.Sorted(macro.Name);

        // A Person and a System take (alias, label, descr); a Container and a Component put what they are built with at 2
        // and push the description to 3. That asymmetry is C4-PlantUML's.
        var built = level is C4Level.Container or C4Level.Component;
        var technology = macro.Part(built ? 2 : -1, "techn");
        var described = macro.Part(built ? 3 : 2, "descr");
        var tags = C4Macro.Tagged(macro.Said(built ? 5 : 4, "tags"));

        var one = read.Called(alias, alias.Text);
        var style = said.Painted(level, shape, external, alias.Text, tags);

        one.Said = macro.Part(1, "label") ?? alias;
        one.Card = new SequenceCard
        {
            Stereotype = C4Elements.Stereotyped(level, external, technology?.Text, macro.Named("type"), said.Hidden),
            Technology = technology,
            Said = said.Described ? described : null,
            Shape = Carded(C4Elements.Shaped(shape, style.Shape)),
            Tone = C4Elements.Banded(level, external),
            Fill = style.Fill,
            Ink = style.Ink,
            Border = style.Border,
        };

        if (inside is not null && read.Boxes.Any(box => string.Equals(box.Key, inside, StringComparison.Ordinal)))
            one.Box ??= inside;
    }

    /// <summary>A relationship: a message from one lifeline to another, carrying what it is done with.</summary>
    private static void Related(SequenceReading read, ContentPart stated, C4Said said, C4Counter counter, C4Macro macro,
                                int offset, bool back, bool both)
    {
        if (macro.Part(offset, "from") is not { Length: > 0 } one) return;
        if (macro.Part(offset + 1, "to") is not { Length: > 0 } other) return;

        // Rel_Back points the other way, so the message is simply built reversed.
        var from = read.Called(back ? other : one, (back ? other : one).Text);
        var to = read.Called(back ? one : other, (back ? one : other).Text);

        from.Reached = true;
        to.Reached = true;

        var style = said.Relating(one.Text, other.Text, C4Macro.Tagged(macro.Said(offset + 6, "tags")));
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

    /// <summary>
    /// The outline a card is drawn with. A person is a card with a head above it — the three <c>SHOW_PERSON_*</c> styles are
    /// one shape here, since a lifeline's head is a column heading and the three differ only in how much of a figure they
    /// draw above the same card.
    /// </summary>
    private static SequenceCardShape Carded(C4Shape shape) => shape switch
    {
        C4Shape.Database => SequenceCardShape.Database,
        C4Shape.Queue => SequenceCardShape.Queue,
        C4Shape.Person => SequenceCardShape.Person,
        _ => SequenceCardShape.Box,
    };
}
