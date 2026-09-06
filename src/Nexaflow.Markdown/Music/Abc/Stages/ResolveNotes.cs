using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.Abc.Stages;

/// <summary>
/// Works out what every note actually sounds and how long every event lasts, and hangs both on them.
///
/// <para>
/// Neither is written down. A bare <c>F</c> is an F sharp in G major, and an F natural if something
/// earlier in the bar said so; an <c>A2</c> is a quarter note under <c>L:1/8</c> and a half note under
/// <c>L:1/4</c>; and either of them inside a triplet lasts two thirds of what it says. All of that is a
/// fact about the surroundings, which is exactly what a stage is for — worked out once, in the order the
/// tune was written, rather than re-derived by everything downstream out of state it would have to keep
/// for itself.
/// </para>
/// <para>
/// Two passes over each line, because <strong>a broken rhythm reaches backwards</strong>. The <c>&gt;</c>
/// of <c>A&gt;B</c> dots the note before it and halves the note after, so the first note's length is not
/// known until the marker after it has been read. A single pass would have to rewrite a node it had
/// already rebuilt; counting the events first and attaching afterwards is the same walk twice and no
/// bookkeeping.
/// </para>
/// </summary>
public sealed class ResolveNotes : IAstStage
{
    public string Name => "abc:notes";

    public ContentNode Run(ContentNode tree)
    {
        if (tree.Kind != AbcKinds.Tune) return tree;

        var lines = new List<ContentNode>(tree.Children.Count);
        var moved = false;

        foreach (var line in tree.Children)
        {
            if (line.Kind != AbcKinds.Line) { lines.Add(line); continue; }

            var told = Line(line);
            moved |= !ReferenceEquals(told, line);
            lines.Add(told);
        }

        return moved ? tree.With(lines) : tree;
    }

    private static ContentNode Line(ContentNode line)
    {
        var context = ResolveContext.Of(line);

        var facts = new List<(AbcPitch? Pitch, AbcLength Length, AbcLength Written)>();
        Read(line, context, AbcLength.Of(1, 1), facts);

        if (facts.Count == 0) return line;

        var at = 0;
        return Attach(line, facts, ref at);
    }

    // ── Pass one: read the tune in the order it was written ─────────────────

    /// <summary>
    /// Walks a line in written order, working out what each event sounds and how long it lasts. Every
    /// answer lands in <paramref name="facts"/> in the same order the second pass will meet the events,
    /// which is what lets the second pass be a plain walk with a counter.
    /// </summary>
    private static void Read(ContentNode node, AbcContext context, AbcLength scale,
                             List<(AbcPitch? Pitch, AbcLength Length, AbcLength Written)> facts)
    {
        // Accidentals last a bar, and only within a bar: the state is per measure, so it starts here and
        // is not carried out again.
        var sounding = new Dictionary<int, int>();
        Walk(node, ref context, scale, sounding, facts);
    }

    private static void Walk(ContentNode node, ref AbcContext context, AbcLength scale,
                             Dictionary<int, int> sounding, List<(AbcPitch?, AbcLength, AbcLength)> facts)
    {
        var broken = 0;

        foreach (var child in node.Children)
        {
            switch (child.Kind)
            {
                case AbcKinds.InlineField when child.Part(Roles.Name)?.Text is { Length: 2 } name:
                    context = Inline(name[0], child.Part(AbcRoles.Value)?.Text ?? "", context);
                    continue;

                case AbcKinds.Measure:
                {
                    // A fresh bar forgets what was altered in the last one.
                    var bar = new Dictionary<int, int>();
                    Walk(child, ref context, scale, bar, facts);
                    continue;
                }

                case AbcKinds.TupletGroup:
                {
                    var (notes, time) = GroupTuplets.Of(child);
                    var inner = notes > 0 && time > 0
                        ? scale * AbcLength.Of(time, notes)
                        : scale;

                    Walk(child, ref context, inner, sounding, facts);
                    continue;
                }

                case AbcKinds.Beam:
                case AbcKinds.Grace:
                    Walk(child, ref context, scale, sounding, facts);
                    continue;

                case AbcKinds.Broken:
                    broken = child.Text.StartsWith('>') ? child.Text.Length : -child.Text.Length;
                    continue;

                case AbcKinds.Note:
                    facts.Add(Sounds(child, context, scale, sounding));
                    Break(facts, ref broken);
                    continue;

                case AbcKinds.Chord:
                {
                    // A chord's own length is the bracket's suffix, and its members carry their own; the
                    // chord is what takes the time, so it is the one that is read.
                    foreach (var member in child.Children)
                        if (member.Kind == AbcKinds.Note)
                            facts.Add(Sounds(member, context, scale, sounding));

                    facts.Add((null, Lasts(child, context, scale), Lasts(child, context, One)));
                    Break(facts, ref broken);
                    continue;
                }

                case AbcKinds.Rest:
                    facts.Add((null, Rests(child, context, scale), Rests(child, context, One)));
                    Break(facts, ref broken);
                    continue;

                default:
                    if (child.Children.Count > 0) Walk(child, ref context, scale, sounding, facts);
                    continue;
            }
        }
    }

