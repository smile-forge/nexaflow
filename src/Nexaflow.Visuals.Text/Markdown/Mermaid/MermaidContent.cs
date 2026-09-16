using System;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// A diagram as a kind of content: how its source is laid out, and what ending a line or taking back a character means
/// in it.
///
/// <para>
/// A diagram is lines — a slice, a node, an edge to a line — so Enter starts another one under the line the caret is on,
/// written as the diagram's grammar starts one after that line and with the caret in it: a pie's next slice, its label
/// and its value still to fill in; a Venn diagram's next item under an item or a set, and its next set anywhere else. Never through the middle of what the caret is on, which would leave half a label on each line. A
/// diagram with no shape of line to start from, or one shown as the characters it was written with, is text for as long
/// as it is, and Enter starts a line in it indented like the one it was pressed on.
/// </para>
/// <para>
/// Backspace and delete take the characters of what is written and never what holds it together. Past the end of a
/// label, a value or a title is a quote, a colon or the end of the line, so the key does nothing there, and a hole has
/// nothing in it to take — so holding the key down empties one thing and stops. A line with nothing yet written on it is
/// the exception, since that is Enter pressed once too often, and is taken back whole.
/// </para>
/// </summary>
/// <param name="lay">
/// Lays the state out at a width and a pixel density, told whether anybody can write in it — the builder, as the element
/// asks for it.
/// </param>
internal sealed class MermaidContent(Func<EditState, double, double, bool, Laid> lay) : IContent
{
    /// <summary>How a first line under the header is indented when there is no line yet to follow — as Mermaid's examples are.</summary>
    private const string FirstIndent = "    ";

    /// <inheritdoc/>
    public Laid Lay(EditState state, double room, double pixelsPerDip, bool readOnly) => lay(state, room, pixelsPerDip, readOnly);

    /// <inheritdoc/>
    /// <remarks>
    /// What a place being written in cannot hold as itself is escaped as the diagram's grammar says — a name put in quotes, a
    /// quote written as its entity code — so a character typed never stops a line reading. What an entity code went into is
    /// shown as written for as long as the caret is in it, so the caret has the characters it stands between.
    /// </remarks>
    public EditState? Typing(Landing landing, string text)
    {
        var state = landing.State;
        if (state.HasSelection || text.Length == 0) return null;

        var caret = state.Caret;
        var part = WrittenIn(landing, caret);

        // What is shown as written stays shown as written while it is written in — typed at its end, it grows to hold what is
        // typed, or the words would turn back into what they read as under the caret.
        if (part is null || MermaidDiagrams.Grammar(MermaidBlock.Read(state.Source).Diagram)?.Escaping(part, caret, text) is not { } writing)
            return state.Raw is { } shown && shown.Holds(caret) ? state.Write(text, shown with { End = shown.End + text.Length }) : null;

        var source = state.Source[..writing.Start] + writing.Text + state.Source[writing.End..];
        var grown = writing.Text.Length - (writing.End - writing.Start);

        // Where what was written in now stands: the part typed into, or the stretch written over less the quotes put round it.
        var (start, end) = writing.Start == writing.End
            ? (part.Start, part.End + grown)
            : (writing.Start + 1, writing.Start + writing.Text.Length - 1);

        RawZone? raw = state.Raw is { } zone && zone.Holds(caret) ? new RawZone(zone.Start, zone.End + grown)
                     : MermaidText.Decode(source[start..end]) != source[start..end] ? new RawZone(start, end)
                     : null;

        return new EditState(source, writing.Caret, Raw: raw);
    }

    /// <inheritdoc/>
    public EditState Settle(Landing landing, string separator)
    {
        var state = landing.State;
        if (separator != "\n") return Typing(landing, separator) ?? state.Write(separator);

        if (Started(landing) is { } started) return started;

        var line = Line(state.Source, Math.Clamp(state.Caret, 0, state.Source.Length));
        return state.Write(Newline(state.Source) + Leading(state.Source, line.Start, line.End));
    }

