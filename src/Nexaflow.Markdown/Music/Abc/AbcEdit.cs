using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc.Stages;

namespace Nexaflow.Markdown.Music.Abc;

/// <summary>
/// Changing a tune by changing its tree.
///
/// <para>
/// Every gesture here rewrites <em>one leaf of one note</em>, and that is not a coincidence — it is what
/// ABC's own shape gives us. An accidental is a prefix, a length is a suffix, and an octave is the
/// letter's case plus a run of marks after it, so sharpening a note replaces its accidental leaf and
/// touches nothing else. A tune somebody lined up by hand still reads that way afterwards, which a string
/// splice cannot promise and a reformat certainly cannot.
/// </para>
/// <para>
/// <strong>What comes back is provisional.</strong> The stages between the parser and the builder do not
/// re-derive themselves when a tree is changed underneath them — a note whose accidental has just changed
/// still carries the pitch that was worked out for the old one — so an edit is printed, and the source it
/// prints as is read back and built from. Which is also why editing the tree is worth the trouble: an edit
/// expressed against a part knows what it touched, so trouble afterwards can be blamed on the keystroke
/// that caused it rather than guessed at by diffing a string.
/// </para>
/// <para>
/// Plain typing is not here. A letter inserted at the caret needs no reshaping — ABC has no construct that
/// has to be bracketed when it grows — so the caller splices it, exactly as the formula editor splices a
/// character its own tree did not have to reshape. What <see cref="NoteAt"/> offers is only the question a
/// splice cannot answer for itself: which octave the new note belongs in.
/// </para>
/// </summary>
public static class AbcEdit
{
    /// <summary>The one ladder an octave gesture walks: the letter's case, then marks either side of it.</summary>
    private const int LowestWrittenOctave = 4;   // an upper-case letter with no marks

    // ── The gestures ────────────────────────────────────────────────────────

    /// <summary>
    /// Moves each of <paramref name="notes"/> <paramref name="by"/> octaves, rewriting the letter's case
    /// and its marks: <c>C,</c> → <c>C</c> → <c>c</c> → <c>c'</c>.
    /// </summary>
    public static AstWrite? Octave(ContentReading reading, IReadOnlyList<ContentPart> notes, int by) =>
        by == 0 ? null : Rewrite(reading, notes, note => Moved(note, by));

    /// <summary>
    /// Raises or lowers each of <paramref name="notes"/> by a semitone, writing the accidental out.
    ///
    /// <para>
    /// From what the note actually <em>sounds</em>, not from what is written in front of it — which is the
    /// difference between a gesture that works and one that surprises. A bare <c>F</c> in G major is an F
    /// sharp; flattening it has to write <c>=F</c>, because taking an accidental away would leave the key
    /// signature to sharpen it again. The stage worked the sounding pitch out already, so this asks it.
    /// </para>
    /// </summary>
    public static AstWrite? Accidental(ContentReading reading, IReadOnlyList<ContentPart> notes, int by) =>
        by == 0 ? null : Rewrite(reading, notes, note => Altered(note, by));

    /// <summary>
    /// Doubles or halves the written length of each of <paramref name="notes"/>: <c>A</c> → <c>A2</c> →
    /// <c>A4</c>, and back down through <c>A/2</c>, <c>A/4</c>. Dots survive, because the multiplier is
    /// scaled rather than replaced — <c>A3/2</c> doubles to <c>A3</c> and halves to <c>A3/4</c>.
    /// </summary>
    public static AstWrite? Length(ContentReading reading, IReadOnlyList<ContentPart> notes, int steps) =>
        steps == 0 ? null : Rewrite(reading, notes, note => Stretched(note, steps));

    /// <summary>
    /// What to type for a new note of <paramref name="letter"/> at <paramref name="caret"/>: the letter,
    /// in the octave the note before it was in.
    ///
    /// <para>
    /// Carried from the note before rather than fixed, because a melody typed left to right stays where
    /// the writer is looking. Typing <c>G</c> after <c>c'</c> means the G above it, not the G two octaves
    /// down — anything else makes the reader chase the tune back to where they were.
    /// </para>
    /// </summary>
    public static string NoteAt(ContentReading reading, int caret, char letter)
    {
        if (!AbcParser.IsNoteLetter(letter)) return letter.ToString();

        var octave = Before(reading, caret) is { } previous ? Sounding(previous) : 5;
        var step = AbcTheory.StepLetters.IndexOf(char.ToUpperInvariant(letter));
        if (step < 0) return letter.ToString();

        // The nearest octave to the note before it, so a step up a scale never jumps a register.
        return Spell(step, octave);
    }