    /// <summary>
    /// A broken rhythm, applied to the pair it stands between. The marker was read before this event, so
    /// by the time the event has been added both halves of the pair are in hand — which is the whole
    /// reason for reading the line before rewriting it.
    /// </summary>
    private static void Break(List<(AbcPitch? Pitch, AbcLength Length, AbcLength Written)> facts, ref int broken)
    {
        if (broken == 0 || facts.Count < 2) { broken = 0; return; }

        var steps = Math.Min(Math.Abs(broken), 3);
        var dotted = Dot(steps);
        var halved = AbcLength.Of(1, 1L << steps);

        // Which of the pair grows is which way the marker points. The one that shrinks keeps a stub, and
        // between them they still last exactly as long as the two notes did.
        var (longer, shorter) = broken > 0 ? (facts.Count - 2, facts.Count - 1) : (facts.Count - 1, facts.Count - 2);

        // A broken rhythm changes what is written as well as what is heard: the pair really is a dotted
        // note and a short one, and that is how both are drawn.
        facts[longer] = (facts[longer].Pitch, facts[longer].Length * dotted, facts[longer].Written * dotted);
        facts[shorter] = (facts[shorter].Pitch, facts[shorter].Length * halved, facts[shorter].Written * halved);
        broken = 0;
    }

    /// <summary>What n dots multiply a length by: 3/2, then 7/4, then 15/8.</summary>
    private static AbcLength Dot(int dots) => AbcLength.Of((1L << (dots + 1)) - 1, 1L << dots);

    /// <summary>What a note sounds and how long it lasts.</summary>
    private static (AbcPitch? Pitch, AbcLength Length, AbcLength Written) Sounds(
        ContentNode note, AbcContext context, AbcLength scale, Dictionary<int, int> sounding)
    {
        if (note.Part(AbcRoles.Letter)?.Text is not { Length: 1 } letter)
            return (null, AbcLength.Zero, AbcLength.Zero);

        var step = AbcTheory.StepLetters.IndexOf(char.ToUpperInvariant(letter[0]));
        if (step < 0) return (null, AbcLength.Zero, AbcLength.Zero);

        var octave = char.IsUpper(letter[0]) ? 4 : 5;
        foreach (var mark in note.Part(AbcRoles.Octave)?.Text ?? "")
            octave += mark == '\'' ? 1 : -1;

        // Written, then what an earlier note in this bar altered, then what the key says. In that order,
        // because each overrides the one after it, and only the first is written on this note.
        var voice = (octave * 7) + step;
        int alter;

        if (note.Part(AbcRoles.Accidental)?.Text is { Length: > 0 } written)
        {
            alter = written[0] switch
            {
                '^' => written.Length,
                '_' => -written.Length,
                _ => 0,
            };
            sounding[voice] = alter;
        }
        else if (sounding.TryGetValue(voice, out var held))
        {
            alter = held;
        }
        else
        {
            alter = AbcTheory.KeyAlterFor(step, context.Fifths);
        }

        return (new AbcPitch(step, Math.Clamp(alter, -2, 2), octave),
                Lasts(note, context, scale),
                Lasts(note, context, One));
    }

    /// <summary>No scaling at all - what a note would last if nothing were compressing it.</summary>
    private static readonly AbcLength One = new(1, 1);

    private static AbcLength Lasts(ContentNode node, AbcContext context, AbcLength scale) =>
        context.Unit * AbcTheory.Factor(node.Part(AbcRoles.Length)?.Text) * scale;

