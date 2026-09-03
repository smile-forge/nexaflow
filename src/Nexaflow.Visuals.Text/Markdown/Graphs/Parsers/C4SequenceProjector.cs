using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Projects a <c>C4Sequence</c> diagram onto the shared <see cref="SequenceDiagram"/> model, so it
/// is drawn by the same renderer as a native <c>sequenceDiagram</c> — element cards stand in for
/// participant boxes, a <c>Boundary</c> becomes the box grouping, and a <c>Rel</c> becomes a message
/// carrying its technology and index.
///
/// The statement stream is walked in order rather than the flat collections, because a sequence
/// diagram *is* its order, and because a boundary's extent is the span between where it opened and
/// where it closed.
///
/// Lines the C4 reader did not claim are handed to <see cref="MermaidSequenceParser.ParseLine"/>,
/// so <c>alt</c>, <c>loop</c>, <c>note over</c> and <c>activate</c> work inside a C4 sequence
/// through the native grammar itself rather than a second implementation of it.
/// </summary>
public static class C4SequenceProjector
{
    public static SequenceDiagram ToSequence(C4Diagram d)
    {
        var diagram = new SequenceDiagram
        {
            Title = d.Title,
            ShowFootBoxes = d.ShowFootBoxes,
        };

        var state = new SequenceParseState();
        var openBoxes = new Stack<SequenceBox>();
        int fallbackIndex = 0;

        foreach (var statement in d.Statements)
        {
            switch (statement)
            {
                case C4ElementStatement element:
                    AddParticipant(diagram, d, element.Element, openBoxes);
                    break;

                case C4BoundaryBegin begin:
                {
                    var box = new SequenceBox { Label = BoxLabel(begin.Boundary) };
                    foreach (var tag in begin.Boundary.Tags)
                        if (d.Tags.TryGetValue(tag, out var style) && style.BgColor is { Length: > 0 } bg)
                            box.Color ??= bg;
                    diagram.Boxes.Add(box);
                    openBoxes.Push(box);
                    break;
                }

                case C4BoundaryEnd:
                    if (openBoxes.Count > 0) openBoxes.Pop();
                    break;

                case C4RelStatement rel:
                    AddMessage(diagram, d, rel.Relationship, ref fallbackIndex);
                    break;

                case C4RawLine raw:
                    // The native grammar, on the same diagram, with the same running state.
                    MermaidSequenceParser.ParseLine(raw.Line, diagram, state);
                    break;
            }
        }

        return diagram;
    }

    // ── Participants ─────────────────────────────────────────────────────────

    private static void AddParticipant(SequenceDiagram diagram, C4Diagram d, C4Element element, Stack<SequenceBox> openBoxes)
    {
        var participant = diagram.GetOrAdd(element.Alias, element.Label);

        var card = new C4ElementInfo
        {
            Kind = element.Kind,
            Shape = ShapeFor(element, d.PersonStyle),
            External = element.External,
            Technology = element.Technology,
            // C4-PlantUML's sequence hides descriptions unless asked: a lifeline head is a column
            // header, and a paragraph in every column pushes the columns apart for no gain.
            Description = d.ShowElementDescriptions ? element.Description : null,
            HideStereotype = d.HideStereotype,
        };
        card.Tags.AddRange(element.Tags);

        var style = new C4Style();
        foreach (var tag in element.Tags)
            if (d.Tags.TryGetValue(tag, out var byTag)) style = style.Merge(byTag);
        if (d.ElementStyles.TryGetValue(element.Alias, out var byAlias)) style = style.Merge(byAlias);

        card.FillColor = style.BgColor;
        card.FontColor = style.FontColor;
        card.BorderColor = style.BorderColor;

        participant.Card = card;

        if (openBoxes.Count > 0) openBoxes.Peek().ParticipantIds.Add(element.Alias);
    }

    private static C4ElementShape ShapeFor(C4Element element, C4PersonStyle personStyle)
    {
        if (element.Kind != C4ElementKind.Person || element.Shape != C4ElementShape.Box)
            return element.Shape;

        return personStyle switch
        {
            C4PersonStyle.Outline  => C4ElementShape.PersonOutline,
            C4PersonStyle.Portrait => C4ElementShape.PersonPortrait,
            _                      => C4ElementShape.Person,
        };
    }

    private static string BoxLabel(C4Boundary boundary) =>
        boundary.Type is { Length: > 0 } type ? $"{boundary.Label} [{type}]" : boundary.Label;

    // ── Messages ─────────────────────────────────────────────────────────────

    private static void AddMessage(SequenceDiagram diagram, C4Diagram d, C4Relationship rel, ref int fallbackIndex)
    {
        // Rel_Back points the other way; the message is simply built reversed.
        string from = rel.Back ? rel.To : rel.From;
        string to   = rel.Back ? rel.From : rel.To;

        diagram.GetOrAdd(from);
        diagram.GetOrAdd(to);

        string text = rel.Label;
        if (rel.Description is { Length: > 0 } descr && descr != text)
            text = text.Length > 0 ? $"{text}\n{descr}" : descr;

        diagram.Items.Add(new SequenceMessage
        {
            FromId = from,
            ToId = to,
            Text = text,
            Technology = rel.Technology,
            Line = SequenceLineStyle.Solid,
            Head = SequenceArrowHead.Filled,
            Bidirectional = rel.Bidirectional,
            // An explicit $index wins; otherwise the diagram counts its own messages, so
            // SHOW_INDEX() alone is enough to number a sequence.
            Number = d.ShowIndex ? rel.Index ?? ++fallbackIndex : null,
        });

        if (d.ShowIndex && rel.Index is int explicitIndex) fallbackIndex = explicitIndex;
    }
}
