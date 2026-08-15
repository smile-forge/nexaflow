using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// A converter applied on the way out and inverted on the way in — a scale, a text codec.
/// </summary>
/// <remarks>
/// <b>A name and nothing else.</b> Arguments used to sit here as literals, which was a second channel
/// beside the <see cref="Requires"/> edges for something the edges already do — and a poorer one, because
/// a literal cannot be a field read a moment ago or the answer to another calculation. An argument is an
/// edge naming the parameter it feeds.
/// </remarks>
public sealed record Conversion(string Name)
{
    /// <summary>So the common case still reads as <c>Via = "unascii"</c>. Null in, null out: a computed
    /// converter name that comes to nothing means <i>no conversion</i>, not a conversion with no
    /// name.</summary>
    public static implicit operator Conversion?(string? name) => name is null ? null : new(name);

    public override string ToString() => Name;
}

/// <summary>
/// One place in a message that becomes octets: what it is called, and how its value is laid down.
/// </summary>
/// <remarks>
/// <para>
/// <b>A field has a form, not a shape.</b> It used to have a <c>Pattern</c>, which was two unrelated things
/// wearing one name — a way a single value becomes octets, and a spelling for a structure made of other
/// fields. The second was only ever an instruction for building a graph, and once the graph is the thing
/// that is written down and read back there is nothing left for it to instruct. A region is a
/// <see cref="FieldSet"/>, a branch is a <see cref="Junction"/>, and a run of bits is a field like any
/// other. What remains here is the part that was always about octets.
/// </para>
/// <para>
/// Which is why this is now small enough to read in one go. Everything that used to hang off a field —
/// where its value comes from, when it is there at all, what set it draws from, what it points at, what it
/// is checked against — is an <b>edge</b>. Those were authoring conveniences that got copied onto edges
/// when a graph was built, so the field carried a second copy of every fact, and the two could disagree.
/// </para>
/// </remarks>
public sealed class Field : Node
{
    /// <summary>What this field is called, and the name anything pointing at it uses.</summary>
    public required string Id { get; init; }

    public override string Name => Id;

    /// <summary>How its value becomes octets, and back.</summary>
    public required WireForm Form { get; init; }

    /// <summary>What to call it when the message is read out for a person — the specification's own name
    /// for it, which is rarely a good identifier and always the right label.</summary>
    public string? As { get; init; }

    /// <summary>A converter applied on the way out and inverted on the way in.</summary>
    public Conversion? Via { get; init; }

    public override string ToString() => As is null ? Id : $"{Id} ({As})";
}
