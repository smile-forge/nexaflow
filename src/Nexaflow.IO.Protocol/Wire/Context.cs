namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// A value that does not come from the octets: who is expected to have it, and what they call it.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a computation.</b> It produces a value and takes no inputs, which is exactly what a
/// <see cref="Constant"/> is — the only difference being where the answer comes from. So a field fed from
/// outside is fed by one edge saying so, and there is no expression in between reading the value back out
/// by name. That intermediate step was the same fact written twice: an edge saying which input, and an
/// expression naming it, either of which could be changed without the other.
/// </para>
/// <para>
/// <b>It has no key.</b> A node already has an id, and that is its name; a second one would be another
/// pair of things that can disagree. What it does carry is <see cref="As"/> — the specification's own term
/// — because the person setting a value has the RFC open and knows it as "Source Port". That is a label
/// for a human, not an identifier, and nothing in the graph resolves by it.
/// </para>
/// <para>
/// There are exactly two kinds, and the difference is who maintains the value: an <see cref="Input"/> is
/// handed in by whoever is driving the engine, and a <see cref="State"/> is carried between messages by
/// the protocol itself. A taxonomy finer than that — prompted, secret, ambient — described how a host
/// might <i>obtain</i> a value, which is a fact about the host and not about the protocol.
/// </para>
/// </remarks>
public abstract class Context : Computation
{
    /// <summary>What the specification calls it, for whoever is setting it.</summary>
    public string? As { get; init; }

    /// <summary>What this is, in words. Not decoration: for anything a person supplies, this is the
    /// question they get asked, and a protocol that cannot say why it wants a value has no business
    /// asking for one.</summary>
    public string Purpose { get; init; } = "";

    /// <summary>
    /// A value the caller provides.
    /// </summary>
    /// <remarks>
    /// Nothing is assumed about where the caller got it: a port chosen by an operating system, a payload
    /// handed down by an application, and an address belonging to the datagram underneath are the same
    /// thing from here — a value this protocol did not compute and cannot recover.
    /// </remarks>
    public sealed class Input : Context;

    /// <summary>
    /// A value the protocol keeps between messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what makes a conversation expressible rather than a sequence of unrelated messages: a
    /// sequence number that advances by what was sent, a next-expected number that advances by what
    /// arrived. None of it is on the wire of any single message, and all of it decides what the next
    /// message says.
    /// </para>
    /// <para>
    /// Distinct from an input because the two fail differently. A missing input is a caller that did not
    /// say something it had to; a missing state is a conversation starting, which is ordinary.
    /// </para>
    /// </remarks>
    public sealed class State : Context;
}
