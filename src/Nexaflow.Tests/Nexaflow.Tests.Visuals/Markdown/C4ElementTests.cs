using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Graphs;
using Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The C4 element card: its stereotype text, the footprint the layout reserves for it, the
/// theme-derived colour grading, and the painter that fills that footprint. One card serves both C4
/// pipelines (a graph node and a sequence participant), so these are the tests that keep the
/// measurement and the painting agreeing.
/// </summary>
[TestClass]
public class C4ElementTests
{
    // ── Stereotype ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Stereotype_NamesTheKind()
    {
        Assert.AreEqual("[Person]",          new C4ElementInfo { Kind = C4ElementKind.Person }.Stereotype());
        Assert.AreEqual("[Software System]", new C4ElementInfo { Kind = C4ElementKind.System }.Stereotype());
        Assert.AreEqual("[Container]",       new C4ElementInfo { Kind = C4ElementKind.Container }.Stereotype());
        Assert.AreEqual("[Component]",       new C4ElementInfo { Kind = C4ElementKind.Component }.Stereotype());
        Assert.AreEqual("[Deployment Node]", new C4ElementInfo { Kind = C4ElementKind.DeploymentNode }.Stereotype());
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Stereotype_AddsTechnologyAndExternal()
    {
        Assert.AreEqual("[Container: Spring MVC]",
            new C4ElementInfo { Kind = C4ElementKind.Container, Technology = "Spring MVC" }.Stereotype());
        Assert.AreEqual("[Person (external)]",
            new C4ElementInfo { Kind = C4ElementKind.Person, External = true }.Stereotype());
        Assert.AreEqual("[Container (external): C#, Xamarin]",
            new C4ElementInfo { Kind = C4ElementKind.Container, External = true, Technology = " C#, Xamarin " }.Stereotype());
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Stereotype_OverrideReplacesTheKind_AndHideSuppressesIt()
    {
        Assert.AreEqual("[boundary]",
            new C4ElementInfo { Kind = C4ElementKind.System, StereotypeOverride = "boundary" }.Stereotype());
        Assert.AreEqual(string.Empty,
            new C4ElementInfo { Kind = C4ElementKind.Container, Technology = "Java", HideStereotype = true }.Stereotype());
    }

    // ── Metrics ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Measure_NeverNarrowerThanMinOrWiderThanMax()
    {
        var (narrow, _) = C4ElementMetrics.Measure("A", new C4ElementInfo { HideStereotype = true });
        Assert.AreEqual(C4ElementMetrics.MinW, narrow, 1e-9);

        var wide = new C4ElementInfo { Description = new string('x', 500) };
        var (w, _) = C4ElementMetrics.Measure(new string('y', 500), wide);
        Assert.AreEqual(C4ElementMetrics.MaxW, w, 1e-9);
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Measure_DescriptionAddsRows()
    {
        var bare = new C4ElementInfo { Kind = C4ElementKind.Container };
        var described = new C4ElementInfo
        {
            Kind = C4ElementKind.Container,
            Description = "Delivers the static content and the Internet banking single page application.",
        };
        double bareH = C4ElementMetrics.Measure("Web Application", bare).h;
        double descH = C4ElementMetrics.Measure("Web Application", described).h;
        Assert.IsTrue(descH > bareH + C4ElementMetrics.DescRowH,
            $"a wrapped description should add more than one row: {bareH} -> {descH}");
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Measure_HideStereotypeDropsItsRow()
    {
        var shown  = new C4ElementInfo { Kind = C4ElementKind.Container };
        var hidden = new C4ElementInfo { Kind = C4ElementKind.Container, HideStereotype = true };
        Assert.AreEqual(
            C4ElementMetrics.Measure("API", shown).h - C4ElementMetrics.MetaRowH,
            C4ElementMetrics.Measure("API", hidden).h, 1e-9);
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Measure_ShapeAddsItsOwnAllowance()
    {
        var box = new C4ElementInfo { Kind = C4ElementKind.Person, Shape = C4ElementShape.Box };
        double boxH = C4ElementMetrics.Measure("Customer", box).h;

        foreach (var (shape, allowance) in new[]
        {
            (C4ElementShape.Person,         C4ElementMetrics.PersonHeadH),
            (C4ElementShape.PersonOutline,  C4ElementMetrics.PersonHeadH),
            (C4ElementShape.PersonPortrait, C4ElementMetrics.PortraitH),
            (C4ElementShape.Database,       2 * C4ElementMetrics.DbCapH),
        })
        {
            var info = new C4ElementInfo { Kind = C4ElementKind.Person, Shape = shape };
            Assert.AreEqual(boxH + allowance, C4ElementMetrics.Measure("Customer", info).h, 1e-9, $"{shape}");
            Assert.AreEqual(allowance, C4ElementMetrics.ShapeAllowance(shape), 1e-9, $"{shape}");
        }
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Measure_QueueIsWiderForItsRoundedEnds()
    {
        var box   = new C4ElementInfo { Kind = C4ElementKind.Container, Technology = "Kafka" };
        var queue = new C4ElementInfo { Kind = C4ElementKind.Container, Technology = "Kafka", Shape = C4ElementShape.Queue };
        Assert.IsTrue(C4ElementMetrics.Measure("Events", queue).w > C4ElementMetrics.Measure("Events", box).w);
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Copy_CarriesEveryPropertyIncludingTags()
    {
        var original = new C4ElementInfo
        {
            Kind = C4ElementKind.Component, Shape = C4ElementShape.Database, External = true,
            Technology = "JDBC", Description = "Stores things", StereotypeOverride = "thing",
            HideStereotype = true, FillColor = "#111", FontColor = "#222", BorderColor = "#333",
            BorderStyle = EdgeStyle.Dashed, BorderThickness = 4,
        };
        original.Tags.Add("v1");

        var copy = original.Copy();
        Assert.AreEqual(original.Kind, copy.Kind);
        Assert.AreEqual(original.Shape, copy.Shape);
        Assert.IsTrue(copy.External);
        Assert.AreEqual("JDBC", copy.Technology);
        Assert.AreEqual("Stores things", copy.Description);
        Assert.AreEqual("thing", copy.StereotypeOverride);
        Assert.IsTrue(copy.HideStereotype);
        Assert.AreEqual("#111", copy.FillColor);
        Assert.AreEqual("#222", copy.FontColor);
        Assert.AreEqual("#333", copy.BorderColor);
        Assert.AreEqual(EdgeStyle.Dashed, copy.BorderStyle);
        Assert.AreEqual(4, copy.BorderThickness, 1e-9);
        CollectionAssert.AreEqual(new[] { "v1" }, copy.Tags);
        copy.Tags.Add("v2");
        Assert.AreEqual(1, original.Tags.Count, "the copy's tag list must be its own");
    }

    // ── Palette ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Palette_GradesTheAbstractionLevelsFromTheAccent()
    {
        var c4 = C4Palette.Resolve(MarkdownPalette.Dark);
        double Lum(Brush b) => DiagramBrushes.Luminance(DiagramBrushes.ColorOf(b, Colors.Black));

        // C4's information is the grading: deeper for the outer abstraction, lighter as you go in.
        Assert.IsTrue(Lum(c4.Person) < Lum(c4.System), "person should be deeper than system");
        Assert.IsTrue(Lum(c4.System) < Lum(c4.Container), "system should be deeper than container");
        Assert.IsTrue(Lum(c4.Container) < Lum(c4.Component), "container should be deeper than component");
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Palette_DerivesFromTheThemeNotFixedHex()
    {
        // The same kind takes a different colour under a different theme — that is the point of
        // deriving the grading rather than shipping C4-PlantUML's blues.
        var dark  = C4Palette.Resolve(MarkdownPalette.Dark);
        var light = C4Palette.Resolve(MarkdownPalette.Light);
        Assert.AreNotEqual(
            DiagramBrushes.ColorOf(dark.Container, Colors.Black),
            DiagramBrushes.ColorOf(light.Container, Colors.Black));
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Palette_ThemeTokenWinsOverTheDerivedDefault()
    {
        var pinned = Color.FromRgb(0x43, 0x8D, 0xD5);
        var palette = new MarkdownPalette
        {
            Text = MarkdownPalette.Dark.Text, TextMuted = MarkdownPalette.Dark.TextMuted,
            Accent = MarkdownPalette.Dark.Accent, Heading = MarkdownPalette.Dark.Heading,
            DefTerm = MarkdownPalette.Dark.DefTerm, Citation = MarkdownPalette.Dark.Citation,
            Marked = MarkdownPalette.Dark.Marked, Success = MarkdownPalette.Dark.Success,
            Warning = MarkdownPalette.Dark.Warning, Danger = MarkdownPalette.Dark.Danger,
            Important = MarkdownPalette.Dark.Important, CodeBg = MarkdownPalette.Dark.CodeBg,
            CodeBorder = MarkdownPalette.Dark.CodeBorder, QuoteBg = MarkdownPalette.Dark.QuoteBg,
            Hr = MarkdownPalette.Dark.Hr, TableBorder = MarkdownPalette.Dark.TableBorder,
            TableHeaderBg = MarkdownPalette.Dark.TableHeaderBg, TableAltRowBg = MarkdownPalette.Dark.TableAltRowBg,
            FigureBorder = MarkdownPalette.Dark.FigureBorder, FigureBg = MarkdownPalette.Dark.FigureBg,
            FooterBg = MarkdownPalette.Dark.FooterBg,
            C4Container = DiagramBrushes.Frozen(pinned),
        };

        var c4 = C4Palette.Resolve(palette);
        Assert.AreEqual(pinned, DiagramBrushes.ColorOf(c4.Container, Colors.Black));
        // …and the levels it did not pin still derive.
        Assert.AreNotEqual(pinned, DiagramBrushes.ColorOf(c4.Component, Colors.Black));
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void BrushesFor_LiteralColoursWinOverEverything()
    {
        var c4 = C4Palette.Resolve(MarkdownPalette.Dark);
        var info = new C4ElementInfo
        {
            Kind = C4ElementKind.Container,
            FillColor = "#969", FontColor = "#fff", BorderColor = "#333",
        };
        var (fill, stroke, text) = c4.BrushesFor(info);
        Assert.AreEqual(Color.FromRgb(0x99, 0x66, 0x99), DiagramBrushes.ColorOf(fill, Colors.Black));
        Assert.AreEqual(Color.FromRgb(0x33, 0x33, 0x33), DiagramBrushes.ColorOf(stroke, Colors.Black));
        Assert.AreEqual(Colors.White, DiagramBrushes.ColorOf(text, Colors.Black));
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void BrushesFor_ExternalUsesTheMutedColourWhateverTheKind()
    {
        var c4 = C4Palette.Resolve(MarkdownPalette.Dark);
        var ext = c4.BrushesFor(new C4ElementInfo { Kind = C4ElementKind.Container, External = true }).fill;
        Assert.AreEqual(DiagramBrushes.ColorOf(c4.External, Colors.Black), DiagramBrushes.ColorOf(ext, Colors.Black));
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void BrushesFor_PicksInkForContrastAgainstTheFill()
    {
        // On BOTH palettes, because the trap is a theme whose Text brush matches the fill's own
        // darkness: a deep Person card on the light palette must not take that palette's dark ink.
        foreach (var palette in new[] { MarkdownPalette.Dark, MarkdownPalette.Light })
        {
            var c4 = C4Palette.Resolve(palette);

            double onWhite = DiagramBrushes.Luminance(
                DiagramBrushes.ColorOf(c4.BrushesFor(new C4ElementInfo { FillColor = "#ffffff" }).text, Colors.Red));
            Assert.IsTrue(onWhite < 100, $"white card should take dark ink (got luminance {onWhite})");

            double onBlack = DiagramBrushes.Luminance(
                DiagramBrushes.ColorOf(c4.BrushesFor(new C4ElementInfo { FillColor = "#000000" }).text, Colors.Red));
            Assert.IsTrue(onBlack > 180, $"black card should take light ink (got luminance {onBlack})");

            // And the derived Person fill — the deepest of the grading — must be legible too.
            var person = new C4ElementInfo { Kind = C4ElementKind.Person };
            var (fill, _, ink) = c4.BrushesFor(person);
            double gap = Math.Abs(
                DiagramBrushes.Luminance(DiagramBrushes.ColorOf(ink, Colors.Red)) -
                DiagramBrushes.Luminance(DiagramBrushes.ColorOf(fill, Colors.Red)));
            Assert.IsTrue(gap > 90, $"person card ink too close to its fill (gap {gap})");
        }
    }

    // ── Painter ───────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-elements")]
    public void Painter_FirstChildIsTheOutlineShape() => UiThread.Run(() =>
    {
        // Load-bearing: WpfGraphRenderer.Highlight selects a composite node by restroking Children[0].
        foreach (var shape in Enum.GetValues<C4ElementShape>())
        {
            var info = new C4ElementInfo { Kind = C4ElementKind.Container, Shape = shape, Description = "d" };
            var (w, h) = C4ElementMetrics.Measure("Card", info);
            var cell = (Canvas)C4ElementPainter.Build("Card", info, w, h, C4Palette.Resolve(MarkdownPalette.Dark));
            Assert.IsTrue(cell.Children.Count > 0, $"{shape}: nothing drawn");
            Assert.IsInstanceOfType(cell.Children[0], typeof(Shape), $"{shape}: outline must come first");
        }
    });

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-elements")]
    public void Painter_CellIsExactlyTheMeasuredFootprint() => UiThread.Run(() =>
    {
        var info = new C4ElementInfo
        {
            Kind = C4ElementKind.Container, Technology = "Java, Spring MVC",
            Description = "Delivers the static content and the Internet banking SPA",
        };
        var (w, h) = C4ElementMetrics.Measure("Web Application", info);
        var cell = (Canvas)C4ElementPainter.Build("Web Application", info, w, h, C4Palette.Resolve(MarkdownPalette.Dark));
        Assert.AreEqual(w, cell.Width, 1e-9);
        Assert.AreEqual(h, cell.Height, 1e-9);
    });

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-elements")]
    public void Painter_ShowDescriptionFalseDropsTheProseRow() => UiThread.Run(() =>
    {
        var info = new C4ElementInfo { Kind = C4ElementKind.Container, Description = "prose" };
        var (w, h) = C4ElementMetrics.Measure("Card", info);
        var c4 = C4Palette.Resolve(MarkdownPalette.Dark);

        int With    = Rows((Canvas)C4ElementPainter.Build("Card", info, w, h, c4));
        int Without = Rows((Canvas)C4ElementPainter.Build("Card", info, w, h, c4, showDescription: false));
        Assert.AreEqual(With - 1, Without);

        static int Rows(Canvas cell) => ((StackPanel)cell.Children[1]).Children.Count;
    });

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-elements")]
    public void Painter_DashedBorderStyleReachesTheOutline() => UiThread.Run(() =>
    {
        var info = new C4ElementInfo { Kind = C4ElementKind.Container, BorderStyle = EdgeStyle.Dashed };
        var (w, h) = C4ElementMetrics.Measure("Card", info);
        var cell = (Canvas)C4ElementPainter.Build("Card", info, w, h, C4Palette.Resolve(MarkdownPalette.Dark));
        var outline = (Shape)cell.Children[0];
        Assert.IsNotNull(outline.StrokeDashArray);
        Assert.IsTrue(outline.StrokeDashArray.Count > 0);
    });

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-elements")]
    public void Painter_EmptyLabelAndNoContentStillDraws() => UiThread.Run(() =>
    {
        var info = new C4ElementInfo { HideStereotype = true };
        var (w, h) = C4ElementMetrics.Measure(string.Empty, info);
        Assert.IsNotNull(C4ElementPainter.Build(string.Empty, info, w, h, C4Palette.Resolve(MarkdownPalette.Light)));
    });
}
