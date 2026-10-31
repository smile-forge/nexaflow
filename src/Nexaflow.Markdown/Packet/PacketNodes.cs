using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Packet;

/// <summary>
/// A field as its stages leave it: the line written for it, and what it comes to — the bits it takes, how they fall across the
/// rows, and which of its numbers stands in the way of knowing how many it takes. None of that is in the line's characters,
/// which it prints as all the same.
/// </summary>
internal sealed class PacketFieldNode : ContentNode
{
    internal PacketFieldNode(ContentNode written) : this(written, written as PacketFieldNode) { }

    private PacketFieldNode(ContentNode shape, PacketFieldNode? said) : base(shape)
    {
        if (said is null) return;

        this.First = said.First;
        this.Last = said.Last;
        this.Spans = said.Spans;
        this.Unsized = said.Unsized;
    }

    /// <summary>The bit it starts at: the one written, or the one after the field before it where none is.</summary>
    public long First { get; init; }

    /// <summary>The bit it ends at — <see cref="First"/>, where how many bits it takes cannot be known.</summary>
    public long Last { get; init; }

    /// <summary>The runs of its bits, a row each, first to last — none for a field starting past the last row a packet draws.</summary>
    public IReadOnlyList<PacketSpan> Spans { get; init; } = [];

    /// <summary>
    /// The role of the number that leaves how many bits it takes unknown — its end or its count, still to write, not a whole
    /// number, before its start or nought — or null where that is known.
    /// </summary>
    public string? Unsized { get; init; }

    protected override ContentNode Reshaped(ContentNode shape) => new PacketFieldNode(shape, this);
}

/// <summary>The run of a field's bits on one row: the row, and the first and last bit of the field on it.</summary>
public readonly record struct PacketSpan(int Row, long From, long To)
{
    /// <summary>How many bits the run takes.</summary>
    public long Bits => this.To - this.From + 1;
}
