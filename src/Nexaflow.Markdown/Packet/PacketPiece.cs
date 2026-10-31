namespace Nexaflow.Markdown.Packet;

/// <summary>
/// The pieces a packet's layout is made of: its rows as they are drawn, top to bottom, and in each row the fields on it, left
/// to right.
/// </summary>
public static class PacketPiece
{
    /// <summary>The rows, which is the packet itself.</summary>
    public const string Rows = "Rows";

    /// <summary>
    /// One row of bits, holding the runs of the fields on it in the order they are drawn across it, and over them the band of
    /// their numbers. It stands for nothing written: a row is how the bits are laid out, not a line of the packet.
    /// </summary>
    public const string Row = "Row";

    /// <summary>
    /// The run of a field's bits on one row: a box and its label in it. It stands for the whole field as it was written, so
    /// pressing it means that line; a field running across rows is a run in each row, every one standing for the same field.
    /// </summary>
    public const string Field = "Field";

    /// <summary>A field's label, in its box — typed into in the box on the row the field starts on.</summary>
    public const string Label = "Label";

    /// <summary>
    /// The end of a field, where its last bit ends: a strip along that edge of the box on the row the field ends on, standing for the
    /// field. A piece of its own so it can be taken hold of — a press on it picks up the field's length, and where it is let go is
    /// where the field then ends.
    /// </summary>
    public const string End = "End";

    /// <summary>The numbers over a row's fields, apart from the fields themselves. It stands for nothing written.</summary>
    public const string Bits = "Bits";

    /// <summary>
    /// The number of a field's first or last bit on a row, over the end of its box — worked out from the bits, standing for
    /// nothing: no caret stops in it, and no press means it.
    /// </summary>
    public const string Bit = "Bit";
}
