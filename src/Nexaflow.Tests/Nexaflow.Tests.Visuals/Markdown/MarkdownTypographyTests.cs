using Markdig.Syntax;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Theming;
using Nexaflow.Visuals.Text.Markdown;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Documents;
using MdMarkdown = Markdig.Markdown;
using System.Collections.Generic;
using System.Windows;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The document's sizes are ratios of one body size rather than the absolutes they used to be — that is
/// what lets the shell's text setting and a viewer's zoom move the whole document without flattening it.
/// A ratio is easy to lose in a refactor (an absolute reads perfectly plausibly in isolation), so these
/// pin the two halves that matter: everything scales, and it scales <em>together</em>.
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

    private static MarkdownDocument Parse(string src) =>
        MdMarkdown.Parse(src, MarkdownPipelineFactory.Default);

    private static MarkdownRenderContext At(double body) =>
        new() { Palette = MarkdownPalette.Dark, BaseFontSize = body };

    /// <summary>Heading, body and code all move with the body size, and keep their relative sizes.</summary>
    [TestMethod]
    public void EverySize_ScalesWithTheBodySize() => UiThread.Run(() =>
    {
        var (h1, body, code) = Measure(13.5);
        var (h1Big, bodyBig, codeBig) = Measure(27.0);   // exactly double

        Assert.AreEqual(body * 2, bodyBig, 1e-6, "body");
        Assert.AreEqual(h1 * 2, h1Big, 1e-6, "heading");
        Assert.AreEqual(code * 2, codeBig, 1e-6, "code run");

        Assert.IsTrue(h1 > body, "an h1 is still larger than body");
        Assert.IsTrue(code < body, "a code run is still set below body");
    });

    /// <summary>With no explicit size the context follows the shell setting, which is what makes every
    /// markdown surface in the app honour Options without its host wiring anything up.</summary>
    [TestMethod]
    public void WithNoExplicitSize_TheContextFollowsTheShellSetting() => UiThread.Run(() =>
    {
        TextTypography.BaseFontSize = 21;
        var ctx = new MarkdownRenderContext { Palette = MarkdownPalette.Dark };

        Assert.AreEqual(21d, ctx.BaseFontSize, 1e-9);
        Assert.AreEqual(21d, ((TextBlock)BlockRenderer.Render(Parse("Body text.\n")[0], "", ctx)).FontSize, 1e-9);
    });

    /// <summary>The flow-document renderer draws the same document, so it must agree size for size —
    /// the two diverging is exactly the drift the shared ratio accessors exist to prevent.</summary>
    [TestMethod]
    public void TheFlowDocumentRenderer_AgreesWithTheBlockRenderer() => UiThread.Run(() =>
    {
        var ctx = At(19.0);
        var doc = MarkdownFlowDocument.Build(Doc, ctx);

        var heading = doc.Blocks.OfType<Paragraph>().First();
        var (h1, _, _) = Measure(19.0);

        Assert.AreEqual(19d, doc.FontSize, 1e-9, "the document's own body size");
        Assert.AreEqual(h1, heading.FontSize, 1e-6, "heading size must match the block renderer's");
    });

    /// <summary>(h1, body, code-run) sizes at a given body size.</summary>
    private static (double H1, double Body, double Code) Measure(double body)
    {
        var ctx = At(body);
        var blocks = Parse(Doc);

        // An h1 renders as a stack (text + underline rule); the text block is the first child.
        var h1 = ((TextBlock)((StackPanel)BlockRenderer.Render(blocks[0], "", ctx)).Children[0]).FontSize;
        var para = (TextBlock)BlockRenderer.Render(blocks[1], "", ctx);
        var codeRun = ((TextBlock)BlockRenderer.Render(blocks[2], "", ctx)).Inlines.OfType<Run>().First();

        return (h1, para.FontSize, codeRun.FontSize);
    }
}
