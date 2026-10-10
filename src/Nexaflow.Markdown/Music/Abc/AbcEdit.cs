using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Music.Abc.Stages;

namespace Nexaflow.Markdown.Music.Abc;

/// <summary>
/// Changing a tune by respelling one part of one note.
///
/// <para>
/// Every gesture here rewrites <em>one part of one note</em>, and that is not a coincidence — it is what
/// ABC's own shape gives us. An accidental is a prefix, a length is a suffix, and an octave is the
/// letter's case plus a run of marks after it, so sharpening a note writes over that note and touches
/// nothing else. A tune somebody lined up by hand still reads that way afterwards, which reprinting the
/// tune cannot promise.
/// </para>
/// <para>
/// <strong>What comes back is the change, not a tree.</strong> A gesture is given the notes as they were
/// read, each knowing the stretch of source it stands in, and hands back that stretch with the note spelled
/// again. The engine writes it and reads the tune afresh, which is what keeps one answer: a tree rewritten
/// in place carries the pitch worked out for the note before the change, so nothing could say truthfully
/// what it prints as. An edit named against a part still knows what it touched, so trouble afterwards can
/// be blamed on the keystroke that caused it rather than guessed at by diffing a string.
/// </para>
/// <para>
/// Plain typing is not here. A letter inserted at the caret needs no reshaping — ABC has no construct that
/// has to be bracketed when it grows — so the caller writes it where the caret is, exactly as the formula
/// editor does with a character its own tree did not have to reshape. What <see cref="NoteAt"/> offers is
/// only the question a write cannot answer for itself: which octave the new note belongs in.
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
    public static ContentChange? Octave(IReadOnlyList<ContentPart> notes, int by) =>
        by == 0 ? null : Respelled(notes, note => Moved(note, by));

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
    public static ContentChange? Accidental(IReadOnlyList<ContentPart> notes, int by) =>
        by == 0 ? null : Respelled(notes, note => Altered(note, by));

    /// <summary>
    /// Doubles or halves the written length of each of <paramref name="notes"/>: <c>A</c> → <c>A2</c> →
    /// <c>A4</c>, and back down through <c>A/2</c>, <c>A/4</c>. Dots survive, because the multiplier is
    /// scaled rather than replaced — <c>A3/2</c> doubles to <c>A3</c> and halves to <c>A3/4</c>.
    /// </summary>
    public static ContentChange? Length(IReadOnlyList<ContentPart> notes, int steps) =>
        steps == 0 ? null : Respelled(notes, note => Stretched(note, steps));

    /// <summary>
    /// What to type for a new note of <paramref name="letter"/> at <paramref name="caret"/>: the letter, in
    /// the octave the note before it was in, and for as long as that note lasted.
    ///
    /// <para>
    /// Both carried from the note before rather than fixed, and for the same reason — a melody typed left to
    /// right stays where the writer is looking. Typing <c>G</c> after <c>c'</c> means the G above it, not
    /// the G two octaves down, and typing it after a run of crotchets means another crotchet.
    /// </para>
    /// <para>
    /// The length is the one a reader notices. A bare letter is one unit note length, which for anything
    /// under three quarters to the bar is a <em>semiquaver</em> — so typing a note into a jig gave a
    /// semiquaver among its quavers, which is correct ABC and reads as a bug. Nothing about the tune says
    /// the writer wanted the shortest note the header allows; what they were just looking at does.
    /// </para>
    /// </summary>
    public static string NoteAt(ContentPart tune, int caret, char letter)
    {
        if (!AbcParser.IsNoteLetter(letter)) return letter.ToString();

        var previous = Before(tune, caret)?.Node;

        var octave = previous is { } sounding ? Sounding(sounding) : 5;
        var step = Pitch.Letters.IndexOf(char.ToUpperInvariant(letter));
        if (step < 0) return letter.ToString();

        return Spell(step, octave) + (previous?.Part(AbcRoles.Length)?.Print() ?? "");
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
        var sounds = (note as AbcEventNode)?.Pitch?.Alter ?? 0;
        var wanted = Math.Clamp(sounds + by, -2, 2);

        var mark = wanted switch { 2 => "^^", 1 => "^", -1 => "_", -2 => "__", _ => "=" };
        return With(note, AbcRoles.Accidental, ContentNode.Leaf(AbcKinds.Accidental, mark, AbcRoles.Accidental));
    }

    private static ContentNode? Stretched(ContentNode note, int steps)
    {
        var factor = AbcTheory.Factor(note.Part(AbcRoles.Length));

        for (var i = 0; i < Math.Abs(steps); i++)
            factor = steps > 0
                ? Duration.Of(factor.Numerator * 2, factor.Denominator)
                : Duration.Of(factor.Numerator, factor.Denominator * 2);

        // Past a breve on one side and a 64th on the other there is nothing left to write.
        if (factor.Numerator > 64 || factor.Denominator > 64) return null;

        return With(note, AbcRoles.Length, AbcParser.Length(Suffix(factor)));
    }

    /// <summary>How ABC writes a length multiplier: <c>2</c>, <c>/2</c>, <c>3/2</c>, and nothing for one.</summary>
    private static string Suffix(Duration factor)
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

        var rebuilt = With(note, AbcRoles.Letter, ContentNode.Leaf(AbcKinds.Letter, letter, AbcRoles.Letter));
        return rebuilt is null
            ? null
            : With(rebuilt, AbcRoles.Octave, marks.Length > 0 ? ContentNode.Leaf(AbcKinds.Octave, marks, AbcRoles.Octave) : null);
    }

    /// <summary>The letter and marks for a step in an octave — <c>C,,</c>, <c>C</c>, <c>c</c>, <c>c''</c>.</summary>
    private static string Spell(int step, int octave)
    {
        var letter = Pitch.Letters[step];

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
        && Pitch.Letters.IndexOf(char.ToUpperInvariant(letter[0])) is var step and >= 0
            ? step
            : null;

    /// <summary>The note the caret stands after, which is the one a key changing a note changes.</summary>
    internal static ContentPart? Before(ContentPart tune, int caret)
    {
        ContentPart? best = null;

        foreach (var part in tune.SelfAndDescendants())
        {
            if (part.Kind != AbcKinds.Note || part.Derived) continue;
            if (part.Part(AbcRoles.Letter) is null) continue;
            if (part.End > caret) continue;
            if (best is null || part.End > best.End) best = part;
        }

        return best;
    }

    // ── Rewriting the tree ──────────────────────────────────────────────────

    /// <summary>
    /// What <paramref name="change"/> makes of every note in <paramref name="notes"/>, as the stretch of source
    /// each note stands in given that note spelled again, and the caret after the last of them.
    /// <para>
    /// Null when nothing changed — a caller with rules of its own about plain typing wants to know, and a
    /// gesture that could not be carried out should not push an undo step. A note whose new spelling is the one
    /// already written changed nothing, so it is no write.
    /// </para>
    /// </summary>
    private static ContentChange? Respelled(IReadOnlyList<ContentPart> notes, Func<ContentNode, ContentNode?> change)
    {
        var writes = new List<ContentWrite>();

        foreach (var note in notes.Where(Editable).OrderBy(note => note.Start))
        {
            if (change(note.Node) is not { } respelled) continue;

            var spelling = respelled.Print();
            if (string.Equals(spelling, note.Print(), StringComparison.Ordinal)) continue;

            writes.Add(new ContentWrite(note, spelling));
        }

        if (writes.Count == 0) return null;

        // The caret stands in the source as it reads afterwards, so what the writes in front of it grew by counts.
        var grown = writes.Take(writes.Count - 1).Sum(write => write.Text.Length - write.Length);

        return new ContentChange(writes, writes[^1].Start + grown + writes[^1].Text.Length);
    }

    /// <summary>A note somebody wrote, with a letter to act on.</summary>
    private static bool Editable(ContentPart part) =>
        part.Kind == AbcKinds.Note && !part.Derived && part.Part(AbcRoles.Letter) is not null;

    /// <summary>
    /// The note with the part playing <paramref name="role"/> set to <paramref name="piece"/> — added where it had
    /// none, replaced where it had one, removed where there is no piece.
    /// <para>
    /// Where a new part goes is fixed by the notation rather than chosen: an accidental is written in
    /// front of the letter and everything else after it, which is the whole of the ordering rule.
    /// </para>
    /// </summary>
    private static ContentNode? With(ContentNode note, string role, ContentNode? piece)
    {
        var rebuilt = new List<ContentNode>(note.Children.Count + 1);
        var replaced = false;

        foreach (var child in note.Children)
        {
            if (child.Role != role) { rebuilt.Add(child); continue; }

            replaced = true;
            if (piece is not null) rebuilt.Add(piece);
        }

        if (!replaced && piece is not null)
        {
            var at = role == AbcRoles.Accidental
                ? 0
                : rebuilt.FindLastIndex(child => child.Role is AbcRoles.Accidental or AbcRoles.Letter
                                                              or AbcRoles.Octave) + 1;

            rebuilt.Insert(Math.Clamp(at, 0, rebuilt.Count), piece);
        }

        return note.With(rebuilt);
    }

    /// <summary>
    /// How a pause of a whole note is written in this tune: a <c>z</c>, and the multiplier that makes it a whole note
    /// against the length everything else in the tune is written in multiples of.
    /// </summary>
    internal static string Pause(ContentPart tune)
    {
        var unit = Unit(tune);

        return "z" + Suffix(Duration.Of(unit.Denominator, unit.Numerator));
    }

    /// <summary>
    /// The length a tune's notes are written in multiples of: what its <c>L:</c> says, or what its <c>M:</c> implies where it
    /// says nothing, or the eighth ABC falls back to where it has neither.
    /// </summary>
    private static Duration Unit(ContentPart tune)
    {
        if (Figures(tune, "L:") is [var over, var under] && under > 0) return Duration.Of(over, under);
        if (Figures(tune, "M:") is [var beats, var unit] && unit > 0) return AbcTheory.UnitFor(beats, unit);

        return Duration.Of(1, 8);
    }

    /// <summary>The numbers written on the first <paramref name="field"/> line of <paramref name="tune"/>.</summary>
    private static int[] Figures(ContentPart tune, string field) =>
        [.. tune.SelfAndDescendants()
                .Where(part => part.Kind == AbcKinds.Field && part.Part(Roles.Name)?.Text == field)
                .Take(1)
                .SelectMany(line => line.SelfAndDescendants())
                .Where(part => part.Kind == Kinds.Number && int.TryParse(part.Text, out _))
                .Select(part => int.Parse(part.Text))];
}
