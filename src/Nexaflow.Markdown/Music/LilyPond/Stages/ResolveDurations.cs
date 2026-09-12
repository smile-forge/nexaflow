using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.LilyPond.Stages;

/// <summary>
/// Works out how long every event lasts, and hangs it on the event.
///
/// <para>
/// A LilyPond duration is written only when it changes: <c>c4 d e f8 g</c> is three quarters and two eighths.
/// So a note's length is a fact about everything written before it — in the order it was <em>written</em>, not
/// the order it is played, because that is how LilyPond's own reader carries it: a definition's notes take the
/// length the note before the definition left behind, wherever the definition is later used.
/// </para>
/// <para>
/// A dot is carried with its note, so <c>c4. d</c> is two dotted quarters. A multiplier — <c>R1*3</c> — is
/// not: it says how long one event lasts, not how the next is written.
/// </para>
/// <para>
/// Two facts go on each event. What it is <em>written</em> as, which decides its head and its flags; and how
/// long it <em>sounds</em>, after its multiplier and any tuplet it is inside, which decides where the bars fall.
/// A triplet eighth is drawn as an eighth and lasts a third of a quarter.
/// </para>
/// </summary>
public sealed class ResolveDurations : IAstStage
{
    public string Name => "lilypond:durations";

    /// <summary>What a written value is carried as until another is written.</summary>
    private sealed class Carried
    {
        public Duration Last = Duration.Quarter;
    }

    public ContentNode Run(ContentNode tree) => Walk(tree, new Carried(), Duration.Of(1, 1));

    private static ContentNode Walk(ContentNode node, Carried carried, Duration squeeze)
    {
        if (node.IsLeaf || node.IsDerived) return node;

        // A tuplet squeezes everything written inside it.
        squeeze *= Squeeze(node);

        var children = node.Children;
        List<ContentNode>? rebuilt = null;

        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];

            var seen = IsEvent(child) ? Timed(child, carried, squeeze, NamedAfter(children, i))
            
                     : IsSkip(child) ? Skipped(child, carried, squeeze)
                     : Walk(child, carried, squeeze);

            if (ReferenceEquals(seen, child)) continue;

            rebuilt ??= [.. children];
            rebuilt[i] = seen;
        }

        return rebuilt is null ? node : node.With(rebuilt);
    }

    /// <summary>
    /// Whether this piece takes time: a note, a rest, a chord, a repeated chord, a chord's name. Not a pitch handed to a
    /// command — <c>\relative c'</c> — and not a note inside a chord, whose chord is what lasts.
    /// </summary>
    private static bool IsEvent(ContentNode node) =>
        node.Kind is LilyPondKinds.Note or LilyPondKinds.Rest or LilyPondKinds.Chord or LilyPondKinds.ChordRepeat or LilyPondKinds.ChordName
        && node.Role is not (LilyPondRoles.Argument or LilyPondRoles.Note);

    private static bool IsSkip(ContentNode node) =>
        node.Kind == LilyPondKinds.Command && node.Part(Roles.Name)?.Text == @"\skip";

    /// <summary>An event, told how long it is written and how long it lasts.</summary>
    private static ContentNode Timed(ContentNode ev, Carried carried, Duration squeeze, Duration? named)
    {
        var (written, scale) = Written(ev.Part(LilyPondRoles.Duration)?.Text, carried, named);
        return Told(ev, written, written * scale * squeeze);
    }

    /// <summary>A <c>\skip 4</c>, which lasts what it says and prints nothing.</summary>
    private static ContentNode Skipped(ContentNode skip, Carried carried, Duration squeeze)
    {
        var length = skip.Parts(LilyPondRoles.Argument).FirstOrDefault()?.Text;
        var (written, scale) = Written(length, carried, named: null);
        return Told(skip, written, written * scale * squeeze);
    }

    /// <summary>
    /// What a duration writes, and what it multiplies that by — or, where none is written, what was carried.
    /// </summary>
    private static (Duration Written, Duration Scale) Written(string? text, Carried carried, Duration? named)
    {
        if (text is not null && LilyPondTheory.Length(text) is { } length)
        {
            carried.Last = length.Written;
            return length;
        }

        if (named is { } breve)
        {
            carried.Last = breve;
            return (breve, Duration.Of(1, 1));
        }

        return (carried.Last, Duration.Of(1, 1));
    }

    /// <summary>
    /// A <c>\breve</c> or a <c>\longa</c> after an event, which is its duration: LilyPond spells the durations
    /// longer than a whole note as commands rather than numbers.
    /// </summary>
    private static Duration? NamedAfter(IReadOnlyList<ContentNode> children, int at)
    {
        for (var i = at + 1; i < children.Count; i++)
        {
            if (children[i].Kind is Kinds.Space or Kinds.Comment) continue;
            if (children[i].Kind != LilyPondKinds.Command) return null;
            return LilyPondTheory.Named(children[i].Part(Roles.Name)?.Text ?? "");
        }

        return null;
    }

    /// <summary>
    /// What a <c>\tuplet 3/2</c> or a <c>\times 2/3</c> does to the time of what it holds — both say three in
    /// the time of two, one each way round.
    /// </summary>
    private static Duration Squeeze(ContentNode node)
    {
        if (node.Kind != LilyPondKinds.Command) return Duration.Of(1, 1);

        var name = node.Part(Roles.Name)?.Text;
        if (name is not (@"\tuplet" or @"\times")) return Duration.Of(1, 1);

        var fraction = node.Parts(LilyPondRoles.Argument)
            .Select(arg => LilyPondTheory.Fraction(arg.Text))
            .FirstOrDefault(f => f is not null);

        if (fraction is not { } found) return Duration.Of(1, 1);

        var (top, bottom) = found;
        return name == @"\tuplet" ? Duration.Of(bottom, top) : Duration.Of(top, bottom);
    }

    private static ContentNode Told(ContentNode node, Duration written, Duration sounds) =>
        node.Saying(
            (LilyPondKinds.Duration, LilyPondRoles.Written, written.ToString()),
            (LilyPondKinds.Duration, LilyPondRoles.Sounds, sounds.ToString()));

    // ── Reading the answers back ────────────────────────────────────────────

    /// <summary>How long this event lasts, in quarter notes, or nothing where it was never timed.</summary>
    public static Duration SoundsOf(ContentNode node) =>
        node.Said(LilyPondRoles.Sounds) is { } text ? Duration.Parse(text) : Duration.Zero;

    /// <summary>The value this event is written as, which is not always how long it lasts.</summary>
    public static Duration WrittenOf(ContentNode node) =>
        node.Said(LilyPondRoles.Written) is { } text ? Duration.Parse(text) : SoundsOf(node);
}
