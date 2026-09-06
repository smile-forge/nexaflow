using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.Abc.Stages;

/// <summary>
/// Hangs each syllable of a <c>w:</c> line under the note it is sung on.
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
                    var sung = Sing(lines[music], Syllables(lines[at].Part(AbcRoles.Value)?.Text ?? ""), verse++);
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

    /// <summary>One syllable, and what it does to the note it lands on.</summary>
    private readonly record struct Syllable(string Text, bool Hyphen, bool Melisma, bool Skip, bool NextBar);

    /// <summary>
    /// A <c>w:</c> line, cut into the pieces that land on notes. Everything here is about counting, and
    /// the pieces that land on nothing — a bar jump, a skipped note — are kept as pieces so the counting
    /// stays a single walk.
    /// </summary>
    private static List<Syllable> Syllables(string line)
    {
        var pieces = new List<Syllable>();
        var word = new System.Text.StringBuilder();

        void Flush(bool hyphen)
        {
            if (word.Length == 0 && !hyphen) return;
            pieces.Add(new Syllable(word.ToString().Replace("~", " "), hyphen, false, false, false));
            word.Clear();
        }

        for (var at = 0; at < line.Length; at++)
        {
            var c = line[at];

            switch (c)
            {
                case '\\' when at + 1 < line.Length && line[at + 1] == '-':
                    word.Append('-');
                    at++;
                    continue;

                case '-':
                    Flush(hyphen: true);
                    continue;

                case ' ':
                case '\t':
                    Flush(hyphen: false);
                    continue;

                case '_':
                    Flush(hyphen: false);
                    pieces.Add(new Syllable("", false, Melisma: true, false, false));
                    continue;

                case '*':
                    Flush(hyphen: false);
                    pieces.Add(new Syllable("", false, false, Skip: true, false));
                    continue;

                case '|':
                    Flush(hyphen: false);
                    pieces.Add(new Syllable("", false, false, false, NextBar: true));
                    continue;

                default:
                    word.Append(c);
                    continue;
            }
        }

        Flush(hyphen: false);
        return pieces;
    }

    // ── Putting them under the notes ────────────────────────────────────────

    /// <summary>
    /// The music line with one verse's syllables hung under its notes. Rests take no syllable — nobody
    /// sings a silence — and a bar jump skips whatever is left of the bar it is in.
    /// </summary>
    private static ContentNode Sing(ContentNode line, List<Syllable> syllables, int verse)
    {
        if (syllables.Count == 0) return line;

        var at = 0;
        var skipping = false;
        return Under(line, syllables, ref at, ref skipping, verse);
    }

    private static ContentNode Under(ContentNode node, List<Syllable> syllables, ref int at, ref bool skipping,
                                     int verse)
    {
        if (node.Kind is AbcKinds.Note or AbcKinds.Chord)
        {
            // A note inside a chord is not sung on its own; the chord is.
            if (node.Role == AbcRoles.Note) return node;
            if (skipping || at >= syllables.Count) return node;

            var syllable = syllables[at++];
            if (syllable.Skip) return node;
            if (syllable.NextBar) { skipping = true; return node; }

            return node.Saying(
                AbcKinds.Text,
                AbcRoles.Lyric,
                $"{verse}:{(syllable.Melisma ? "_" : syllable.Text)}{(syllable.Hyphen ? "-" : "")}");
        }

        if (node.Kind == AbcKinds.Measure) skipping = false;

        if (node.IsLeaf) return node;

        var rebuilt = new List<ContentNode>(node.Children.Count);
        var moved = false;

        foreach (var child in node.Children)
        {
            var seen = Under(child, syllables, ref at, ref skipping, verse);
            moved |= !ReferenceEquals(seen, child);
            rebuilt.Add(seen);
        }

        return moved ? node.With(rebuilt) : node;
    }

    // ── Reading the answers back ────────────────────────────────────────────

    /// <summary>The syllables sung on this event, one per verse, in verse order.</summary>
    public static IEnumerable<(int Verse, string Text, bool Hyphen, bool Melisma)> Of(ContentNode node)
    {
        foreach (var derived in node.Children)
        {
            if (derived.Role != Roles.Derived) continue;

            foreach (var fact in derived.Children)
            {
                if (fact.Role != AbcRoles.Lyric) continue;

                var split = fact.Text.IndexOf(':');   // the verse number, then the syllable
                if (split < 0 || !int.TryParse(fact.Text[..split], out var verse)) continue;

                var text = fact.Text[(split + 1)..];
                var hyphen = text.EndsWith('-');
                if (hyphen) text = text[..^1];

                yield return (verse, text == "_" ? "" : text, hyphen, text == "_");
            }
        }
    }
}
