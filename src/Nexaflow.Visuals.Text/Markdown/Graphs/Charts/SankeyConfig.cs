using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>How a Sankey link is coloured.  <see cref="Custom"/> means a fixed colour is supplied in
/// <see cref="SankeyConfig.LinkColorCustom"/>.</summary>
public enum SankeyLinkColor { Source, Target, Gradient, Custom }

/// <summary>Horizontal alignment of the Sankey columns (d3-sankey alignment modes).</summary>
public enum SankeyNodeAlignment { Justify, Center, Left, Right }

/// <summary>Node-label rendering style.</summary>
public enum SankeyLabelStyle { Legacy, Outlined }

/// <summary>
/// The Mermaid <c>config.sankey</c> configuration.  Values carry Mermaid's documented defaults, except
/// <see cref="Width"/>/<see cref="Height"/>, which are tuned for the in-app surface.  Node/link colours
/// fall back to the active <see cref="MarkdownPalette"/>; <see cref="NodeColors"/> overrides per node by name.
/// </summary>
public sealed class SankeyConfig
{
    public double Width  { get; set; } = 720;   // Mermaid default 600
    public double Height { get; set; } = 440;   // Mermaid default 400

    public SankeyLinkColor LinkColor { get; set; } = SankeyLinkColor.Gradient;
    /// <summary>The fixed link colour when <see cref="LinkColor"/> is <see cref="SankeyLinkColor.Custom"/>.</summary>
    public Brush? LinkColorCustom { get; set; }

    public SankeyNodeAlignment NodeAlignment { get; set; } = SankeyNodeAlignment.Justify;

    public bool ShowValues { get; set; } = true;
    public string Prefix { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;

    public double NodeWidth   { get; set; } = 10;
    public double NodePadding { get; set; } = 12;

    public SankeyLabelStyle LabelStyle { get; set; } = SankeyLabelStyle.Legacy;

    /// <summary>Per-node colour overrides, keyed by node name. Nodes not listed use the palette.</summary>
    public Dictionary<string, Brush> NodeColors { get; } = new(StringComparer.Ordinal);

    /// <summary>Mermaid's responsive-width flag — parsed for completeness; the diagram already sizes to content.</summary>
    public bool UseMaxWidth { get; set; }
}