    // ── Working out what each gesture writes ────────────────────────────────

    private static ContentNode? Moved(ContentNode note, int by)
    {
        if (Step(note) is not { } step) return null;
        return Respell(note, step, Sounding(note) + by);
    }

    private static ContentNode? Altered(ContentNode note, int by)
    {
        if (Step(note) is null) return null;

        // What it sounds now, which is the written accidental where there is one and the key's where
        // there is not — a distinction only the stage that read the whole line can make.
        var sounds = ResolveNotes.PitchOf(note)?.Alter ?? 0;
        var wanted = Math.Clamp(sounds + by, -2, 2);

        var mark = wanted switch { 2 => "^^", 1 => "^", -1 => "_", -2 => "__", _ => "=" };
        return With(note, AbcRoles.Accidental, AbcKinds.Accidental, mark);
    }

    private static ContentNode? Stretched(ContentNode note, int steps)
    {
        var factor = AbcTheory.Factor(note.Part(AbcRoles.Length)?.Text);

        for (var i = 0; i < Math.Abs(steps); i++)
            factor = steps > 0
                ? AbcLength.Of(factor.Numerator * 2, factor.Denominator)
                : AbcLength.Of(factor.Numerator, factor.Denominator * 2);

        // Past a breve on one side and a 64th on the other there is nothing left to write.
        if (factor.Numerator > 64 || factor.Denominator > 64) return null;

        return With(note, AbcRoles.Length, AbcKinds.Length, Suffix(factor));
    }

    /// <summary>How ABC writes a length multiplier: <c>2</c>, <c>/2</c>, <c>3/2</c>, and nothing for one.</summary>
    private static string Suffix(AbcLength factor)
    {
        if (factor.Denominator == 1) return factor.Numerator == 1 ? "" : $"{factor.Numerator}";
        if (factor.Numerator == 1) return factor.Denominator == 2 ? "/" : $"/{factor.Denominator}";
        return $"{factor.Numerator}/{factor.Denominator}";
    }

    /// <summary>
    /// The note respelled in <paramref name="octave"/>: an upper-case letter for octave 4 and below, a
    /// lower-case one for 5 and above, and the marks that carry it the rest of the way.
    /// </summary>
    private static ContentNode? Respell(ContentNode note, int step, int octave)
    {
        if (octave is < -1 or > 12) return null;

        var spelled = Spell(step, octave);
        var letter = spelled[..1];
        var marks = spelled[1..];

        var rebuilt = With(note, AbcRoles.Letter, AbcKinds.Letter, letter);
        return rebuilt is null ? null : With(rebuilt, AbcRoles.Octave, AbcKinds.Octave, marks);
    }

    /// <summary>The letter and marks for a step in an octave — <c>C,,</c>, <c>C</c>, <c>c</c>, <c>c''</c>.</summary>
    private static string Spell(int step, int octave)
    {
        var letter = AbcTheory.StepLetters[step];

        if (octave <= LowestWrittenOctave)
            return char.ToUpperInvariant(letter) + new string(',', LowestWrittenOctave - octave);

        return char.ToLowerInvariant(letter) + new string('\'', octave - LowestWrittenOctave - 1);
    }

    /// <summary>Which octave a note is written in, letter case and marks together.</summary>
    private static int Sounding(ContentNode note)
    {
        if (note.Part(AbcRoles.Letter)?.Text is not { Length: 1 } letter) return 5;

        var octave = char.IsUpper(letter[0]) ? LowestWrittenOctave : LowestWrittenOctave + 1;
        foreach (var mark in note.Part(AbcRoles.Octave)?.Text ?? "")
            octave += mark == '\'' ? 1 : -1;

        return octave;
    }

    private static int? Step(ContentNode note) =>
        note.Part(AbcRoles.Letter)?.Text is { Length: 1 } letter
        && AbcTheory.StepLetters.IndexOf(char.ToUpperInvariant(letter[0])) is var step and >= 0
            ? step
            : null;

