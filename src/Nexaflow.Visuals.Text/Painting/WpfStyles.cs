using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Icons;
using Nexaflow.Visuals.Text.Editor.Highlighting;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Visuals.Text.Painting;

/// <summary>
/// What content is drawn in on a WPF surface, said in the medium's own handles: the theme's brushes as inks, its typefaces as faces,
/// and the window's language for what a layout says — one <see cref="PortableStyle"/> a showing hands every builder.
///
/// <para>
/// The same builders serve dark surfaces (the AI overlay, a chat, the markdown page) and light ones (a scratchpad post-it) by being
/// handed a different one. The light values use semi-transparent black for fills and borders, so code blocks, quotes and tables tint
/// whatever note colour sits behind them. A surface that differs in one thing says so — <c>style with { TextSize = 18 }</c>.
/// </para>
/// </summary>
public static class WpfStyles
{
    /// <summary>The family body text is set in where nothing names another.</summary>
    public const string TextFont = "Segoe UI";

    /// <summary>The family code, and source shown as it was written, is set in where nothing names another.</summary>
    public const string MonoFont = "Consolas, Courier New";

    /// <summary>Light text on dark surfaces — the app's own theme, and the default everywhere.</summary>
    public static PortableStyle Dark => _dark ??= Made(DarkInks);

    /// <summary>Dark text on light surfaces — for the coloured scratchpad post-its.</summary>
    public static PortableStyle Light => _light ??= Made(LightInks);

    private static PortableStyle? _dark;
    private static PortableStyle? _light;

    /// <summary>
    /// The active theme's brushes (TextBrush, AccentBrush, the surfaces…), so content matches whatever theme is loaded — light themes
    /// get dark text, dark ones their own accents. Any key the theme lacks, and all of them where there is no application, is
    /// <see cref="Dark"/>'s.
    /// </summary>
    public static PortableStyle FromTheme()
    {
        var resources = Application.Current?.Resources;
        Brush Themed(string key, Brush fallback) => resources?[key] as Brush ?? fallback;
        Brush? Pinned(string key) => resources?[key] as Brush;

        var dark = DarkInks;

        return Made(new Inks
        {
            Text = Themed("TextBrush", dark.Text),
            TextMuted = Themed("TextMutedBrush", dark.TextMuted),
            Accent = Themed("AccentBrush", dark.Accent),
            Heading = Themed("AccentBrush", dark.Heading),
            DefTerm = Themed("TextBrush", dark.DefTerm),
            Citation = Themed("Accent2Brush", dark.Citation),
            Marked = Themed("MarkedBrush", dark.Marked),
            Success = Themed("SuccessBrush", dark.Success),
            Warning = Themed("WarningBrush", dark.Warning),
            Danger = Themed("DangerBrush", dark.Danger),
            Important = Themed("Accent2Brush", dark.Important),
            CodeBg = Themed("DeepBgBrush", dark.CodeBg),
            CodeBorder = Themed("BorderBrush", dark.CodeBorder),
            QuoteBg = Themed("Surface2Brush", dark.QuoteBg),
            Hr = Themed("BorderBrush", dark.Hr),
            TableBorder = Themed("BorderLightBrush", dark.TableBorder),
            TableHeaderBg = Themed("Surface2Brush", dark.TableHeaderBg),
            TableAltRowBg = Themed("SurfaceBrush", dark.TableAltRowBg),
            FigureBorder = Themed("BorderBrush", dark.FigureBorder),
            FigureBg = Themed("SurfaceBrush", dark.FigureBg),
            FooterBg = Themed("SurfaceBrush", dark.FooterBg),
            QrDark = Themed("QrDarkBrush", dark.QrDark),
            QrLight = Themed("QrLightBrush", dark.QrLight),
            BarcodeDark = Themed("BarcodeDarkBrush", dark.BarcodeDark),
            BarcodeLight = Themed("BarcodeLightBrush", dark.BarcodeLight),
            Elements = dark.Elements.ToDictionary(element => element.Key, element => Themed($"Element{element.Key}Brush", element.Value)),

            // Null unless the theme pins one: absent means C4's grading is derived from the accent and the muted text, which is the
            // wanted default rather than a missing value.
            C4Person = Pinned("C4PersonBrush"),
            C4System = Pinned("C4SystemBrush"),
            C4Container = Pinned("C4ContainerBrush"),
            C4Component = Pinned("C4ComponentBrush"),
            C4External = Pinned("C4ExternalBrush"),
            C4Boundary = Pinned("C4BoundaryBrush"),
            C4DeploymentNode = Pinned("C4DeploymentNodeBrush"),
        });
    }

    /// <summary>The brush an ink of a style made here paints with.</summary>
    public static Brush Brush(this LayoutInk ink) => ((WpfInk)ink).Brush;

    /// <summary>The typeface a face of a style made here sets words in.</summary>
    public static Typeface Typeface(this LayoutFace face) => ((WpfFace)face).Typeface;

