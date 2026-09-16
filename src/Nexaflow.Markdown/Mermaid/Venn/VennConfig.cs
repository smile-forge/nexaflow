namespace Nexaflow.Markdown.Mermaid.Venn;

/// <summary>
/// What a <c>venn-beta</c> block's front matter asks for: the size of the drawing under <c>config: venn:</c>, and its
/// colours under <c>themeVariables:</c>.
///
/// <para>
/// The keys are Mermaid's — <c>width</c>, <c>height</c>, <c>padding</c>, <c>useMaxWidth</c> and <c>useDebugLayout</c>,
/// and <c>venn1</c>…<c>venn8</c>, <c>vennTitleTextColor</c> and <c>vennSetTextColor</c>. A width or height nobody wrote
/// is the builder's own, as a colour nobody wrote is the theme's: Mermaid's 800 by 450 is what its renderer draws at, not
/// a claim about what a diagram in the middle of a page should be.
/// </para>
/// </summary>
public sealed record VennConfig
{
    /// <summary>How many colours the palette has before it starts again.</summary>
    public const int PaletteSize = 8;

    /// <summary>What a block with no front matter asks for.</summary>
    public static VennConfig Default { get; } = new();

    /// <summary>How wide the drawing is, or null for the builder's own.</summary>
    public double? Width { get; init; }

    /// <summary>How tall the drawing is, or null for the builder's own.</summary>
    public double? Height { get; init; }

    /// <summary>Clear air between the circles and the edge of the drawing.</summary>
    public double Padding { get; init; } = 15;

    /// <summary>Whether the drawing is fitted to the width it is given, rather than drawn at the width it asks for.</summary>
    public bool UseMaxWidth { get; init; } = true;

    /// <summary>Whether the layout's workings are drawn over it: each circle's centre, and where each region's text is set.</summary>
    public bool UseDebugLayout { get; init; }

    /// <summary>The colours written for <c>venn1</c>…<c>venn8</c>, by their number. What is not written is the theme's.</summary>
    public IReadOnlyDictionary<int, string> Swatches { get; init; } = new Dictionary<int, string>();

    /// <summary>The ink of the title, or null for the theme's.</summary>
    public string? TitleTextColour { get; init; }

    /// <summary>The ink of what is written on the sets and their overlaps, or null for the theme's.</summary>
    public string? SetTextColour { get; init; }

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static VennConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    /// <summary>Reads a block's front matter, already read as config.</summary>
    public static VennConfig From(MermaidConfig config)
    {
        var venn = config.Section("config")?.Section("venn") ?? MermaidConfig.None;
        var theme = config.Section("config")?.Section("themeVariables") ?? MermaidConfig.None;

        var swatches = new Dictionary<int, string>();
        for (var number = 1; number <= PaletteSize; number++)
            if (theme.Value($"venn{number}") is { Length: > 0 } colour)
                swatches[number] = colour;

        return new VennConfig
        {
            Width = Size(venn.Number("width")),
            Height = Size(venn.Number("height")),
            Padding = Math.Max(0, venn.Number("padding") ?? Default.Padding),
            UseMaxWidth = venn.Flag("useMaxWidth") ?? Default.UseMaxWidth,
            UseDebugLayout = venn.Flag("useDebugLayout") ?? Default.UseDebugLayout,

            Swatches = swatches,
            TitleTextColour = theme.Value("vennTitleTextColor"),
            SetTextColour = theme.Value("vennSetTextColor"),
        };
    }

    /// <summary>A size, or nothing where none was written or what was written would draw nothing.</summary>
    private static double? Size(double? asked) => asked is > 0 ? asked : null;
}
