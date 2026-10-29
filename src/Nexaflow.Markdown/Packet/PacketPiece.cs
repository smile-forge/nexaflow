namespace Nexaflow.Markdown.Packet;

/// <summary>The pieces a packet's layout is made of — its layers, and what is in them.</summary>
public static class PacketPiece
{
    /// <summary>The fields, which is the packet itself.</summary>
    public const string Fields = "Fields";

    /// <summary>
    /// The run of a field's bits on one row, drawn as a box with its label in it. It stands for the whole field as it was
    /// written, so pressing it means that line; a field running across rows is a box on each.
    /// </summary>
    public const string Field = "Field";

    /// <summary>A field's label, in its box — typed into in the box on the row the field starts on.</summary>
    public const string Label = "Label";

    /// <summary>The numbers over the fields.</summary>
    public const string Bits = "Bits";

    /// <summary>
    /// The number of a field's first or last bit on a row, over the end of its box it stands at — typed into where it is the
    /// number written, and otherwise worked out and pressed as the field.
    /// </summary>
    public const string Bit = "Bit";
}