    /// <summary>What a theme's brushes are, before they are made the medium's inks.</summary>
    private sealed record Inks
    {
        public required Brush Text { get; init; }
        public required Brush TextMuted { get; init; }
        public required Brush Accent { get; init; }
        public required Brush Heading { get; init; }
        public required Brush DefTerm { get; init; }
        public required Brush Citation { get; init; }

        /// <summary>The highlighter wash behind <c>==marked==</c> text — translucent, so it tints the surface on a light or a dark one.</summary>
        public required Brush Marked { get; init; }

        public required Brush Success { get; init; }
        public required Brush Warning { get; init; }
        public required Brush Danger { get; init; }
        public required Brush Important { get; init; }
        public required Brush CodeBg { get; init; }
        public required Brush CodeBorder { get; init; }
        public required Brush QuoteBg { get; init; }
        public required Brush Hr { get; init; }
        public required Brush TableBorder { get; init; }
        public required Brush TableHeaderBg { get; init; }
        public required Brush TableAltRowBg { get; init; }
        public required Brush FigureBorder { get; init; }
        public required Brush FigureBg { get; init; }
        public required Brush FooterBg { get; init; }

        /// <summary>A scannable code's dark modules and the paper round them — dark on light whatever the theme, which a scanner needs.</summary>
        public Brush QrDark { get; init; } = Frozen(0x0B, 0x0B, 0x0F);

        public Brush QrLight { get; init; } = Frozen(0xFF, 0xFF, 0xFF);

        /// <summary>A barcode's bars and stock: a pair of their own, since a barcode is routinely printed in a brand colour.</summary>
        public Brush BarcodeDark { get; init; } = Frozen(0x0B, 0x0B, 0x0F);

        public Brush BarcodeLight { get; init; } = Frozen(0xFF, 0xFF, 0xFF);

        /// <summary>Distinct, saturated colours a chart takes in turn — saturated enough to read on a light surface or a dark one.</summary>
        public IReadOnlyList<Brush> Series { get; init; } = DefaultSeries;

        /// <summary>
        /// The colours a chemical structure draws its elements in, by symbol — nitrogen blue, oxygen red, sulfur yellow, the halogens
        /// green — mid-tones, since a structure is read by its colours on a light page and a dark one alike.
        /// </summary>
        public IReadOnlyDictionary<string, Brush> Elements { get; init; } = DefaultElements;

        /// <summary>The colours a theme pins C4's levels to — each null where it pins none, and C4 grades from the accent instead.</summary>
        public Brush? C4Person { get; init; }
        public Brush? C4System { get; init; }
        public Brush? C4Container { get; init; }
        public Brush? C4Component { get; init; }
        public Brush? C4External { get; init; }
        public Brush? C4Boundary { get; init; }
        public Brush? C4DeploymentNode { get; init; }
    }

    private static readonly IReadOnlyList<Brush> DefaultSeries =
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

    private static readonly IReadOnlyDictionary<string, Brush> DefaultElements = new Dictionary<string, Brush>
    {
        ["N"] = Frozen(0x3B, 0x82, 0xF6), // blue
        ["O"] = Frozen(0xEF, 0x44, 0x44), // red
        ["S"] = Frozen(0xCA, 0x8A, 0x04), // yellow
        ["P"] = Frozen(0xEA, 0x58, 0x0C), // orange
        ["F"] = Frozen(0x22, 0xA3, 0x4A), // green
        ["Cl"] = Frozen(0x22, 0xA3, 0x4A), // green
        ["Br"] = Frozen(0xB4, 0x53, 0x09), // brown
        ["I"] = Frozen(0x93, 0x33, 0xEA), // purple
        ["B"] = Frozen(0xDB, 0x27, 0x77), // pink
        ["Si"] = Frozen(0xA1, 0x62, 0x07), // ochre
        ["Se"] = Frozen(0xCA, 0x8A, 0x04), // yellow
    };

    private static readonly Inks DarkInks = new()
    {
        Text = Frozen(0xE8, 0xEA, 0xF2),
        TextMuted = Frozen(0x78, 0x80, 0xA0),
        Accent = Frozen(0x4F, 0x8E, 0xF7),
        Heading = Frozen(0xA8, 0xD4, 0xFF),
        DefTerm = Frozen(0xD0, 0xE8, 0xFF),
        Citation = Frozen(0xC8, 0xA8, 0xFF),
        Marked = Frozen(0x55, 0xFF, 0xD0, 0x60),
        Success = Frozen(0x4E, 0xCB, 0x71),
        Warning = Frozen(0xF5, 0x9E, 0x0B),
        Danger = Frozen(0xFF, 0x6B, 0x6B),
        Important = Frozen(0xC8, 0xA8, 0xFF),
        CodeBg = Frozen(0x08, 0x0C, 0x16),
        CodeBorder = Frozen(0x2A, 0x30, 0x47),
        QuoteBg = Frozen(0x2A, 0x30, 0x47),
        Hr = Frozen(0x2A, 0x30, 0x47),
        TableBorder = Frozen(0x3A, 0x42, 0x5C),
        TableHeaderBg = Frozen(0x1E, 0x24, 0x38),
        TableAltRowBg = Frozen(0x11, 0x15, 0x22),
        FigureBorder = Frozen(0x3A, 0x42, 0x5C),
        FigureBg = Frozen(0x11, 0x15, 0x22),
        FooterBg = Frozen(0x11, 0x15, 0x22),
    };

