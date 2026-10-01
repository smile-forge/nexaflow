using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using System.Windows;
using System.Windows.Media;
using Nexaflow.Icons;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Icons;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Prose;

/// <summary>
/// What an edit means in markdown's own source: the document, wherever it is not another language.
///
/// <para>
/// <strong>What is typed is what is on the page.</strong> The document is written in as a word processor is, and
/// markdown is how it is kept. A reader who types an asterisk means an asterisk, so one typed where markdown would read
/// it as markup is written behind a backslash — the page shows what was typed, and the source still says it. Nothing
/// typed ever turns into formatting.
/// </para>
/// <para>
/// Only a character typed is the reader's own. Text arriving whole — a paste, a palette key, a drop — is markdown
/// already, and is written as it came.
/// </para>
/// <para>
/// <strong>Enter starts what comes next.</strong> In a list it is the next item, marked as this one was; in a quote, the
/// next paragraph of the quote; anywhere else, the next paragraph. An item with nothing written in it ends the list
/// instead, which is Enter pressed twice. A table's cell is one line, so Enter there does nothing.
/// </para>
/// <para>
/// <strong>Backspace at the end of a line shows the line as it was written.</strong> There is machinery on that line
/// that is not on the screen — the hashes of a heading, the marks round a word set heavy, an item's marker — and a key
/// that takes back a character has nothing to take where the character it would take is not drawn. So it shows the
/// characters instead, which is the one thing a reader wanting to change the markup is reaching for. Moving away puts
/// it back. At the start of a paragraph it takes back the gap before it, joining the two.
/// </para>
/// <para>
/// <strong>A right-click offers a block in any language there is one to start in.</strong> Each is drawn as its icon, behind the
/// ribbon's one Insert button, and choosing one writes the block it starts from after the block the ribbon was opened over.
/// </para>
/// </summary>
internal sealed partial class MarkdownEdits : IContentLanguage, IOnEdit
{
    public static MarkdownEdits Instance { get; } = new();

    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => edit.Kind switch
    {
        EditKind.Typing => Typing(edit),
        EditKind.Settling when edit.Text == "\n" => Breaking(edit),
        EditKind.Settling => Typing(edit),
        EditKind.Erasing => Erasing(edit, forward: false),
        EditKind.Deleting => Erasing(edit, forward: true),
        EditKind.Choosing => Started(edit),
        _ => null,
    };

    /// <inheritdoc/>
    public IOnEdit OnEdit => this;

    /// <summary>
    /// A block to start in every language there is one to start in (<see cref="ContentLanguages.Insertable"/>), each drawn as its icon and
    /// named for a reader hovering over it — nothing, where the reader may not write.
    /// </summary>
    public IReadOnlyList<LayoutIntent> Offers(ContentAsk ask) => ask.IsReadOnly ? [] :
    [
        .. ContentLanguages.Insertable.Select(language => new LayoutIntent(Inserts + language.DisplayName, null, language.DisplayName)
        {
            Offer = LayoutOffer.Insert,
            Shape = Drawn(language.Icon),
        }),
    ];

    /// <summary>What every offer to start a block begins its verb with; the language's name follows.</summary>
    private const string Inserts = "insert:";

    /// <summary>
    /// A block started in the language chosen: the block it starts from, written after the block the ribbon was opened over with a blank
    /// line either side, and the caret at the end of its last line, where a reader goes on to write it.
    /// </summary>
    private static ContentChange? Started(ContentEdit edit)
    {
        if (!edit.Text.StartsWith(Inserts, StringComparison.Ordinal)
            || ContentLanguages.Insertable.FirstOrDefault(language => Inserts + language.DisplayName == edit.Text)?.DefaultBlock is not { } starts)
            return null;

        var block = starts.ReplaceLineEndings("\n");
        var source = edit.State.Source;
        var end = Outermost(edit) is { } over ? over.Start + over.Length : edit.State.Caret;

        // At the end of the line the block over ends on, so the gap after it stays after the new one.
        var at = source.IndexOf('\n', Math.Clamp(end - 1, edit.Start, edit.End));
        at = at < 0 || at > edit.End ? edit.End : at;

        var lead = at == edit.Start ? string.Empty : "\n\n";
        var rest = source[at..edit.End];
        var trail = rest.Length == 0 ? "\n" : rest == "\n" || rest.StartsWith("\n\n", StringComparison.Ordinal) ? string.Empty : "\n";

        return ContentChange.Write(at, 0, lead + block + trail, caret: at + lead.Length + block.LastIndexOf("\n```", StringComparison.Ordinal));
    }

