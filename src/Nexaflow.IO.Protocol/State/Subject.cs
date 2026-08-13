using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Transforms;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;

namespace Nexaflow.IO.Protocol.State;

/// <summary>Which way a message went, from the point of view of the machine running this.</summary>
public enum Bearing
{
    Sent,
    Received,
}

/// <summary>
/// How sure a view is.
///
/// <para>
/// The distinction the whole two-party model exists for. Sending a request tells you what <i>you</i> now
/// believe; what the peer believes is a guess until something comes back. A model with one shared state
/// cannot tell "the request is outstanding" from "the request arrived", and those differ by exactly the
/// case every retry, timeout and idempotence rule is about.
/// </para>
/// </summary>
public enum Confidence
{
    /// <summary>Observed. This party's own view of its own actions.</summary>
    Known,

    /// <summary>Inferred. Our view of what the peer probably thinks, which a lost packet makes wrong.</summary>
    Presumed,
}

/// <summary>Someone holding a view of the conversation. Two, usually; a routed protocol has more.</summary>
public sealed class Party : Node
{
    public required string Id { get; init; }

    public override string Name => Id;

    public string About { get; init; } = "";
}

/// <summary>One state a conversation can be in, from one party's point of view.</summary>
public sealed class Phase : Node
{
    public required string Id { get; init; }

    public override string Name => Id;

    public string About { get; init; } = "";
}

/// <summary>
/// A named place on a subject where something learned is kept.
///
/// <para>
/// What goes in it is the protocol's business and <b>not the engine's</b>. The engine does not know that
/// a window size bounds anything or that a session key is secret; it knows that a message said to put this
/// there. Anything else would be the engine having opinions about a protocol it is supposed to have no
/// knowledge of.
/// </para>
/// </summary>
public sealed class Recall : Node
{
    public required string Id { get; init; }

    public override string Name => Id;

    public string About { get; init; } = "";
}

/// <summary>
/// One value a transition takes out of the message that caused it and keeps.
/// </summary>
/// <param name="From">Where in the message it comes from — an expression, so a value can be picked out of
/// a bit run or computed from several.</param>
/// <param name="Into">Where it is kept.</param>
/// <param name="Via">A converter on the way, where the wire form is not the useful form.</param>
/// <param name="Through">A document-authored transform, for the same reason a field has one: an encoding
/// rule belonging to one protocol reaches its state without the engine learning it.</param>
/// <remarks>
/// There is deliberately no confidence here. A phase can be presumed, because believing the peer received
/// something is a guess; a recording is attached to a message that actually happened and says what the
/// specification says to keep, so it is simply a fact. Putting a confidence on it would invite recording
/// what we hope rather than what we saw.
/// </remarks>
public sealed record Recording(Expr From, Recall Into, Conversion? Via = null, Transform? Through = null);

/// <summary>
/// A value a move requires to be in a set before it can happen.
/// </summary>
/// <remarks>
/// Not strictly necessary — the moves that exist are already the only ones allowed, so a guard adds no
/// power. What it adds is somewhere to put a restriction the specification states, in the specification's
/// own terms, next to the set that says why those values and not others. It also cross-checks the machine:
/// a move guarded by values the message can never carry is a mistake about one of the two, and the engine
/// can say so before a packet arrives.
/// </remarks>
public sealed record Guard(Field Reading, ValueSet Within);

/// <summary>
/// A message moving one party's view from one phase to another.
///
/// <para>
/// A node rather than an entry in a table, for the same reason a rule is one: it has several participants
/// — a message, a direction, whose view moves, from where, to where — and none of them is more the subject
/// than the others. Written as a property bag on a phase, "which party" quietly becomes an attribute of a
/// state rather than a thing the transition is about.
/// </para>
///
/// <para>
/// One message on the wire usually produces <b>two</b> of these: what the sender now knows, and what it
/// presumes about the receiver. They are separate because they can be separately wrong.
/// </para>
/// </summary>
public sealed class Transition : Node
{
    /// <summary>Whose view this moves.</summary>
    public required Party Whose { get; init; }

    /// <summary>Where the view has to be. <b>Null means anywhere</b> — switching a light on is legal
    /// whether or not it was already on, and saying so is a declaration rather than an omission.</summary>
    public Phase? From { get; init; }

    public required Phase To { get; init; }

    /// <summary>The document whose messages can cause it.</summary>
    public required MessageDef On { get; init; }

