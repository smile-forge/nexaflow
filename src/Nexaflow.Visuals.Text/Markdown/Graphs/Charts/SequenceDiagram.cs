namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>Line texture of a sequence message (solid <c>-&gt;</c> vs. dashed <c>--&gt;</c>).</summary>
public enum SequenceLineStyle { Solid, Dashed }

/// <summary>Head drawn at the receiving end of a sequence message.</summary>
public enum SequenceArrowHead
{
    None,    // ->   / -->        (plain line, no head)
    Filled,  // ->>  / -->>       (solid triangle — synchronous)
    Open,    // -)   / --)        (open V — asynchronous)
    Cross,   // -x   / --x        (× — lost / destroy)
}

/// <summary>UML actor role for a participant, from <c>actor</c> or <c>@{ "type": … }</c>.</summary>
public enum ParticipantKind { Participant, Actor, Boundary, Control, Entity, Database, Collections, Queue }

/// <summary>Where a note sits relative to its participant(s).</summary>
public enum NotePlacement { RightOf, LeftOf, Over }

/// <summary>The block constructs that wrap a span of messages.</summary>
public enum FragmentKind { Alt, Opt, Loop, Par, Break, Critical, Rect }

/// <summary>The three points of a fragment block in the linear item stream.</summary>
public enum FragmentBoundary { Begin, Section, End }

/// <summary>A lifeline column in a sequence diagram.</summary>
public sealed class SequenceParticipant
{
    public required string Id { get; init; }   // alias referenced by messages
    public string Label { get; set; } = string.Empty;
    public ParticipantKind Kind { get; set; } = ParticipantKind.Participant;
    public bool Created   { get; set; }         // introduced mid-diagram via 'create'
    public bool Destroyed { get; set; }         // ended via 'destroy'

    /// <summary>
    /// When set, the lifeline's head is drawn as a C4 element card (title, stereotype, description)
    /// instead of the plain box or actor glyph — this is what makes one renderer serve both a native
    /// <c>sequenceDiagram</c> and a <c>C4Sequence</c>. Null for every native participant, so the
    /// two only differ in how the head is painted, never in how the timeline is laid out.
    /// </summary>
    public C4ElementInfo? Card { get; set; }
}

/// <summary>Base type for anything on the diagram's vertical timeline.</summary>
public abstract class SequenceItem { }

/// <summary>One message arrow between two lifelines (or a self-message when From == To).</summary>
public sealed class SequenceMessage : SequenceItem
{
    public required string FromId { get; init; }
    public required string ToId   { get; init; }
    public string Text { get; set; } = string.Empty;
    public SequenceLineStyle Line { get; set; } = SequenceLineStyle.Solid;
    public SequenceArrowHead Head { get; set; } = SequenceArrowHead.Filled;
    public bool Bidirectional    { get; set; }  // <<->>  /  <<-->>  → heads at both ends
    public bool DotSource        { get; set; }  // '()' on the sender end   → dot at the source
    public bool DotTarget        { get; set; }  // '()' on the receiver end → dot at the target
    public bool ActivateTarget   { get; set; }  // '+' after the arrow → activate the target
    public bool DeactivateSource { get; set; }  // '-' after the arrow → deactivate the sender
    public int? Number           { get; set; }  // autonumber sequence number, if enabled

    /// <summary>The <c>$techn</c> of a C4 relationship, drawn as a smaller muted <c>[HTTPS]</c> line
    /// under the message text. Null on a native message, which then keeps its single label line.</summary>
    public string? Technology    { get; set; }

    /// <summary>Explicit arrow colour (a C4 <c>UpdateRelStyle</c>/<c>AddRelTag</c>); null keeps the theme's.</summary>
    public string? LineColor     { get; set; }

    /// <summary>Explicit label colour; null keeps the theme's.</summary>
    public string? TextColor     { get; set; }
}

/// <summary>A free-floating note beside, or spanning, one or more lifelines.</summary>
public sealed class SequenceNote : SequenceItem
{
    public NotePlacement Placement { get; set; }
    public List<string> ParticipantIds { get; } = [];
    public string Text { get; set; } = string.Empty;
}

/// <summary>An explicit <c>activate</c> / <c>deactivate</c> on a lifeline.</summary>
public sealed class SequenceActivation : SequenceItem
{
    public required string ParticipantId { get; init; }
    public bool Activate { get; init; }   // true = activate, false = deactivate
}

/// <summary>A <c>destroy X</c> marker — the lifeline ends with an ✗ at this point.</summary>
public sealed class SequenceDestroy : SequenceItem
{
    public required string ParticipantId { get; init; }
}

/// <summary>
/// One boundary of a fragment block (<c>alt/opt/loop/par/break/critical/rect</c>).
/// <see cref="FragmentBoundary.Begin"/> opens it, <see cref="FragmentBoundary.Section"/> is an
/// <c>else</c>/<c>and</c>/<c>option</c> divider, <see cref="FragmentBoundary.End"/> closes it.
/// </summary>
public sealed class SequenceFragment : SequenceItem
{
    public FragmentBoundary Boundary { get; init; }
    public FragmentKind Kind { get; init; }     // meaningful on Begin
    public string Label { get; set; } = string.Empty;
    public string? Color { get; set; }          // rect fill colour (e.g. "rgb(191,223,255)")
}

/// <summary>A <c>box … end</c> grouping that tints a contiguous run of participants.</summary>
public sealed class SequenceBox
{
    public string Label { get; set; } = string.Empty;
    public string? Color { get; set; }
    public List<string> ParticipantIds { get; } = [];
}

/// <summary>
/// Data model for a Mermaid <c>sequenceDiagram</c>.  Participants are ordered by first
/// appearance (explicit declarations win position); the <see cref="Items"/> timeline keeps
/// messages, notes, activations and fragment boundaries in source order.  Independent of the
/// graph/Sugiyama pipeline — sequence layout is its own renderer.
/// </summary>
public sealed class SequenceDiagram
{
    public string Title { get; set; } = string.Empty;
    public List<SequenceParticipant> Participants { get; } = [];
    public List<SequenceItem>        Items        { get; } = [];
    public List<SequenceBox>         Boxes        { get; } = [];

    /// <summary>
    /// Whether the participant heads are repeated at the bottom of the diagram. Mermaid always
    /// repeats them, so this defaults to true and a native diagram is unaffected; C4-PlantUML's
    /// <c>SHOW_FOOT_BOXES(false)</c> turns them off.
    /// </summary>
    public bool ShowFootBoxes { get; set; } = true;

    /// <summary>The messages on the timeline, in order (convenience view over <see cref="Items"/>).</summary>
    public IReadOnlyList<SequenceMessage> Messages => Items.OfType<SequenceMessage>().ToList();

    public SequenceParticipant? Find(string id) =>
        Participants.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    /// <summary>Returns the participant with <paramref name="id"/>, creating it if absent.</summary>
    public SequenceParticipant GetOrAdd(string id, string? label = null)
    {
        var p = Find(id);
        if (p is null)
        {
            p = new SequenceParticipant { Id = id, Label = label ?? id };
            Participants.Add(p);
        }
        else if (label is not null)
        {
            p.Label = label;
        }
        return p;
    }
}