    /// <summary>The block of the document the edit landed in — the part it names, or what holds it, just under the root — or null for none.</summary>
    private static ContentPart? Outermost(ContentEdit edit)
    {
        for (var part = edit.Part; part?.Parent is { } parent; part = parent)
            if (ReferenceEquals(parent, edit.Root)) return part;

        return null;
    }

    /// <summary>
    /// <paramref name="icon"/> as a shape in the ribbon's box — its glyph's outline, centred — or null where the font has no such icon and
    /// the offer is named instead. Each is made once.
    /// </summary>
    private static Geometry? Drawn(IconRef icon) => Shapes.GetOrAdd(icon, static icon =>
    {
        var font = icon.IsEmpty || !IconCatalog.Contains(icon) ? null : IconCatalog.FontFor(icon);
        if (font is null) return null;

        var face = new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var glyph = new FormattedText(IconCatalog.GlyphFor(icon), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, Side, Brushes.Black, 1.0)
            .BuildGeometry(new Point(0, 0));

        var bounds = glyph.Bounds;
        if (bounds.IsEmpty) return null;

        glyph.Transform = new TranslateTransform((Side - bounds.Width) / 2 - bounds.X, (Side - bounds.Height) / 2 - bounds.Y);
        glyph.Freeze();
        return glyph;
    });

    /// <summary>Every icon drawn so far.</summary>
    private static readonly ConcurrentDictionary<IconRef, Geometry?> Shapes = new();

    /// <summary>How wide and high the box an icon is drawn in is.</summary>
    private const double Side = 16;

    /// <summary>
    /// A character written so it reads as itself. A line shown as its characters stays shown as its characters while it is written
    /// in — typed at its end it grows to hold what was typed, or the markup would turn back into what it reads as under the caret.
    /// </summary>
    private static ContentChange? Typing(ContentEdit edit)
    {
        var state = edit.State;
        var text = edit.Text;
        if (text.Length == 0) return null;

        if (!state.HasSelection && state.Raw is { } shown && shown.Holds(state.Caret))
            return ContentChange.Typed(state, text, shown with { End = shown.End + text.Length });

        return text.Length == 1 && Prose(edit.Part) ? Literal(state, text[0]) : null;
    }

    /// <summary>Enter: what comes next after the line it was pressed on.</summary>
    private static ContentChange? Breaking(ContentEdit edit)
    {
        var state = edit.State;
        if (!state.HasSelection && state.Raw is { } shown && shown.Holds(state.Caret)) return null;
        if (!Prose(edit.Part)) return null;

        // A table is a line per row, so there is no second line a cell could go on to.
        if (Within(edit.Part, MarkdownKinds.Cell)) return ContentChange.Stay(state);

        return Broken(state, Within(edit.Part, MarkdownKinds.Item));
    }

    /// <summary>Taking back a character: the gap between two paragraphs, or the characters a line was written with.</summary>
    private static ContentChange? Erasing(ContentEdit edit, bool forward)
    {
        var state = edit.State;
        if (state.HasSelection) return null;

        // Already shown as its characters: it is text to its ends, and no further.
        if (state.Raw is { } raw)
            return !forward && raw.Holds(state.Caret) && state.Caret == raw.Start ? ContentChange.Stay(state) : null;

        if (Joined(state, forward) is { } joined) return joined;
        if (forward) return null;

        return Written(edit.Landing) is { } line ? ContentChange.Showing(state, line) : null;
    }

    // ── What is typed is what is on the page ────────────────────────────────

    /// <summary>
    /// The character written so that it reads as itself — behind a backslash where markdown would read it as markup, and
    /// as itself everywhere else. Null where nothing needs doing, which leaves it to be typed as any character is.
    /// </summary>
    internal static ContentChange? Literal(EditState state, char typed)
    {
        // Read as the line will be once what is picked out is gone, which changes nothing in front of where the character goes.
        var cleared = state.HasSelection ? state.Write(string.Empty) : state;
        var source = cleared.Source;
        var at = Math.Clamp(cleared.Caret, 0, source.Length);

        // Two things only become markup once something follows them, so they are put beyond it when that arrives: an
        // entity when its semicolon is typed, and a tag when the first letter of its name is.
        var behind = typed == ';' ? Entity(source, at)
                   : (char.IsLetter(typed) || typed is '/' or '!' or '?') && at > 0 && source[at - 1] == '<' && !Escaped(source, at - 1) ? at - 1
                   : (int?)null;

        if (behind is { } mark)
        {
            var written = ContentChange.Typed(state, typed.ToString());
            return written.And(new ContentWrite(mark, 0, "\\")) with { Caret = written.Caret + 1, Raw = null };
        }

        return Marks(source, at, typed) ? ContentChange.Typed(state, "\\" + typed) : null;
    }