    /// <summary>
    /// How long a rest lasts. <c>Z</c> is a whole bar however long the bar is, and its multiplier counts
    /// bars rather than unit lengths — which is the one place ABC measures something in bars.
    /// </summary>
    private static AbcLength Rests(ContentNode rest, AbcContext context, AbcLength scale)
    {
        var letter = rest.Part(Roles.Name)?.Text ?? "";
        var factor = AbcTheory.Factor(rest.Part(AbcRoles.Length)?.Text);

        return letter == "Z" ? context.Bar * factor : context.Unit * factor * scale;
    }

    private static AbcContext Inline(char letter, string value, AbcContext context) =>
        char.ToUpperInvariant(letter) switch
        {
            'K' => context with { Fifths = AbcTheory.Fifths(value) ?? context.Fifths },
            'M' => AbcTheory.Meter(value) is { } meter
                ? context with { Beats = meter.Beats, BeatUnit = meter.Unit }
                : context,
            'L' => AbcTheory.UnitLength(value) is { } unit ? context with { Unit = unit } : context,
            'V' => context with { Voice = value.Split([' ', '\t'], 2)[0].Trim() },
            _ => context,
        };

    // ── Pass two: hang the answers where they belong ────────────────────────

    private static ContentNode Attach(ContentNode node,
                                      List<(AbcPitch? Pitch, AbcLength Length, AbcLength Written)> facts,
                                      ref int at)
    {
        if (node.Kind == AbcKinds.Chord)
        {
            var members = new List<ContentNode>(node.Children.Count);
            foreach (var child in node.Children)
                members.Add(child.Kind == AbcKinds.Note ? Told(child, facts, ref at) : child);

            var chord = node.With(members);
            return at < facts.Count ? Length(chord, facts[at++]) : chord;
        }

        if (node.Kind is AbcKinds.Note) return Told(node, facts, ref at);
        if (node.Kind is AbcKinds.Rest) return at < facts.Count ? Length(node, facts[at++]) : node;

        if (node.IsLeaf) return node;

        var rebuilt = new List<ContentNode>(node.Children.Count);
        var moved = false;

        foreach (var child in node.Children)
        {
            var seen = Attach(child, facts, ref at);
            moved |= !ReferenceEquals(seen, child);
            rebuilt.Add(seen);
        }

        return moved ? node.With(rebuilt) : node;
    }

    private static ContentNode Told(ContentNode note,
                                    List<(AbcPitch? Pitch, AbcLength Length, AbcLength Written)> facts,
                                    ref int at)
    {
        if (at >= facts.Count) return note;

        var fact = facts[at++];
        return fact.Pitch is { } sounds
            ? note.Saying(
                (AbcKinds.Note, AbcRoles.Pitch, sounds.ToString()),
                (AbcKinds.Length, AbcRoles.Duration, fact.Length.ToString()),
                (AbcKinds.Length, AbcRoles.Written, fact.Written.ToString()))
            : note;
    }

    private static ContentNode Length(ContentNode node, (AbcPitch? Pitch, AbcLength Length, AbcLength Written) fact) =>
        node.Saying(
            (AbcKinds.Length, AbcRoles.Duration, fact.Length.ToString()),
            (AbcKinds.Length, AbcRoles.Written, fact.Written.ToString()));

    // ── Reading the answers back ────────────────────────────────────────────

    /// <summary>What this note sounds, or null where it was never a note.</summary>
    public static AbcPitch? PitchOf(ContentNode note) =>
        note.Said(AbcRoles.Pitch) is { } text ? AbcPitch.Parse(text) : null;

    /// <summary>How long this event lasts, in quarter notes.</summary>
    public static AbcLength LengthOf(ContentNode node) =>
        node.Said(AbcRoles.Duration) is { } text ? AbcLength.Parse(text) : AbcLength.Zero;

    /// <summary>
    /// The value this event is <em>written</em> as, which is not always how long it lasts. A triplet
    /// eighth sounds for a third of a quarter and is drawn as an eighth - the number over the group says
    /// the rest - so choosing a note head from what it sounds draws a dotted sixteenth, which is wrong.
    /// </summary>
    public static AbcLength WrittenOf(ContentNode node) =>
        node.Said(AbcRoles.Written) is { } text ? AbcLength.Parse(text) : LengthOf(node);
}