    /// <summary>Which of that document's messages, read against the decode. One document can describe a
    /// request and its acknowledgement, and they are not the same event.</summary>
    public required Expr When { get; init; }

    public required Bearing Way { get; init; }

    public Confidence Confidence { get; init; } = Confidence.Known;

    /// <summary>What the message has to be carrying, beyond matching <see cref="When"/>. Each guard is a
    /// field and the set its value must be in, so the restriction reads as the specification states it and
    /// carries the set's own reason into any refusal.</summary>
    public IReadOnlyList<Guard> Only { get; init; } = [];

    /// <summary>What this move takes out of the message and keeps. The protocol says what to put where;
    /// the engine moves it and forms no view about it.</summary>
    public IReadOnlyList<Recording> Records { get; init; } = [];

    /// <summary>
    /// Whether this move is the end of the instance.
    ///
    /// <para>
    /// Declared, because lifetime is the protocol's and not the engine's. A BACnet invoke id is free again
    /// once the transaction has an outcome and gets reused immediately; a session key lasts until the
    /// connection does; a device mode outlives everything and is only ever overwritten. Nothing general is
    /// true of all three, so nothing general is assumed.
    /// </para>
    /// </summary>
    public bool Ends { get; init; }

    /// <summary>Why this is a legal move, carried into the refusal when something else is attempted.</summary>
    public required string Because { get; init; }

    public override string Name => $"{Whose.Id}: {From?.Id ?? "anywhere"} → {To.Id}";

    public override string ToString() => Name;
}

/// <summary>
/// Something that has states: what they are, who holds a view of them, and what moves between them.
///
/// <para>
/// <b>Not necessarily a conversation.</b> Telling a light to switch on defines a belief about the light,
/// and there is no exchange, no correlation and nothing to pair the message with — the state is about a
/// thing in the world, and a message moves it. Making an exchange the centre of the model would have made
/// that the awkward case and request/response the natural one, which is backwards: the conversation is a
/// subject that happens to be identified by a number in the packet.
/// </para>
/// </summary>
public sealed class Subject : Node
{
    public required string Id { get; init; }

    public override string Name => Id;

    public required Phase Start { get; init; }

    public required IReadOnlyList<Party> Parties { get; init; }

    public required IReadOnlyList<Transition> Transitions { get; init; }

    /// <summary>
    /// What tells one of these apart from another, when there is more than one.
    ///
    /// <para>
    /// <b>Null when there is only one.</b> A light has one power state; a device has one mode. Nothing in
    /// the message says which light, because there is only the one this document is talking to.
    /// </para>
    ///
    /// <para>
    /// Where there are many, it is a concept and not a field. Up to 255 BACnet transactions run at once
    /// between one pair of machines and nothing separates them but a number in the packet — a number
    /// called <c>invokeId</c> in a request and three other things elsewhere. What they share is the
    /// <i>idea</i>, so that is what this points at.
    /// </para>
    /// </summary>
    public Concept? Distinguishes { get; init; }

    public string About { get; init; } = "";

    /// <summary>
    /// The state model as nodes and relationships: which message triggers what, which way it has to be
    /// going, and what each move keeps.
    ///
    /// <para>
    /// The state layer was the one part of the model built as properties rather than edges, which made it
    /// the one part nothing could be asked about. "What does receiving this message do?" is now a query
    /// rather than a scan.
    /// </para>
    /// </summary>
    public ProtocolGraph Graph => _graph ??= Build();
    private ProtocolGraph? _graph;

    private ProtocolGraph Build()
    {
        var graph = new ProtocolGraph();

        graph.Add(this);
        foreach (var party in Parties) graph.Add(party);

        foreach (var transition in Transitions)
        {
            graph.Add(new Triggers { From = transition.On.Root, To = transition, Way = transition.Way });
            graph.Add(new Moves { From = transition, To = transition.To, Entering = true });

            if (transition.From is { } from) graph.Add(new Moves { From = transition, To = from });

            graph.Add(new Viewed { From = transition, To = transition.Whose });

            foreach (var recording in transition.Records)
                graph.Add(new Remembers { From = transition, To = recording.Into });
        }

        return graph;
    }

    /// <summary>What a message does, asked of the graph. Every slot it can write, for a reviewer who wants
    /// to know what talking to this device leaves behind.</summary>
    public IReadOnlyList<Recall> Keeps => [.. Graph.Of<Remembers>().Select(e => (Recall)e.To).Distinct()];

