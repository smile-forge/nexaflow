using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.Abc.Stages;

/// <summary>
/// Says of each note which syllable of each <c>w:</c> line is sung on it (<see cref="AbcEventNode"/>).
///
/// <para>
/// A lyric line is written after the music it belongs to and lines up with it by counting: a space or a
/// hyphen ends a syllable, <c>_</c> holds the one before it over another note, <c>*</c> passes a note by
/// without a word, <c>|</c> jumps to the next bar however many notes are left in this one, <c>~</c> glues
/// two words onto one note, and <c>\-</c> is a hyphen somebody meant literally. Several <c>w:</c> lines
/// stack as verses.
/// </para>
/// <para>
/// So a syllable's position is a fact about two lines at once, and neither of them says it. That is why
/// this is a stage rather than something the builder works out: by the time anything is being drawn, the
/// question "which note is this word under" should already have an answer, and it should be an answer
/// somebody can point at.
/// </para>
/// </summary>
public sealed class AlignLyrics : IAstStage
{
    public string Name => "abc:lyrics";

    public ContentNode Run(ContentNode tree)
    {
        if (tree.Kind != AbcKinds.Tune) return tree;

        var lines = new List<ContentNode>(tree.Children);
        var moved = false;
        var music = -1;
        var verse = 0;

        for (var at = 0; at < lines.Count; at++)
        {
            switch (lines[at].Kind)
            {
                case AbcKinds.Line:
                    music = at;
                    verse = 0;
                    continue;

                case AbcKinds.LyricLine when music >= 0:
                {
                    // Printed rather than read off the node: a lyric's value is the syllables it was split
                    // into, so its characters are its children's rather than its own.
                    var sung = Sing(lines[music], Syllables(lines[at].Part(AbcRoles.Value)), verse++, at);
                    if (ReferenceEquals(sung, lines[music])) continue;

                    lines[music] = sung;
                    moved = true;
                    continue;
                }

                case AbcKinds.Field:
                    // A field between the music and its words is fine — but a new key or a new title
                    // starts a new stretch of music, and words after it belong to that.
                    continue;

                default:
                    continue;
            }
        }

        return moved ? tree.With(lines) : tree;
    }

    // ── Reading a w: line ───────────────────────────────────────────────────

    /// <summary>One syllable, what it does to the note it lands on, and where it was written.</summary>
    /// <param name="At">
    /// Which piece of the verse it is, so whatever draws it can find the characters again. The layout tree
    /// and this tree need not look alike — a syllable is drawn under a note in the music line and written
    /// in the <c>w:</c> line, two different branches — and an index is what lets one point at the other
    /// without either having to hold a reference into the other.
    /// </param>
    private readonly record struct Syllable(string Text, bool Hyphen, bool Melisma, bool Skip, bool NextBar,
                                            int At);

    /// <summary>
    /// A <c>w:</c> line, cut into the pieces that land on notes.
    ///
    /// <para>
    /// Read off the pieces the parser already split the line into rather than scanned again here. Two
    /// scanners with the same rules is one rule written twice, and the parser's is the one that has
    /// characters attached to its answer.
    /// </para>
    /// <para>
    /// Everything here is about counting, and the pieces that land on nothing — a bar jump, a skipped
    /// note — are kept as pieces so the counting stays a single walk.
    /// </para>
    /// </summary>
    private static List<Syllable> Syllables(ContentNode? value)
    {
        var pieces = new List<Syllable>();
        if (value is null) return pieces;

        var children = value.Children;

        for (var at = 0; at < children.Count; at++)
        {
            var piece = children[at];

            if (piece.Kind == AbcKinds.Syllable)
            {
                var hyphen = at + 1 < children.Count && children[at + 1].Text.StartsWith('-');
                pieces.Add(new Syllable(Sings(piece), hyphen, false, false, false, at));
                continue;
            }

            if (piece.Kind != AbcKinds.LyricMark) continue;

            switch (piece.Text[0])
            {
                case '_': pieces.Add(new Syllable("", false, Melisma: true, false, false, at)); break;
                case '*': pieces.Add(new Syllable("", false, false, Skip: true, false, at)); break;
                case '|': pieces.Add(new Syllable("", false, false, false, NextBar: true, at)); break;
            }
        }

        return pieces;
    }

    /// <summary>
    /// What a syllable sings: its words, with a <c>~</c> — ABC's way of writing two words sung on one note — sung as the
    /// space it stands for, and the backslash that kept a hyphen in the word left out.
    /// </summary>
    private static string Sings(ContentNode syllable) =>
        syllable.IsLeaf
            ? syllable.Text
            : string.Concat(syllable.Children.Select(piece => piece.Role switch
            {
                AbcRoles.Joined => " ",
                AbcRoles.Escape => "",
                _ => piece.Text,
            }));

    // ── Putting them under the notes ────────────────────────────────────────

    /// <summary>
    /// The music line with one verse's syllables sung on its notes — the <c>w:</c> line at <paramref name="written"/> among the
    /// tune's lines. Rests take no syllable — nobody sings a silence — and a bar jump skips whatever is left of the bar it is in.
    /// </summary>
    private static ContentNode Sing(ContentNode line, List<Syllable> syllables, int verse, int written)
    {
        if (syllables.Count == 0) return line;

        var at = 0;
        var skipping = false;
        return Under(line, syllables, ref at, ref skipping, verse, written);
    }

    private static ContentNode Under(ContentNode node, List<Syllable> syllables, ref int at, ref bool skipping,
                                     int verse, int written)
    {
        if (node.Kind is AbcKinds.Note or AbcKinds.Chord)
        {
            // A note inside a chord is not sung on its own; the chord is.
            if (node.Role == AbcRoles.Note) return node;
            if (skipping || at >= syllables.Count) return node;

            var syllable = syllables[at++];
            if (syllable.Skip) return node;
            if (syllable.NextBar) { skipping = true; return node; }

            // The verse, where it was written and what it says. Where it was written is what lets whatever draws this
            // find the characters again in the `w:` line, which is somewhere else entirely in this tree.
            return AbcEventNode.Of(node).Singing(new AbcSung(verse, written, syllable.At,
                                                             syllable.Melisma ? "" : syllable.Text,
                                                             syllable.Hyphen, syllable.Melisma));
        }

        if (node.Kind == AbcKinds.Measure)
        {
            // A bar jump is settled at the bar line, not on a note. Consuming it when the next note asked
            // for a syllable spent that note's turn on the marker and left the whole bar unsung.
            while (at < syllables.Count && syllables[at].NextBar) at++;
            skipping = false;
        }

        if (node.IsLeaf) return node;

        var rebuilt = new List<ContentNode>(node.Children.Count);
        var moved = false;

        foreach (var child in node.Children)
        {
            var seen = Under(child, syllables, ref at, ref skipping, verse, written);
            moved |= !ReferenceEquals(seen, child);
            rebuilt.Add(seen);
        }

        return moved ? node.With(rebuilt) : node;
    }

    // ── Reading the answers back ────────────────────────────────────────────
}