    /// <inheritdoc/>
    public EditState? Erasing(Landing landing, bool forward)
    {
        var state = landing.State;
        if (state.HasSelection) return null;

        var caret = state.Caret;

        // A stretch shown as its characters is text to its ends, and no further.
        if (state.Raw is { } raw)
            return raw.Holds(caret) && caret == (forward ? raw.End : raw.Start) ? state : null;

        if (landing.Laid.Holes.Any(hole => hole.Sits().Start == caret))
        {
            var source = state.Source;
            var line = Line(source, caret);

            if (line.Start > 0 && Blank(MermaidBlock.Read(source), Line(source, line.Start - 1).Start) is { } blank
                && source[line.Start..line.End].Trim() == blank.Text.Trim())
            {
                var from = line.Start > 1 && source[line.Start - 2] == '\r' ? line.Start - 2 : line.Start - 1;
                return state.Remove(from, line.End - from);
            }

            return state;
        }

        // At the edge of something written, what is past the edge is not part of it.
        return landing.Laid.Root.WordsAt(caret) is { Part: { } part } && caret == (forward ? part.End() : part.Start)
            ? state
            : null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A name renamed where it is declared — a set's, an item's — is renamed wherever the diagram uses it, written as its
    /// grammar writes a name there, so a union or a style goes on naming what it named. Only a name declared once, and only
    /// onto one nothing else is declared as: two sets of one name, or a rename onto another's, are left as they are rather
    /// than guessed at.
    /// </remarks>
    public EditState Edited(EditState before, EditState after)
    {
        var now = MermaidBlock.Read(after.Source);
        if (MermaidDiagrams.Grammar(now.Diagram) is not { } grammar) return after;

        var (start, end, written) = Changed(before.Source, after.Source);

        var was = grammar.Names(MermaidBlock.Read(before.Source).Reading.Root);
        if (was.Where(name => name.Declared.Start <= start && end <= name.Declared.End).ToList() is not [var renamed]
            || renamed.Uses.Count == 0
            || was.Count(name => name.Name == renamed.Name) != 1)
            return after;

        var names = grammar.Names(now.Reading.Root);
        if (names.Where(name => name.Declared.Start <= start && written <= name.Declared.End).ToList() is not [var renaming]
            || renaming.Name.Length == 0
            || renaming.Name == renamed.Name
            || names.Count(name => name.Name == renaming.Name) != 1)
            return after;

        var naming = grammar.Naming(renaming.Name);
        var (source, caret, raw) = (after.Source, after.Caret, after.Raw);

        // Last first, so every use still stands where it did; one past the edit has moved by what the edit wrote.
        foreach (var use in renamed.Uses.OrderByDescending(use => use.Start))
        {
            var at = use.Start >= end ? use.Start + (written - end) : use.Start;
            source = source[..at] + naming + source[(at + use.Length)..];

            var by = naming.Length - use.Length;
            if (at + use.Length <= caret) caret += by;
            if (raw is { } zone && at + use.Length <= zone.Start) raw = new RawZone(zone.Start + by, zone.End + by);
        }

        return after with { Source = source, Caret = caret, Raw = raw, Selected = null };
    }

    /// <summary>The stretch of <paramref name="before"/> an edit wrote over, and where what it wrote ends in <paramref name="after"/>.</summary>
    private static (int Start, int End, int Written) Changed(string before, string after)
    {
        var shorter = Math.Min(before.Length, after.Length);

        var start = 0;
        while (start < shorter && before[start] == after[start]) start++;

        var same = 0;
        while (same < shorter - start && before[before.Length - 1 - same] == after[after.Length - 1 - same]) same++;

        return (start, before.Length - same, after.Length - same);
    }

    /// <summary>
    /// A new line under the one the caret is on, as the diagram's grammar starts one, with the caret where it says — or
    /// null where there is no shape to start from, or the diagram is being shown as its characters.
    /// </summary>
    private static EditState? Started(Landing landing)
    {
        if (landing.Laid.Root.SelfAndDescendants().Any(piece => piece.Kind == LayoutText.SourceKind)) return null;

        var state = landing.State;
        var source = state.Source;
        var block = MermaidBlock.Read(source);
        if (block.Header is not { } header) return null;

        // On the header, or above it in the front matter, the first line goes straight under the header: anywhere earlier
        // it would be written into the front matter.
        var caret = Math.Clamp(state.Caret, 0, source.Length);
        var on = Line(source, Math.Max(caret, header.End));
        if (Blank(block, on.Start) is not { } blank) return null;
        var indent = on.Start > header.Start ? Leading(source, on.Start, on.End) : Following(source, on.Next);

        var newline = Newline(source);
        return (state with { Raw = null })
            .MoveCaretTo(on.End)
            .Write(newline + indent + blank.Text)
            .MoveCaretTo(on.End + newline.Length + indent.Length + blank.Caret);
    }

    /// <summary>What the caret is writing in: a hole it stands in, or a run of words drawn as they were written — or null.</summary>
    private static ContentPart? WrittenIn(Landing landing, int caret) =>
        landing.Laid.Holes.FirstOrDefault(hole => hole.Sits().Start == caret) is { Exists: true, Part: ContentPart hole }
            ? hole
            : landing.Laid.Root.WordsAt(caret).Part as ContentPart;

    /// <summary>
    /// How the diagram's grammar starts a line under the one starting at <paramref name="line"/> — told what that line says —
    /// where it has a grammar that can.
    /// </summary>
    private static (string Text, int Caret)? Blank(MermaidBlock block, int line) =>
        MermaidDiagrams.Grammar(block.Diagram)?.Blank(
            block.Reading.Root.SelfAndDescendants()
                .FirstOrDefault(part => part.Kind == MermaidKinds.Line && part.Start == line)?.Children
                .FirstOrDefault(child => child.Kind != Kinds.Space)?.Node);

    /// <summary>
    /// The line <paramref name="offset"/> is on: where it starts, where its characters stop short of the line ending, and
    /// where the line after it starts.
    /// </summary>
    private static (int Start, int End, int Next) Line(string source, int offset)
    {
        var start = offset == 0 ? 0 : source.LastIndexOf('\n', offset - 1) + 1;
        var ending = source.IndexOf('\n', offset);
        if (ending < 0) return (start, source.Length, source.Length);

        return (start, ending > start && source[ending - 1] == '\r' ? ending - 1 : ending, ending + 1);
    }

    /// <summary>The space a line begins with.</summary>
    private static string Leading(string source, int start, int end)
    {
        var at = start;
        while (at < end && source[at] is ' ' or '\t') at++;
        return source[start..at];
    }

    /// <summary>How the first line from <paramref name="from"/> with anything on it is indented, or <see cref="FirstIndent"/> where none has.</summary>
    private static string Following(string source, int from)
    {
        for (var at = from; at < source.Length;)
        {
            var line = Line(source, at);
            if (!string.IsNullOrWhiteSpace(source[line.Start..line.End])) return Leading(source, line.Start, line.End);
            if (line.Next <= at) break;
            at = line.Next;
        }

        return FirstIndent;
    }

    /// <summary>The line ending the source is written with, so a line added to it ends the same way.</summary>
    private static string Newline(string source) => source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