    private static readonly Inks LightInks = new()
    {
        Text = Frozen(0x1F, 0x1F, 0x23),
        TextMuted = Frozen(0x5C, 0x5F, 0x66),
        Accent = Frozen(0x18, 0x57, 0xC0),
        Heading = Frozen(0x16, 0x33, 0x5E),
        DefTerm = Frozen(0x16, 0x33, 0x5E),
        Citation = Frozen(0x6A, 0x1B, 0x9A),
        Marked = Frozen(0x66, 0xFF, 0xE0, 0x60),
        Success = Frozen(0x1E, 0x7E, 0x4A),
        Warning = Frozen(0xB4, 0x6A, 0x00),
        Danger = Frozen(0xC0, 0x2A, 0x2A),
        Important = Frozen(0x6A, 0x1B, 0x9A),
        CodeBg = Frozen(0x18, 0x00, 0x00, 0x00),
        CodeBorder = Frozen(0x33, 0x00, 0x00, 0x00),
        QuoteBg = Frozen(0x12, 0x00, 0x00, 0x00),
        Hr = Frozen(0x33, 0x00, 0x00, 0x00),
        TableBorder = Frozen(0x40, 0x00, 0x00, 0x00),
        TableHeaderBg = Frozen(0x1F, 0x00, 0x00, 0x00),
        TableAltRowBg = Frozen(0x0E, 0x00, 0x00, 0x00),
        FigureBorder = Frozen(0x33, 0x00, 0x00, 0x00),
        FigureBg = Frozen(0x10, 0x00, 0x00, 0x00),
        FooterBg = Frozen(0x10, 0x00, 0x00, 0x00),
    };

    /// <summary>A style of <paramref name="inks"/>, in the medium of the thread that asks — whose inks and faces any thread may draw.</summary>
    private static PortableStyle Made(Inks inks)
    {
        var medium = WpfMedium.OnThisThread;
        LayoutInk Ink(Brush brush) => medium.Ink(brush);

        return new PortableStyle((family, weight, italic) =>
            medium.Face(Typefaces.Of(Typefaces.Family(family), FontWeight.FromOpenTypeWeight(weight), italic ? FontStyles.Italic : FontStyles.Normal)))
        {
            Text = Ink(inks.Text),
            TextMuted = Ink(inks.TextMuted),
            Accent = Ink(inks.Accent),
            Heading = Ink(inks.Heading),
            DefTerm = Ink(inks.DefTerm),
            Citation = Ink(inks.Citation),
            Marked = Ink(inks.Marked),
            Success = Ink(inks.Success),
            Warning = Ink(inks.Warning),
            Danger = Ink(inks.Danger),
            Important = Ink(inks.Important),
            CodeBg = Ink(inks.CodeBg),
            CodeBorder = Ink(inks.CodeBorder),
            QuoteBg = Ink(inks.QuoteBg),
            Hr = Ink(inks.Hr),
            TableBorder = Ink(inks.TableBorder),
            TableHeaderBg = Ink(inks.TableHeaderBg),
            TableAltRowBg = Ink(inks.TableAltRowBg),
            FigureBg = Ink(inks.FigureBg),
            FigureBorder = Ink(inks.FigureBorder),
            FooterBg = Ink(inks.FooterBg),
            QrDark = Ink(inks.QrDark),
            QrLight = Ink(inks.QrLight),
            BarcodeDark = Ink(inks.BarcodeDark),
            BarcodeLight = Ink(inks.BarcodeLight),
            Series = [.. inks.Series.Select(Ink)],
            Elements = inks.Elements.ToDictionary(element => element.Key, element => Ink(element.Value)),
            C4Person = inks.C4Person is { } person ? Ink(person) : null,
            C4System = inks.C4System is { } system ? Ink(system) : null,
            C4Container = inks.C4Container is { } container ? Ink(container) : null,
            C4Component = inks.C4Component is { } component ? Ink(component) : null,
            C4DeploymentNode = inks.C4DeploymentNode is { } node ? Ink(node) : null,
            C4External = inks.C4External is { } external ? Ink(external) : null,
            C4Boundary = inks.C4Boundary is { } boundary ? Ink(boundary) : null,
            Syntax = kind => SyntaxTokenMap.ResourceKey(kind) is { } key && Application.Current?.TryFindResource(key) is Brush brush
                ? medium.Ink(brush)
                : null,
            TextFont = TextFont,
            MonoFont = MonoFont,
            IconFace = medium.Face(new Typeface(IconCatalog.FluentFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal)),
            Says = Str.Get,
        };
    }

    private static Brush Frozen(byte r, byte g, byte b) => Frozen(0xFF, r, g, b);

    private static Brush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
