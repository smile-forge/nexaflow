using System;
using System.Linq;
using System.Windows;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Theming;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The document's sizes are ratios of one body size rather than absolutes — that is what lets the shell's text setting
/// and a viewer's zoom move the whole document without flattening it. A ratio is easy to lose in a refactor (an absolute
/// reads perfectly plausibly in isolation), so these pin the two halves that matter: everything scales, and it scales
/// <em>together</em>.
/// <para>Interactive desktop only (WPF elements need an STA thread). Run with
/// <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]   // reads the process-wide shell text size; see TextZoomTests
[CoversNode("markdown-zoom")]
public class MarkdownTypographyTests
{
    private const string Doc = "# Heading\n\nBody text.\n\n`code`\n";

    [TestCleanup]
    public void Cleanup() => TextTypography.BaseFontSize = TextTypography.DefaultBaseFontSize;

    /// <summary>Heading, body and code all move with the body size, and keep their relative sizes.</summary>
    [TestMethod]
    public void EverySize_ScalesWithTheBodySize() => UiThread.Run(() =>
    {
        var (h1, body, code) = Sizes(13.5);
        var (h1Big, bodyBig, codeBig) = Sizes(27.0);   // exactly double

        Assert.AreEqual(body * 2, bodyBig, body * 0.02, "body");
        Assert.AreEqual(h1 * 2, h1Big, h1 * 0.02, "heading");
        Assert.AreEqual(code * 2, codeBig, code * 0.02, "code run");

        Assert.IsTrue(h1 > body, "an h1 is still larger than body");
        Assert.IsTrue(code < body, "a code run is still set below body");
    });

    /// <summary>
    /// With no size of its own the surface follows the shell setting, which is what makes every markdown surface in the
    /// app honour Options without its host wiring anything up.
    /// </summary>
    [TestMethod]
    public void WithNoExplicitSize_TheSurfaceFollowsTheShellSetting() => UiThread.Run(() =>
    {
        TextTypography.BaseFontSize = 21;
        var following = Body(Shown(new MarkdownSurface()));
        var told = Body(Shown(new MarkdownSurface { BaseFontSize = 21 }));

        TextTypography.BaseFontSize = TextTypography.DefaultBaseFontSize;
        var smaller = Body(Shown(new MarkdownSurface()));

        Assert.AreEqual(told, following, 1e-6, "unset, it is set at the shell's size");
        Assert.IsTrue(smaller < following, "and moves when the shell's size does");
    });

    [TestMethod]
    public void ASizeTheHostGivesIsTheSizeItIsSetAt() => UiThread.Run(() =>
    {
        TextTypography.BaseFontSize = 21;

        var told = Body(Shown(new MarkdownSurface { BaseFontSize = 13 }));
        var shell = Body(Shown(new MarkdownSurface()));

        Assert.IsTrue(told < shell, "a host with a zoom of its own says what size, whatever the shell's is");
    });

    // ── Reading the answers ─────────────────────────────────────────────────

    /// <summary>How large the heading, the body and the code run are set at a given body size — by where their baseline falls.</summary>
    private static (double H1, double Body, double Code) Sizes(double body)
    {
        var laid = Laying.Lay(null, Doc, 600, StyleFormat.Dark with { TextSize = body });
        var words = laid.Root.SelfAndDescendants().Where(piece => piece.Words is not null).ToList();

        double Of(string said) => words.First(piece => piece.Words!.Glyphs.Text.Contains(said, StringComparison.Ordinal))
                                       .Words!.Glyphs.Baseline;

        return (Of("Heading"), Of("Body"), Of("code"));
    }

    private static MarkdownSurface Shown(MarkdownSurface surface)
    {
        surface.Markdown = "Body text.\n";
        surface.Measure(new Size(600, 400));
        surface.Arrange(new Rect(0, 0, 600, 400));
        surface.UpdateLayout();
        return surface;
    }

    /// <summary>How large the body is set on a surface.</summary>
    private static double Body(MarkdownSurface surface) =>
        surface.Shown.Laid.Root.SelfAndDescendants().First(piece => piece.Words is { } said && said.Glyphs.Text.Contains("Body"))
               .Words!.Glyphs.Baseline;
}