    /// <summary>The note before <paramref name="caret"/>, or null when there is none.</summary>
    private static ContentNode? Before(ContentReading reading, int caret)
    {
        ContentPart? best = null;

        foreach (var part in reading.Root.SelfAndDescendants())
        {
            if (part.Kind != AbcKinds.Note || part.Derived) continue;
            if (part.Part(AbcRoles.Letter) is null) continue;
            if (part.End > caret) continue;
            if (best is null || part.End > best.End) best = part;
        }

        return best?.Node;
    }

    // ── Rewriting the tree ──────────────────────────────────────────────────

    /// <summary>
    /// Applies <paramref name="change"/> to every note in <paramref name="notes"/>, and says where in the
    /// new source the writing landed.
    /// <para>
    /// Null when nothing changed — a caller with rules of its own about plain typing wants to know, and a
    /// gesture that could not be carried out should not push an undo step.
    /// </para>
    /// </summary>
    private static AstWrite? Rewrite(ContentReading reading, IReadOnlyList<ContentPart> notes,
                                     Func<ContentNode, ContentNode?> change)
    {
        var root = reading.Root.Node;
        var touched = new List<(ContentNode Node, int Start)>();
        var moved = false;

        // Right to left, so an earlier note's offsets are still true when a later one has already grown.
        foreach (var note in notes.Where(Editable).OrderByDescending(n => n.Start))
        {
            if (change(note.Node) is not { } replacement) continue;
            if (replacement.Same(note.Node)) continue;

            root = Swap(root, note.Node, replacement);
            touched.Add((replacement, note.Start));
            moved = true;
        }

        if (!moved) return null;

        // Where the writing ended up, taken from the tree that now holds it rather than worked out from
        // the shape of a change nobody made in one piece.
        var start = touched.Min(t => t.Start);
        var end = 0;

        foreach (var place in root.Placed())
            if (touched.Any(t => ReferenceEquals(t.Node, place.Node)))
                end = Math.Max(end, place.End);

        return new AstWrite(root, start, Math.Max(0, end - start), Reshaped: true);
    }

    /// <summary>A note somebody wrote, with a letter to act on.</summary>
    private static bool Editable(ContentPart part) =>
        part.Kind == AbcKinds.Note && !part.Derived && part.Part(AbcRoles.Letter) is not null;

    /// <summary>
    /// The tree with one piece replaced. Every subtree that was not on the way to it is the object it
    /// already was, so an edit costs the spine and nothing else.
    /// </summary>
    private static ContentNode Swap(ContentNode node, ContentNode target, ContentNode replacement)
    {
        if (ReferenceEquals(node, target)) return replacement;
        if (node.IsLeaf) return node;

        var rebuilt = new List<ContentNode>(node.Children.Count);
        var moved = false;

        foreach (var child in node.Children)
        {
            var seen = Swap(child, target, replacement);
            moved |= !ReferenceEquals(seen, child);
            rebuilt.Add(seen);
        }

        return moved ? node.With(rebuilt) : node;
    }

    /// <summary>
    /// The note with one of its leaves set to <paramref name="text"/> — added where it had none, replaced
    /// where it had one, removed where the text is empty.
    /// <para>
    /// Where a new leaf goes is fixed by the notation rather than chosen: an accidental is written in
    /// front of the letter and everything else after it, which is the whole of the ordering rule.
    /// </para>
    /// </summary>
    private static ContentNode? With(ContentNode note, string role, string kind, string text)
    {
        var rebuilt = new List<ContentNode>(note.Children.Count + 1);
        var replaced = false;

        foreach (var child in note.Children)
        {
            if (child.Role != role) { rebuilt.Add(child); continue; }

            replaced = true;
            if (text.Length > 0) rebuilt.Add(ContentNode.Leaf(kind, text, role));
        }

        if (!replaced && text.Length > 0)
        {
            var at = role == AbcRoles.Accidental
                ? 0
                : rebuilt.FindLastIndex(child => child.Role is AbcRoles.Accidental or AbcRoles.Letter
                                                              or AbcRoles.Octave) + 1;

            rebuilt.Insert(Math.Clamp(at, 0, rebuilt.Count), ContentNode.Leaf(kind, text, role));
        }

        return note.With(rebuilt);
    }
}
