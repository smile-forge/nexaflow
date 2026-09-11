using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.LilyPond.Stages;

/// <summary>
/// Works out what every note sounds, and hangs it on the note.
///
/// <para>
/// A LilyPond note's octave depends on how it was entered. Written plainly, <c>c</c> is the C below middle C and
/// each <c>'</c> raises it an octave. Inside <c>\relative</c> it is the C nearest the note before it, so a tune
/// can be typed without counting octaves; inside <c>\fixed c'</c> every note is in the octave named. Which of
/// those a note is in is a fact about what it was written inside, which is exactly what a stage can see.
/// </para>
/// <para>
/// A chord's notes are each measured from the one before, and the note after the chord from the chord's
/// <em>first</em> note — LilyPond's rule, and the reason <c>&lt;c e g&gt; g</c> goes down to the g below.
/// Grace notes are notes like any other here, and move the reference as they should.
/// </para>
/// <para>
/// <c>\transpose c d</c> moves everything inside it by the interval from one to the other, after it has been
/// read. The reference a <c>\relative</c> inside it measures from is the written note, which is how LilyPond
/// reads it too.
/// </para>
/// </summary>
public sealed class ResolvePitches : IAstStage
{
    public string Name => "lilypond:pitches";

    private enum Entry { Absolute, Relative, Fixed }

    /// <summary>How notes are being entered where the walk is.</summary>
    private readonly record struct Scope(Entry Entry, int Octave, int Steps, int Semitones);

    /// <summary>What the next note in relative entry is measured from, and the last chord for a <c>q</c>.</summary>
    private sealed class Carried
    {
        public Pitch Reference = Relative;
        public IReadOnlyList<Pitch> Chord = [];
    }

    /// <summary>
    /// Where a <c>\relative</c> with no start pitch measures from: the F below middle C, which is the one
    /// pitch that leaves the first note where writing it plainly would have put it.
    /// </summary>
    private static readonly Pitch Relative = new(3, 0, 3);

    public ContentNode Run(ContentNode tree) =>
        Walk(tree, new Scope(Entry.Absolute, 3, 0, 0), new Carried());

    private static ContentNode Walk(ContentNode node, Scope scope, Carried carried)
    {
        if (node.IsLeaf || node.IsDerived) return node;

        switch (node.Kind)
        {
            case LilyPondKinds.Note when IsPlayed(node):
                return Sounded(node, scope, carried);

            case LilyPondKinds.Chord when IsPlayed(node):
                return Chorded(node, scope, carried);

            case LilyPondKinds.ChordRepeat when IsPlayed(node):
                return carried.Chord.Count == 0
                    ? node
                    : node.Saying([.. carried.Chord.Select(p => (LilyPondKinds.Note, LilyPondRoles.Pitch, p.ToString()))]);

            case LilyPondKinds.Command:
                return Entered(node, scope, carried);
        }

        return Children(node, scope, carried);
    }

    /// <summary>What a command does to how the music inside it is entered, and the music, walked that way.</summary>
    private static ContentNode Entered(ContentNode command, Scope scope, Carried carried)
    {
        var pitches = command.Parts(LilyPondRoles.Argument).Where(a => a.Kind == LilyPondKinds.Note).ToList();

        switch (command.Part(Roles.Name)?.Text)
        {
            case @"\relative":
            {
                // A block of its own: what it measures from is its own, and a relative block inside another
                // is not moved by the one outside it. What the outer block measures from resumes after it.
                var outer = carried.Reference;
                carried.Reference = pitches.Count > 0 ? Plainly(pitches[0], 3) : Relative;

                var inside = Children(command, scope with { Entry = Entry.Relative }, carried);
                carried.Reference = outer;
                return inside;
            }

            case @"\fixed":
                return pitches.Count == 0
                    ? Children(command, scope, carried)
                    : Children(command, scope with { Entry = Entry.Fixed, Octave = Plainly(pitches[0], 3).Octave }, carried);

            case @"\absolute":
                return Children(command, scope with { Entry = Entry.Absolute, Octave = 3 }, carried);

            case @"\transpose" when pitches.Count >= 2:
            {
                var from = Plainly(pitches[0], 3);
                var to = Plainly(pitches[1], 3);

                return Children(command, scope with
                {
                    Steps = scope.Steps + (to.DiatonicIndex - from.DiatonicIndex),
                    Semitones = scope.Semitones + (to.Semitones - from.Semitones),
                }, carried);
            }

            default:
                return Children(command, scope, carried);
        }
    }

