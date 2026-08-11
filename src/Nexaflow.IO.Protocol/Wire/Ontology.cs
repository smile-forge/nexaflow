namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// Something the protocol has a name for, and the part of the message that is it.
///
/// <para>
/// The layer above shape. A wire description says where octets go; it cannot say that two of them are
/// <i>the same thing</i>, and there is one construct where that is the whole content of what is being
/// written down: a reference. A compression pointer does not mean "go to octet 23" — it means "the rest of
/// this is that name", and a name is a concept, not a byte range. Trying to say that with offsets is what
/// makes references need indices, occurrence plumbing and a way to designate the third thing the encoder
/// happened to emit.
/// </para>
///
/// <para>
/// The parts are <b>declarable</b>, and that is the point. What a message might carry is unbounded — every
/// domain name that has ever existed — but what a document has <i>places for</i> is finite and fixed the
/// moment it is written. Values arrive from outside and resolve at run; the parts do not move. So identity
/// is by declaration and nothing has to be indexed, counted or searched for.
/// </para>
///
/// <para>
/// A concept <b>does not compute</b>. It names one node and that is all it does, which is why it cannot
/// become a second description of the shape that drifts from the first: there is nothing in it to disagree
/// with. The structure remains the truth. A concept whose name is a poor description of what it points at
/// is confusing to read and changes nothing about what gets parsed or built.
/// </para>
/// </summary>
public sealed class Concept : Node
{
    public required string Id { get; init; }

    public override string Name => Id;

    /// <summary>
    /// The nodes that are this thing.
    ///
    /// <para>
    /// A list, because one idea can appear in more than one message. BACnet's invoke id is a single
    /// concept realised as four different fields — one in a request, one in an acknowledgement, one in a
    /// segment ack, one in an error — and what makes two messages part of the same exchange is that they
    /// agree about <i>it</i>, not that they happen to use the same field name. A concept per message would
    /// make that agreement inexpressible.
    /// </para>
    ///
    /// <para>
    /// Authoring input, which becomes one <see cref="Names"/> edge each — the edges are what the engine
    /// reads, as with a rule's order and an arm's key.
    /// </para>
    /// </summary>
    public required IReadOnlyList<Node> Of { get; init; }

    /// <summary>What it means, for whoever reads the document. Prose, and deliberately without effect.</summary>
    public string About { get; init; } = "";

    public override string ToString() => $"{Id} → {string.Join(", ", Of.Select(o => o.Name))}";
}