    /// <summary>Document-time checks. Cheap, and they catch a state machine that cannot be entered or one
    /// with a state nothing reaches.</summary>
    /// <summary>
    /// What a move's expression may name, checked against what its scope actually binds.
    ///
    /// <para>
    /// The last place the vocabulary table did not reach, and the omission had the shape it always has: a
    /// move is read against the decoded captures alone, so a condition naming <c>inputs</c>, <c>present</c>
    /// or a region's <c>.extent</c> resolves to nothing and makes every comparison against it quietly
    /// false. A state machine that silently never fires is worse than one that refuses, because the
    /// refusal at least happens.
    /// </para>
    ///
    /// <para>
    /// The field named is deliberately <b>not</b> required to be one the message carries. A move asking
    /// about a part that may not have arrived is the ordinary case — a connection stays open unless a
    /// header said otherwise — and reading absent as absent is what makes that expressible.
    /// </para>
    /// </summary>
    private void Reads(Transition transition, Expr expression, string what, List<string> issues)
    {
        foreach (var root in Vocabulary.RootsOf(expression))
        {
            if (Vocabulary.Available(ExprSite.Moving).Contains(root)) continue;

            issues.Add(Vocabulary.All.Contains(root)
                ? $"subject '{Id}': {what} '{transition}' names `{root}`, and "
                + $"{Vocabulary.Why(root, ExprSite.Moving)}."
                : $"subject '{Id}': {what} '{transition}' names `{root}`, which is not part of the walk's "
                + "vocabulary at all. A root nothing binds reads as nothing and makes every comparison "
                + $"against it false. The ones that exist are: {string.Join(", ", Vocabulary.All.Order())}.");
        }

        // Matched against the expression rather than walked out of the graph, because a move's
        // expressions are not in it: a transition points at a message, and what it computes hangs off the
        // transition, which has no computation edge yet. When it gets one this becomes the same walk
        // everything else uses.
        foreach (var read in expression.Descendants().OfType<Expr.Member>())
            if (read is { Target: Expr.Member { Target: Expr.Root { Name: "fields" } } named }
                && !read.Name.Equals("value", StringComparison.Ordinal))
                issues.Add($"subject '{Id}': {what} '{transition}' reads the {read.Name} of "
                         + $"'{named.Name}'. A move sees what the message said, not how it was laid out — "
                         + "only `.value` answers here, and anything else reads as nothing.");
    }

    public IReadOnlyList<string> Validate()
    {
        List<string> issues = [];

        if (Transitions.Count == 0) issues.Add($"subject '{Id}': nothing can happen in it");

        foreach (var transition in Transitions)
        {
            if (!Parties.Contains(transition.Whose))
                issues.Add($"subject '{Id}': {transition} moves the view of '{transition.Whose.Id}', who "
                         + "holds no view of this");

            if (string.IsNullOrWhiteSpace(transition.Because))
                issues.Add($"subject '{Id}': {transition} does not say why. A refusal that cannot say what "
                         + "it expected instead is barely better than none.");

            foreach (var guard in transition.Only)
            {
                if (!transition.On.AllFields.Contains(guard.Reading))
                    issues.Add($"subject '{Id}': {transition} is guarded on '{guard.Reading.Name}', which "
                             + $"is not a field of '{transition.On.Id}'");

                if (guard.Within.Members.Count == 0)
                    issues.Add($"subject '{Id}': {transition} is guarded on '{guard.Within.Id}', which "
                             + "admits nothing, so the move can never be taken");
            }

            if (Distinguishes is { } key && transition.On.NamedAll(key).Count == 0)
                issues.Add($"subject '{Id}': {transition} is on a message that has no '{key.Id}', so "
                         + "nothing says which one of these it is about");

            Reads(transition, transition.When, "the condition on", issues);

            foreach (var recording in transition.Records)
                Reads(transition, recording.From, $"what '{transition}' keeps in '{recording.Into.Id}',",
                      issues);
        }

        var reachable = new HashSet<Phase> { Start };
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (var t in Transitions)
                if ((t.From is null || reachable.Contains(t.From)) && reachable.Add(t.To)) grew = true;
        }

        foreach (var stranded in Transitions.Select(t => t.From).OfType<Phase>().Distinct()
                                            .Where(p => !reachable.Contains(p)))
            issues.Add($"subject '{Id}': nothing reaches '{stranded.Id}', so the moves out of it can never "
                     + "be taken — a state that cannot be entered is a description of something other "
                     + "than this protocol");

        return issues;
    }
}
