using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Packet;

/// <summary>Which way a row's bits are numbered across it.</summary>
public enum PacketBitOrder
{
    /// <summary>Its lowest bit on the left — how a protocol's header is drawn.</summary>
    Ascending,

    /// <summary>
    /// Its highest bit on the left — how a hardware register is drawn. Only the drawing turns round: fields are still written
    /// lowest bit first and keep their width, and a row not filled has its room on the left, its lowest bit against the right.
    /// </summary>
    Descending,
}

/// <summary>
/// What a packet's front matter asks for: its geometry under <c>config: packet:</c>, and its colours and sizes under
/// <c>themeVariables: packet:</c>.
///
/// <para>
/// The defaults and the limits are Mermaid's: a bit 32 wide, a row 32 tall holding 32 bits, numbered from the left, with 5 of
/// air between two fields and between two rows. A value outside what an option allows is brought back inside it rather than
/// refused — config is not source, and a packet with a silly number in its front matter still draws. Colours are the words they
/// were written as, and a colour or a size nobody wrote is the theme's.
/// </para>
/// </summary>
public sealed record PacketConfig
{
    /// <summary>What a block with no front matter asks for.</summary>
    public static PacketConfig Default { get; } = new();

    /// <summary>How tall a row is drawn, at least — a label wrapped onto more lines than that holds makes its row taller.</summary>
    public double RowHeight { get; init; } = 32;

    /// <summary>How wide one bit is drawn, before anything asks the packet to be narrower.</summary>
    public double BitWidth { get; init; } = 32;

    /// <summary>How many bits a row holds; a field running past the end of one goes on into the next.</summary>
    public int BitsPerRow { get; init; } = 32;

    /// <summary>Whether each field's first and last bit are numbered over it.</summary>
    public bool ShowBits { get; init; } = true;

    /// <summary>Which way a row is numbered.</summary>
    public PacketBitOrder BitOrder { get; init; }

    /// <summary>The air between two fields side by side.</summary>
    public double PaddingX { get; init; } = 5;

    /// <summary>The air between two rows, over which the bit numbers are set where they are shown.</summary>
    public double PaddingY { get; init; } = 5;

    /// <summary>The size of a bit number, a label and the title — or null for the theme's.</summary>
    public double? ByteFontSize { get; init; }

    /// <inheritdoc cref="ByteFontSize"/>
    public double? LabelFontSize { get; init; }

    /// <inheritdoc cref="ByteFontSize"/>
    public double? TitleFontSize { get; init; }

    /// <summary>The ink of the number over a field's first bit as a row reads, and over its last — or null for the theme's.</summary>
    public string? StartByteColour { get; init; }

    /// <inheritdoc cref="StartByteColour"/>
    public string? EndByteColour { get; init; }

    /// <summary>The ink of a label and of the title — or null for the theme's.</summary>
    public string? LabelColour { get; init; }

    /// <inheritdoc cref="LabelColour"/>
    public string? TitleColour { get; init; }

    /// <summary>What a field is filled with, the line round it and how thick that is — or null for the theme's.</summary>
    public string? BlockFillColour { get; init; }

    /// <inheritdoc cref="BlockFillColour"/>
    public string? BlockStrokeColour { get; init; }

    /// <inheritdoc cref="BlockFillColour"/>
    public double? BlockStrokeWidth { get; init; }

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static PacketConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    /// <summary>Reads a block's front matter, already read as config.</summary>
    public static PacketConfig From(MermaidConfig config)
    {
        var packet = config.Diagram("packet");
        var theme = config.DiagramTheme("packet");

        return new PacketConfig
        {
            RowHeight = Math.Max(1, packet.Number("rowHeight") ?? Default.RowHeight),
            BitWidth = Math.Max(1, packet.Number("bitWidth") ?? Default.BitWidth),
            BitsPerRow = packet.Number("bitsPerRow") is { } bits ? (int)Math.Clamp(Math.Floor(bits), 1, int.MaxValue) : Default.BitsPerRow,
            ShowBits = packet.Flag("showBits") ?? Default.ShowBits,
            BitOrder = string.Equals(packet.Value("bitOrder")?.Trim(), "descending", StringComparison.OrdinalIgnoreCase)
                ? PacketBitOrder.Descending
                : PacketBitOrder.Ascending,
            PaddingX = Math.Max(0, packet.Number("paddingX") ?? Default.PaddingX),
            PaddingY = Math.Max(0, packet.Number("paddingY") ?? Default.PaddingY),

            ByteFontSize = theme.Size("byteFontSize"),
            LabelFontSize = theme.Size("labelFontSize"),
            TitleFontSize = theme.Size("titleFontSize"),
            StartByteColour = theme.Value("startByteColor"),
            EndByteColour = theme.Value("endByteColor"),
            LabelColour = theme.Value("labelColor"),
            TitleColour = theme.Value("titleColor"),
            BlockFillColour = theme.Value("blockFillColor"),
            BlockStrokeColour = theme.Value("blockStrokeColor"),
            BlockStrokeWidth = theme.Number("blockStrokeWidth") is { } width ? Math.Max(0, width) : null,
        };
    }
}