    /// <summary>Whether <paramref name="typed"/>, written at <paramref name="at"/>, is something markdown would read as markup.</summary>
    private static bool Marks(string source, int at, char typed)
    {
        var before = at > 0 ? source[at - 1] : '\n';
        var starts = Starts(source, at);

        return typed switch
        {
            '\\' or '`' or '*' or '[' or ']' or '~' or '|' or '$' or '^' => true,

            // Inside a word an underscore opens nothing, which is what lets snake_case be written as it is.
            '_' => !char.IsLetterOrDigit(before),

            // Doubled they mark a word; at the start of a line they underline the one above it into a heading.
            '=' or '+' => before == typed || starts,

            '#' or '>' or '-' or ':' => starts,

            // A number and a full stop at the start of a line is a list.
            '.' or ')' => Counted(source, at),

            _ => false,
        };
    }

    /// <summary>
    /// Whether what stands before <paramref name="at"/> on its line is only the machinery of the blocks it is in — a
    /// quote's marks, an item's marker, a heading's hashes — so that anything typed there begins what the line says.
    /// </summary>
    private static bool Starts(string source, int at) => Opening().IsMatch(Before(source, at));

    /// <summary>Whether the line so far, past its machinery, is a number — so a full stop or a bracket after it would count.</summary>
    private static bool Counted(string source, int at)
    {
        var line = Before(source, at);
        var opening = Opening().Match(line);
        var rest = line[(opening.Success ? opening.Length : 0)..];

        return rest.Length is > 0 and <= 9 && rest.All(char.IsAsciiDigit);
    }

    /// <summary>The line <paramref name="at"/> is on, up to it.</summary>
    private static string Before(string source, int at)
    {
        var start = at == 0 ? 0 : source.LastIndexOf('\n', at - 1) + 1;
        return source[start..at];
    }

    /// <summary>Where an unescaped <c>&amp;</c> starts what the semicolon about to be typed would make an entity, or null.</summary>
    private static int? Entity(string source, int at)
    {
        var from = at - 1;
        while (from >= 0 && (char.IsAsciiLetterOrDigit(source[from]) || source[from] == '#')) from--;

        return from >= 0 && from < at - 1 && source[from] == '&' && !Escaped(source, from) ? from : null;
    }

    /// <summary>Whether the character at <paramref name="at"/> is already written behind a backslash.</summary>
    private static bool Escaped(string source, int at)
    {
        var slashes = 0;
        for (var before = at - 1; before >= 0 && source[before] == '\\'; before--) slashes++;
        return slashes % 2 == 1;
    }

    // ── Enter starts what comes next ────────────────────────────────────────

    /// <summary>
    /// What Enter writes where the caret is: the next item, the next paragraph of a quote, or the next paragraph — over whatever is
    /// picked out.
    /// </summary>
    private static ContentChange Broken(EditState state, bool inItem)
    {
        var cleared = state.HasSelection ? state.Write(string.Empty) : state;
        var source = cleared.Source;
        var at = Math.Clamp(cleared.Caret, 0, source.Length);
        var start = at == 0 ? 0 : source.LastIndexOf('\n', at - 1) + 1;
        var ending = source.IndexOf('\n', at);
        var end = ending < 0 ? source.Length : ending > start && source[ending - 1] == '\r' ? ending - 1 : ending;
        var line = source[start..end];

        var quote = Quoted().Match(line).Value;

        if (inItem && Marker().Match(line, quote.Length) is { Success: true } marker && marker.Index == quote.Length)
        {
            var written = start + quote.Length + marker.Length;

            // Nothing written in it: this is the Enter that ends the list, and the marker goes.
            if (!state.HasSelection && string.IsNullOrWhiteSpace(source[Math.Min(written, end)..end]) && at >= written)
                return ContentChange.Write(start + quote.Length, end - (start + quote.Length), string.Empty);

            var bullet = marker.Groups["bullet"].Value;
            var next = int.TryParse(bullet[..^1], out var number) && bullet.Length > 1 && bullet[^1] is '.' or ')'
                ? (number + 1).ToString() + bullet[^1]
                : bullet;

            return ContentChange.Typed(state, "\n" + quote + marker.Groups["indent"].Value + next + marker.Groups["gap"].Value
                                              + (marker.Groups["task"].Success ? "[ ] " : string.Empty));
        }

        return ContentChange.Typed(state, quote.Length > 0 ? "\n" + quote.TrimEnd() + "\n" + quote : "\n\n");
    }

