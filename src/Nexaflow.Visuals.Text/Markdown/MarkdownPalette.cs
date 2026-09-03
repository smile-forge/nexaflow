using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Colour scheme for markdown rendering. The same renderer (<see cref="BlockRenderer"/>,
/// <see cref="MarkdownFlowDocument"/>) serves both dark surfaces (AI overlay, AIChat,
/// markdown editor) and light surfaces (scratchpad post-its) by swapping the palette.
/// Fonts and font sizes are theme-independent and stay on <see cref="BlockRenderer"/>.
///
/// The light palette uses semi-transparent black for fills/borders so code blocks,
/// quotes and tables tint whatever note colour sits behind them.
/// </summary>
public sealed class MarkdownPalette
{
    public required Brush Text          { get; init; }
    public required Brush TextMuted     { get; init; }
    public required Brush Accent        { get; init; }
    public required Brush Heading       { get; init; }
    public required Brush DefTerm       { get; init; }
    public required Brush Citation      { get; init; }
    /// <summary>Highlighter wash behind <c>==marked==</c> text (emphasis-extras). Translucent so it
    /// tints the surface and keeps the body text legible on either a light or dark background.</summary>
    public required Brush Marked        { get; init; }
    /// <summary>Semantic accents (green / amber / red / purple). Used for alert-block callouts
    /// (<c>&gt; [!TIP]</c> / <c>[!WARNING]</c> / <c>[!CAUTION]</c> / <c>[!IMPORTANT]</c>); reusable
    /// for any status colour.</summary>
    public required Brush Success       { get; init; }
    public required Brush Warning       { get; init; }
    public required Brush Danger        { get; init; }
    public required Brush Important      { get; init; }
    public required Brush CodeBg        { get; init; }
    public required Brush CodeBorder    { get; init; }
    public required Brush QuoteBg       { get; init; }
    public required Brush Hr            { get; init; }
    public required Brush TableBorder   { get; init; }
    public required Brush TableHeaderBg { get; init; }
    public required Brush TableAltRowBg { get; init; }
    public required Brush FigureBorder  { get; init; }
    public required Brush FigureBg      { get; init; }
    public required Brush FooterBg      { get; init; }

    /// <summary>
    /// The QR code's dark modules and the paper they sit on. Their own pair rather than <see cref="Text"/>
    /// over <see cref="FigureBg"/>, because a QR code is not decoration: it has to stay dark-on-light on
    /// every theme or scanners stop reading it. A theme may still retune them (<c>QrDarkBrush</c> /
    /// <c>QrLightBrush</c>), and a block's own <c>dark:</c> / <c>light:</c> settings win over both.
    /// </summary>
    public Brush QrDark  { get; init; } = DefaultQrDark;
    public Brush QrLight { get; init; } = DefaultQrLight;

    private static readonly Brush DefaultQrDark  = Frozen(0x0B, 0x0B, 0x0F);
    private static readonly Brush DefaultQrLight = Frozen(0xFF, 0xFF, 0xFF);


    /// <summary>
    /// A barcode's bars and the label stock behind them. Their own pair for the same reason the QR ones
    /// are: a scannable mark has to stay dark-on-light whatever the theme does. Separate from the QR pair
    /// rather than shared with it because a barcode is routinely printed in a brand colour and a QR code
    /// rarely is - the block syntax's own example sets lineColor to a blue.
    /// </summary>
    public Brush BarcodeDark  { get; init; } = DefaultQrDark;
    public Brush BarcodeLight { get; init; } = DefaultQrLight;

    /// <summary>
    /// The C4 diagram element colours, by abstraction level. Unlike every other member these are
    /// <b>nullable</b> and default to null: C4's scheme is a *grading* (the deeper the colour, the
    /// higher the abstraction; grey means external), and the grading is derived from this palette's
    /// own <see cref="Accent"/> and <see cref="TextMuted"/> rather than fixed — so a C4 diagram reads
    /// as C4 on every theme instead of staying cornflower blue when the theme changes. A theme sets
    /// <c>C4PersonBrush</c>…<c>C4DeploymentNodeBrush</c> to pin any level (including to
    /// C4-PlantUML's canonical hex), and a diagram's own <c>UpdateElementStyle($bgColor=…)</c> wins
    /// over both. Resolved by <c>C4Palette</c>.
    /// </summary>
    public Brush? C4Person         { get; init; }
    public Brush? C4System         { get; init; }
    public Brush? C4Container      { get; init; }
    public Brush? C4Component      { get; init; }
    public Brush? C4External       { get; init; }
    public Brush? C4Boundary       { get; init; }
    public Brush? C4DeploymentNode { get; init; }

    /// <summary>Distinct, saturated colours for chart series / pie slices / graph accents. Read
    /// cyclically by the graph + chart renderers. Saturated enough to read on light or dark surfaces;
    /// a theme can supply its own set.</summary>
    public IReadOnlyList<Brush> Series { get; init; } = DefaultSeries;

    /// <summary>The shared "mini palette" of chart colours.</summary>
    public static readonly IReadOnlyList<Brush> DefaultSeries =
    [
        Frozen(0x4F, 0x8E, 0xF7), // blue
        Frozen(0xFF, 0x6B, 0x6B), // red
        Frozen(0x4E, 0xCB, 0x71), // green
        Frozen(0xFF, 0xD0, 0x60), // amber
        Frozen(0xA0, 0x60, 0xFF), // purple
        Frozen(0xFF, 0x9F, 0x43), // orange
        Frozen(0x48, 0xDB, 0xFB), // cyan
        Frozen(0xFF, 0x6B, 0xB5), // pink
        Frozen(0x1D, 0xD1, 0xA1), // teal
        Frozen(0xFE, 0xCA, 0x57), // yellow
    ];

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Brush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>Light text on dark surfaces — the original app theme. Default everywhere.</summary>
    public static readonly MarkdownPalette Dark = new()
    {
        Text          = Frozen(0xE8, 0xEA, 0xF2),
        TextMuted     = Frozen(0x78, 0x80, 0xA0),
        Accent        = Frozen(0x4F, 0x8E, 0xF7),
        Heading       = Frozen(0xA8, 0xD4, 0xFF),
        DefTerm       = Frozen(0xD0, 0xE8, 0xFF),
        Citation      = Frozen(0xC8, 0xA8, 0xFF),
        Marked        = Frozen(0x55, 0xFF, 0xD0, 0x60),
        Success       = Frozen(0x4E, 0xCB, 0x71),
        Warning       = Frozen(0xF5, 0x9E, 0x0B),
        Danger        = Frozen(0xFF, 0x6B, 0x6B),
        Important     = Frozen(0xC8, 0xA8, 0xFF),
        CodeBg        = Frozen(0x08, 0x0C, 0x16),
        CodeBorder    = Frozen(0x2A, 0x30, 0x47),
        QuoteBg       = Frozen(0x2A, 0x30, 0x47),
        Hr            = Frozen(0x2A, 0x30, 0x47),
        TableBorder   = Frozen(0x3A, 0x42, 0x5C),
        TableHeaderBg = Frozen(0x1E, 0x24, 0x38),
        TableAltRowBg = Frozen(0x11, 0x15, 0x22),
        FigureBorder  = Frozen(0x3A, 0x42, 0x5C),
        FigureBg      = Frozen(0x11, 0x15, 0x22),
        FooterBg      = Frozen(0x11, 0x15, 0x22),
    };

    /// <summary>Dark text on light surfaces — for the coloured scratchpad post-its.</summary>
    public static readonly MarkdownPalette Light = new()
    {
        Text          = Frozen(0x1F, 0x1F, 0x23),
        TextMuted     = Frozen(0x5C, 0x5F, 0x66),
        Accent        = Frozen(0x18, 0x57, 0xC0),
        Heading       = Frozen(0x16, 0x33, 0x5E),
        DefTerm       = Frozen(0x16, 0x33, 0x5E),
        Citation      = Frozen(0x6A, 0x1B, 0x9A),
        Marked        = Frozen(0x66, 0xFF, 0xE0, 0x60),
        Success       = Frozen(0x1E, 0x7E, 0x4A),
        Warning       = Frozen(0xB4, 0x6A, 0x00),
        Danger        = Frozen(0xC0, 0x2A, 0x2A),
        Important     = Frozen(0x6A, 0x1B, 0x9A),
        CodeBg        = Frozen(0x18, 0x00, 0x00, 0x00),
        CodeBorder    = Frozen(0x33, 0x00, 0x00, 0x00),
        QuoteBg       = Frozen(0x12, 0x00, 0x00, 0x00),
        Hr            = Frozen(0x33, 0x00, 0x00, 0x00),
        TableBorder   = Frozen(0x40, 0x00, 0x00, 0x00),
        TableHeaderBg = Frozen(0x1F, 0x00, 0x00, 0x00),
        TableAltRowBg = Frozen(0x0E, 0x00, 0x00, 0x00),
        FigureBorder  = Frozen(0x33, 0x00, 0x00, 0x00),
        FigureBg      = Frozen(0x10, 0x00, 0x00, 0x00),
        FooterBg      = Frozen(0x10, 0x00, 0x00, 0x00),
    };

    /// <summary>
    /// Builds a palette from the active application theme's brushes (TextBrush, AccentBrush, surfaces…),
    /// so markdown text + backgrounds match whatever theme is loaded — light themes get dark text,
    /// dark/immersive themes get their own accents. Falls back to <see cref="Dark"/> for any key that
    /// isn't present (or when there is no <see cref="Application"/>, e.g. in tests).
    /// </summary>
    public static MarkdownPalette FromTheme()
    {
        var res = Application.Current?.Resources;
        Brush R(string key, Brush fallback) => res?[key] as Brush ?? fallback;
        var d = Dark;

        return new MarkdownPalette
        {
            Text          = R("TextBrush",        d.Text),
            TextMuted     = R("TextMutedBrush",   d.TextMuted),
            Accent        = R("AccentBrush",      d.Accent),
            Heading       = R("AccentBrush",      d.Heading),
            DefTerm       = R("TextBrush",        d.DefTerm),
            Citation      = R("Accent2Brush",     d.Citation),
            Marked        = R("MarkedBrush",      d.Marked),
            Success       = R("SuccessBrush",     d.Success),
            Warning       = R("WarningBrush",     d.Warning),
            Danger        = R("DangerBrush",      d.Danger),
            Important     = R("Accent2Brush",     d.Important),
            CodeBg        = R("DeepBgBrush",      d.CodeBg),
            CodeBorder    = R("BorderBrush",      d.CodeBorder),
            QuoteBg       = R("Surface2Brush",    d.QuoteBg),
            Hr            = R("BorderBrush",       d.Hr),
            TableBorder   = R("BorderLightBrush", d.TableBorder),
            TableHeaderBg = R("Surface2Brush",    d.TableHeaderBg),
            TableAltRowBg = R("SurfaceBrush",     d.TableAltRowBg),
            FigureBorder  = R("BorderBrush",      d.FigureBorder),
            FigureBg      = R("SurfaceBrush",     d.FigureBg),
            FooterBg      = R("SurfaceBrush",     d.FooterBg),
            QrDark        = R("QrDarkBrush",      d.QrDark),
            QrLight       = R("QrLightBrush",     d.QrLight),
            BarcodeDark   = R("BarcodeDarkBrush",  d.BarcodeDark),
            BarcodeLight  = R("BarcodeLightBrush", d.BarcodeLight),

            // Null unless the theme pins one — see the C4* members: absent means "derive the C4
            // grading from Accent/TextMuted", which is the wanted default, not a missing value.
            C4Person         = res?["C4PersonBrush"]         as Brush,
            C4System         = res?["C4SystemBrush"]         as Brush,
            C4Container      = res?["C4ContainerBrush"]      as Brush,
            C4Component      = res?["C4ComponentBrush"]      as Brush,
            C4External       = res?["C4ExternalBrush"]       as Brush,
            C4Boundary       = res?["C4BoundaryBrush"]       as Brush,
            C4DeploymentNode = res?["C4DeploymentNodeBrush"] as Brush,
        };
    }
}
