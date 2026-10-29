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
    /// One row of bits, holding the runs of the fields on it in the order they are drawn across it. It stands for nothing
    /// written: a row is how the bits are laid out, not a line of the packet.
    /// </summary>
    public const string Row = "Row";

    /// <summary>
    /// The run of a field's bits on one row: a box, its label in it and its numbers over its ends. It stands for the whole
    /// field as it was written, so pressing it means that line; a field running across rows is a run in each row, every one
    /// standing for the same field.
    /// </summary>
    public const string Field = "Field";

    /// <summary>A field's label, in its box — typed into in the box on the row the field starts on.</summary>
    public const string Label = "Label";

    /// <summary>
    /// The number of a field's first or last bit on a row, over the end of its box it stands at — typed into where it is the
    /// number written, and otherwise worked out and pressed as the field.
    /// </summary>
    public const string Bit = "Bit";
}