    private static ContentNode Children(ContentNode node, Scope scope, Carried carried)
    {
        List<ContentNode>? rebuilt = null;

        for (var i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];

            // What a command is handed is not played: \key g, \relative c', \transpose c d.
            if (child.Role == LilyPondRoles.Argument) continue;

            var seen = Walk(child, scope, carried);
            if (ReferenceEquals(seen, child)) continue;

            rebuilt ??= [.. node.Children];
            rebuilt[i] = seen;
        }

        return rebuilt is null ? node : node.With(rebuilt);
    }

    private static bool IsPlayed(ContentNode node) => node.Role != LilyPondRoles.Argument;

    /// <summary>A note, told what it sounds.</summary>
    private static ContentNode Sounded(ContentNode note, Scope scope, Carried carried) =>
        Placed(note, scope, carried) is { } pitch ? Told(note, pitch, scope) : note;

    /// <summary>
    /// A chord, each note measured from the one before it, and what comes next from its first.
    /// </summary>
    private static ContentNode Chorded(ContentNode chord, Scope scope, Carried carried)
    {
        var members = new List<ContentNode>(chord.Children.Count);
        var sounded = new List<Pitch>();
        Pitch? first = null;

        foreach (var child in chord.Children)
        {
            if (child.Kind != LilyPondKinds.Note || Placed(child, scope, carried) is not { } pitch)
            {
                members.Add(child);
                continue;
            }

            first ??= pitch;
            sounded.Add(Moved(pitch, scope));
            members.Add(Told(child, pitch, scope));
        }

        if (first is { } opening && scope.Entry == Entry.Relative) carried.Reference = opening;
        if (sounded.Count > 0) carried.Chord = sounded;

        return chord.With(members);
    }

    /// <summary>
    /// What a written note is, before any transposition: where its entry puts it, and — in relative entry —
    /// the note the next one is measured from.
    /// </summary>
    private static Pitch? Placed(ContentNode note, Scope scope, Carried carried)
    {
        if (note.Part(LilyPondRoles.NoteName)?.Text is not { } name || LilyPondTheory.Name(name) is not { } named)
            return null;

        var (step, alter) = named;

        var marks = Marks(note);

        switch (scope.Entry)
        {
            case Entry.Relative:
            {
                // The nearest note of that letter, then the marks: an interval of a fourth or less up or down,
                // counted in letters — whatever the accidentals, which is what makes c to fis a fourth.
                var from = carried.Reference.DiatonicIndex;
                var index = (carried.Reference.Octave * 7) + step;
                while (index - from > 3) index -= 7;
                while (from - index > 3) index += 7;
                index += 7 * marks;

                var pitch = new Pitch(step, alter, (index - step) / 7);
                carried.Reference = pitch;
                return pitch;
            }

            case Entry.Fixed:
                return new Pitch(step, alter, scope.Octave + marks);

            default:
                return new Pitch(step, alter, 3 + marks);
        }
    }

    /// <summary>A pitch written plainly, as the start of a <c>\relative</c> or a <c>\transpose</c> is.</summary>
    private static Pitch Plainly(ContentNode note, int octave)
    {
        var (step, alter) = LilyPondTheory.Name(note.Part(LilyPondRoles.NoteName)?.Text ?? "") ?? (0, 0);
        return new Pitch(step, alter, octave + Marks(note));
    }

    private static int Marks(ContentNode note) =>
        (note.Part(LilyPondRoles.Octave)?.Text ?? "").Sum(mark => mark == '\'' ? 1 : -1);

    private static Pitch Moved(Pitch pitch, Scope scope) =>
        scope.Steps == 0 && scope.Semitones == 0 ? pitch : pitch.Transposed(scope.Steps, scope.Semitones);

    private static ContentNode Told(ContentNode note, Pitch pitch, Scope scope) =>
        note.Saying(LilyPondKinds.Note, LilyPondRoles.Pitch, Moved(pitch, scope).ToString());

    // ── Reading the answers back ────────────────────────────────────────────

    /// <summary>What this note sounds, or null where it was never a note played.</summary>
    public static Pitch? PitchOf(ContentNode note) =>
        note.Said(LilyPondRoles.Pitch) is { } text ? Pitch.Parse(text) : null;

    /// <summary>What a repeated chord sounds: every pitch of the chord it repeats.</summary>
    public static IEnumerable<Pitch> PitchesOf(ContentNode node)
    {
        foreach (var derived in node.Children)
        {
            if (derived.Role != Roles.Derived) continue;

            foreach (var fact in derived.Children)
                if (fact.Role == LilyPondRoles.Pitch)
                    yield return Pitch.Parse(fact.Text);
        }
    }
}
