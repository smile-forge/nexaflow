namespace Nexaflow.Markdown.Packet;

/// <summary>What a piece of a <c>packet</c> block is, beyond what every block written Mermaid's way shares (<see cref="Mermaid.MermaidKinds"/>).</summary>
public static class PacketKinds
{
    /// <summary>One field: its bits — a range, one bit, or a count — a colon, and its label. A <see cref="PacketFieldNode"/> once its stages have run.</summary>
    public const string Field = "packet-field";

    /// <summary>A field's label: the quote it opens with, what it says, and the quote closing it — or what is still to write, where nothing is yet.</summary>
    public const string Label = "packet-label";

    /// <summary>A backslash and the character it keeps, inside a label: <c>\"</c>, <c>\\</c>, <c>\n</c>.</summary>
    public const string Escape = "packet-escape";

    /// <summary>A line ending inside a label, which a label written in quotes may run across.</summary>
    public const string Break = "packet-break";
}

/// <summary>What a piece of a <c>packet</c> block is <em>to</em> the piece holding it.</summary>
public static class PacketRoles
{
    /// <summary>The bit a field starts at, where it is written.</summary>
    public const string Start = "packet-start";

    /// <summary>The bit a field ends at, after the dash.</summary>
    public const string End = "packet-end";

    /// <summary>How many bits a field takes, after the plus.</summary>
    public const string Bits = "packet-bits";

    /// <summary>What a field's label says.</summary>
    public const string Label = "packet-label";
}
