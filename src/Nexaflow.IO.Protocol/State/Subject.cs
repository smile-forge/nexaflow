using Nexaflow.IO.Protocol.Expressions;
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

    /// <summary>Document-time checks. Cheap, and they catch a state machine that cannot be entered or one
    /// with a state nothing reaches.</summary>
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

            if (Distinguishes is { } key && transition.On.NamedAll(key).Count == 0)
                issues.Add($"subject '{Id}': {transition} is on a message that has no '{key.Id}', so "
                         + "nothing says which one of these it is about");
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