    // ── Backspace ───────────────────────────────────────────────────────────

    /// <summary>
    /// Backspace at the start of a paragraph, or delete at the end of one, where only the gap between it and its neighbour
    /// is in the way: the gap goes, and the two are one. Null anywhere else.
    /// </summary>
    private static ContentChange? Joined(EditState state, bool forward)
    {
        var source = state.Source;
        var at = Math.Clamp(state.Caret, 0, source.Length);

        var (from, to) = forward ? (at, Skip(source, at, +1)) : (Skip(source, at, -1), at);
        if (to - from < 2 || source[from..to].Count(character => character == '\n') < 2) return null;

        // Nothing on the far side of the gap is no neighbour to join.
        if (from == 0 || to == source.Length) return null;

        return ContentChange.Write(from, to - from, string.Empty);
    }

    /// <summary>Past the white space on one side of <paramref name="at"/>.</summary>
    private static int Skip(string source, int at, int step)
    {
        var to = at;
        if (step < 0) while (to > 0 && char.IsWhiteSpace(source[to - 1])) to--;
        else while (to < source.Length && char.IsWhiteSpace(source[to])) to++;
        return to;
    }

    /// <summary>
    /// The line the caret stands at the end of, where that line is drawn as something other than the characters it was
    /// written with — or null where it is drawn as itself, and taking back a character means taking back a character.
    /// </summary>
    private static RawZone? Written(Landing landing)
    {
        var source = landing.State.Source;
        var caret = Math.Clamp(landing.State.Caret, 0, source.Length);

        var start = caret == 0 ? 0 : source.LastIndexOf('\n', caret - 1) + 1;
        var ending = source.IndexOf('\n', caret);
        var end = ending < 0 ? source.Length
                : ending > start && source[ending - 1] == '\r' ? ending - 1
                : ending;

        if (caret != end || start >= end) return null;

        return Shows(landing, start, end) ? null : new RawZone(start, end);
    }

    /// <summary>
    /// Whether every character of a line is on the screen as itself. Asked of what was drawn rather than of the tree,
    /// because the question is what a reader can see: a run says whether what it shows is what it was set from, and a
    /// line whose runs do not account for all of it has something on it that is not being shown.
    /// </summary>
    private static bool Shows(Landing landing, int start, int end)
    {
        var shown = 0;

        foreach (var piece in landing.Laid.Root.SelfAndDescendants())
            if (piece.Words is { Maps: true }
                && piece.Part is { } part
                && part.Start >= start
                && part.Start + part.Length <= end)
                shown += part.Length;

        return shown >= end - start;
    }

    // ── Where the caret is ──────────────────────────────────────────────────

    private static bool Prose(ContentPart? part) =>
        part is null || (!Within(part, MarkdownKinds.Code, MarkdownKinds.Html, MarkdownKinds.FrontMatter, MarkdownKinds.Reference, MarkdownKinds.Formula)
                         && !ContentNested.Holders(part).Any());

    /// <summary>Whether a part is, or is inside, one of <paramref name="kinds"/>.</summary>
    private static bool Within(ContentPart? part, params string[] kinds)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (kinds.Contains(at.Kind)) return true;

        return false;
    }

    /// <summary>The machinery a line opens with: a quote's marks, an item's marker and its box, a heading's hashes.</summary>
    [GeneratedRegex(@"^[ \t>]*(?:(?:[-*+]|\d{1,9}[.)])[ \t]+(?:\[[ xX]\][ \t]+)?)*(?:#{1,6}[ \t]+)?[ \t]*$")]
    private static partial Regex Opening();

    /// <summary>A quote's marks at the start of a line.</summary>
    [GeneratedRegex(@"^(?:[ \t]{0,3}>[ \t]?)*")]
    private static partial Regex Quoted();

    /// <summary>An item's marker: how far in it is, the bullet or number, the gap after it, and its box.</summary>
    [GeneratedRegex(@"\G(?<indent>[ \t]*)(?<bullet>[-*+]|\d{1,9}[.)])(?<gap>[ \t]+)(?<task>\[[ xX]\][ \t]+)?")]
    private static partial Regex Marker();
}
