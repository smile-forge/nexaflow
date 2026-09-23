using System;
using System.Linq;
using System.Text.RegularExpressions;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
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
/// </summary>
internal sealed partial class MarkdownEdits : IOnEdit
{
    public static MarkdownEdits Instance { get; } = new();

    /// <inheritdoc/>
    /// <remarks>
    /// A line shown as its characters stays shown as its characters while it is written in — typed at its end it grows
    /// to hold what was typed, or the markup would turn back into what it reads as under the caret.
    /// </remarks>
    public EditState? Typing(ContentEdit edit, string text)
    {
        var state = edit.State;
        if (text.Length == 0) return null;

        if (!state.HasSelection && state.Raw is { } shown && shown.Holds(state.Caret))
            return state.Write(text, shown with { End = shown.End + text.Length });

        if (text.Length != 1 || !Prose(edit.Landing)) return null;

        return Literal(state.HasSelection ? state.Write(string.Empty) : state, text[0]);
    }

    /// <inheritdoc/>
    public EditState? Settling(ContentEdit edit, string separator)
    {
        if (separator != "\n") return Typing(edit, separator);

        var state = edit.State;
        if (!state.HasSelection && state.Raw is { } shown && shown.Holds(state.Caret)) return null;

        var part = MarkdownContent.Standing(edit.Landing);
        if (!Prose(part)) return null;

        // A table is a line per row, so there is no second line a cell could go on to.
        if (Within(part, MarkdownKinds.Cell)) return state;

        return Broken(state.HasSelection ? state.Write(string.Empty) : state, Within(part, MarkdownKinds.Item));
    }

    /// <inheritdoc/>
    public EditState? Erasing(ContentEdit edit, bool forward)
    {
        var state = edit.State;
        if (state.HasSelection) return null;

        // Already shown as its characters: it is text to its ends, and no further.
        if (state.Raw is { } raw)
            return !forward && raw.Holds(state.Caret) && state.Caret == raw.Start ? state : null;

        if (Joined(state, forward) is { } joined) return joined;
        if (forward) return null;

        return Written(edit.Landing) is { } line ? state with { Raw = line } : null;
    }

    // ── What is typed is what is on the page ────────────────────────────────

    /// <summary>
    /// The character written so that it reads as itself — behind a backslash where markdown would read it as markup, and
    /// as itself everywhere else. Null where nothing needs doing, which leaves it to be typed as any character is.
    /// </summary>
    internal static EditState? Literal(EditState state, char typed)
    {
        var source = state.Source;
        var at = Math.Clamp(state.Caret, 0, source.Length);

        // Two things only become markup once something follows them, so they are put beyond it when that arrives: an
        // entity when its semicolon is typed, and a tag when the first letter of its name is.
        if (typed == ';' && Entity(source, at) is { } amp) return Behind(state, amp).Write(";");

        if ((char.IsLetter(typed) || typed is '/' or '!' or '?') && at > 0 && source[at - 1] == '<' && !Escaped(source, at - 1))
            return Behind(state, at - 1).Write(typed.ToString());

        return Marks(source, at, typed) ? state.Write("\\" + typed) : null;
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

    /// <summary>A backslash put in front of the character at <paramref name="at"/>, which is before the caret.</summary>
    private static EditState Behind(EditState state, int at) =>
        state with { Source = state.Source.Insert(at, "\\"), Caret = state.Caret + 1, Selected = [], Raw = null };

    // ── Enter starts what comes next ────────────────────────────────────────

    /// <summary>What Enter writes where the caret is: the next item, the next paragraph of a quote, or the next paragraph.</summary>
    private static EditState Broken(EditState state, bool inItem)
    {
        var source = state.Source;
        var at = Math.Clamp(state.Caret, 0, source.Length);
        var start = at == 0 ? 0 : source.LastIndexOf('\n', at - 1) + 1;
        var ending = source.IndexOf('\n', at);
        var end = ending < 0 ? source.Length : ending > start && source[ending - 1] == '\r' ? ending - 1 : ending;
        var line = source[start..end];

        var quote = Quoted().Match(line).Value;

        if (inItem && Marker().Match(line, quote.Length) is { Success: true } marker && marker.Index == quote.Length)
        {
            var written = start + quote.Length + marker.Length;

            // Nothing written in it: this is the Enter that ends the list, and the marker goes.
            if (string.IsNullOrWhiteSpace(source[Math.Min(written, end)..end]) && at >= written)
                return state with
                {
                    Source = source[..(start + quote.Length)] + source[end..],
                    Caret = start + quote.Length,
                    Selected = [],
                    Raw = null,
                };

            var bullet = marker.Groups["bullet"].Value;
            var next = int.TryParse(bullet[..^1], out var number) && bullet.Length > 1 && bullet[^1] is '.' or ')'
                ? (number + 1).ToString() + bullet[^1]
                : bullet;

            return state.Write("\n" + quote + marker.Groups["indent"].Value + next + marker.Groups["gap"].Value
                               + (marker.Groups["task"].Success ? "[ ] " : string.Empty));
        }

        return quote.Length > 0
            ? state.Write("\n" + quote.TrimEnd() + "\n" + quote)
            : state.Write("\n\n");
    }

    // ── Backspace ───────────────────────────────────────────────────────────

    /// <summary>
    /// Backspace at the start of a paragraph, or delete at the end of one, where only the gap between it and its neighbour
    /// is in the way: the gap goes, and the two are one. Null anywhere else.
    /// </summary>
    private static EditState? Joined(EditState state, bool forward)
    {
        var source = state.Source;
        var at = Math.Clamp(state.Caret, 0, source.Length);

        var (from, to) = forward ? (at, Skip(source, at, +1)) : (Skip(source, at, -1), at);
        if (to - from < 2 || source[from..to].Count(character => character == '\n') < 2) return null;

        // Nothing on the far side of the gap is no neighbour to join.
        if (from == 0 || to == source.Length) return null;

        return state with { Source = source[..from] + source[to..], Caret = from, Selected = [], Raw = null };
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

    /// <summary>Whether the caret stands in words a reader writes, rather than in code, markup or a document's front matter.</summary>
    private static bool Prose(Landing landing) => Prose(MarkdownContent.Standing(landing));

    private static bool Prose(ContentPart? part) =>
        part is null || !Within(part, MarkdownKinds.Code, MarkdownKinds.Fence, MarkdownKinds.Html, MarkdownKinds.FrontMatter,
                                MarkdownKinds.Reference, MarkdownKinds.Math, MarkdownKinds.Formula);

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
