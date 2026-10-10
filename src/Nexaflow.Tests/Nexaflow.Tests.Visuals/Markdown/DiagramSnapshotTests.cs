using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Globalization;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Renders every diagram in the sample corpus to a bitmap and compares it against a previously
/// captured one, so a change that was meant to add a diagram type can be shown not to have moved an
/// existing one.
///
/// <para>
/// This exists because the rest of the suite cannot see this class of bug. A unit test asserts what
/// a renderer was asked to draw; it does not notice a label drawn three pixels behind a box, a
/// legend appended below the visible area, or a group silently dropped before it reached the canvas.
/// All three of those shipped through a green suite and were caught here.
/// </para>
///
/// <para>
/// The snapshots are <b>not</b> in the repository, and this is inconclusive without them: text
/// rasterisation depends on the machine's fonts and DPI, so a committed set would fail for reasons
/// that have nothing to do with the change under test. It is a before/after tool, not an absolute
/// one. Use it around a change:
/// </para>
///
/// <code>
/// # on the base commit
/// $env:NEXAFLOW_DIAGRAM_SNAPSHOTS = "C:\snapshots"
/// $env:NEXAFLOW_DIAGRAM_SNAPSHOTS_WRITE = "1"
/// &lt;run this test&gt;                 # captures the "before"
///
/// # after the change
/// $env:NEXAFLOW_DIAGRAM_SNAPSHOTS_WRITE = ""
/// &lt;run this test&gt;                 # fails listing every diagram that moved
/// </code>
///
/// <para>
/// A reported difference is not automatically a regression — a deliberate improvement moves pixels
/// too. It is a prompt to look at the two images and decide, which is the step that is easy to skip
/// without something insisting on it.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("rendering snapshot harness")]
public class DiagramSnapshotTests
{
    private const string Folder = "NEXAFLOW_DIAGRAM_SNAPSHOTS";
    private const string Write  = "NEXAFLOW_DIAGRAM_SNAPSHOTS_WRITE";

    /// <summary>
    /// Graph-family diagrams live in a pan/zoom viewport that collapses to nothing when it is
    /// measured against infinity, so every diagram is hosted at one fixed width. The value only has
    /// to be stable, not correct.
    /// </summary>
    private const double HostWidth = 1100;

    /// <summary>
    /// A mermaid block, closed by a fence standing on a line of its own.
    ///
    /// <para>
    /// Which line it closes on has to be said, because a label may itself be a block of another language — a node
    /// whose words are a formula is written <c>a["```latex E = mc^2"]</c>. Closing on the first three backticks
    /// anywhere would end the diagram in the middle of that label, and what gets drawn is the half of a diagram that
    /// is left, which reads as a diagram with a label nobody closed.
    /// </para>
    /// </summary>
    private static readonly Regex Fence = new(@"^```mermaid[^\n]*\n(.*?\n)^```[ \t\r]*$",
                                              RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.Compiled);

    [TestMethod]
    public void EveryDiagramRendersAsItDidBefore()
    {
        string? folder = Environment.GetEnvironmentVariable(Folder);
        if (string.IsNullOrWhiteSpace(folder))
            Assert.Inconclusive($"set {Folder} to a snapshot folder (and {Write}=1 once, to capture it)");

        bool writing = Environment.GetEnvironmentVariable(Write) is { Length: > 0 } w && w != "0";
        Directory.CreateDirectory(folder!);

        var written  = new List<string>();
        var matched  = new List<string>();
        var missing  = new List<string>();
        var differed = new List<string>();

        UiThread.Run(() =>
        {
            foreach (string path in TestSampleData.Files("markdown").OrderBy(p => p))
            {
                string stem = Path.GetFileNameWithoutExtension(path);
                string text = File.ReadAllText(path);
                int index = 0;

                foreach (Match fence in Fence.Matches(text))
                {
                    index++;
                    foreach (var (theme, palette) in new[]
                    {
                        ("dark",  StyleFormat.Dark),
                        ("light", StyleFormat.Light),
                    })
                    {
                        string name = $"{stem}-{index}-{theme}.png";
                        byte[] png  = Render(fence.Groups[1].Value, palette);
                        string file = Path.Combine(folder!, name);

                        if (writing)
                        {
                            File.WriteAllBytes(file, png);
                            written.Add(name);
                        }
                        else if (!File.Exists(file))
                        {
                            missing.Add(name);
                        }
                        else if (!File.ReadAllBytes(file).AsSpan().SequenceEqual(png))
                        {
                            // Keep the new render beside the old one so the two can be compared.
                            File.WriteAllBytes(Path.Combine(folder!, $"{Path.GetFileNameWithoutExtension(name)}.actual.png"), png);
                            differed.Add(name);
                        }
                        else
                        {
                            matched.Add(name);
                        }
                    }
                }
            }
        });

        if (writing)
        {
            Assert.Inconclusive($"captured {written.Count} snapshots into {folder} — unset {Write} and run again after your change");
            return;
        }

        Assert.AreEqual(0, differed.Count,
            $"{differed.Count} of {differed.Count + matched.Count} diagrams render differently — " +
            $"compare each against its .actual.png and decide whether the change was intended:{Environment.NewLine}" +
            string.Join(Environment.NewLine, differed.Select(d => "  " + d)));

        Assert.AreEqual(0, missing.Count,
            $"no snapshot for {missing.Count} diagram(s); re-capture with {Write}=1:{Environment.NewLine}" +
            string.Join(Environment.NewLine, missing.Take(10).Select(m => "  " + m)));

        Assert.IsTrue(matched.Count > 0, "the sample corpus produced no diagrams at all");
    }

    /// <summary>
    /// Every diagram of a sample, drawn as a reader meets it — a block among blocks in a document — and written out to
    /// be looked at.
    ///
    /// <para>
    /// The snapshots above lay each diagram on its own, against a width of their choosing. That is not the page: in a
    /// document a diagram is laid by the markdown builder, standing as a block, into the room the page has, and what a
    /// label holds is laid again inside that. A difference between the two is exactly the kind worth seeing, and until
    /// now nothing here could draw the second one.
    /// </para>
    /// </summary>
    [TestMethod]
    public void WriteDocumentFigures()
    {
        string? folder = Environment.GetEnvironmentVariable(Folder);
        if (string.IsNullOrWhiteSpace(folder)) Assert.Inconclusive($"set {Folder} to a folder to write the figures to");

        Directory.CreateDirectory(folder!);
        var written = new List<string>();

        // The room the page is given, so that a diagram's fit to a narrow column can be looked at as well as its
        // natural size — which for a diagram that cannot reflow is the only thing that changes about it.
        var width = Environment.GetEnvironmentVariable(Width) is { Length: > 0 } said
                    && double.TryParse(said, CultureInfo.InvariantCulture, out var asked) && asked > 0
                ? asked
                : DocumentWidth;

            var setting = Environment.GetEnvironmentVariable(Setting) is { Length: > 0 } set
                       && double.TryParse(set, CultureInfo.InvariantCulture, out var size) && size > 0
                ? size
                : double.NaN;

        UiThread.Run(() =>
        {
            foreach (string path in TestSampleData.Files("markdown").OrderBy(p => p))
            {
                string stem = Path.GetFileNameWithoutExtension(path);
                string text = File.ReadAllText(path);
                int index = 0;

                foreach (Match fence in Fence.Matches(text))
                {
                    index++;
                    string one = $"```mermaid\n{fence.Groups[1].Value}```\n";
                    string file = Path.Combine(folder!, $"{stem}-{index}-page.png");

                    File.WriteAllBytes(file, Page(one, width, setting));
                    written.Add(Path.GetFileName(file));
                }
            }
        });

        Assert.Inconclusive($"wrote {written.Count} figure(s) to {folder}");
    }

    /// <summary>Names the room to draw the figures in, where they are wanted in something other than a full page.</summary>
    private const string Width = "NEXAFLOW_DIAGRAM_SNAPSHOTS_WIDTH";

    /// <summary>Names the size the page sets body text at, for looking at what a diagram does when the reader zooms.</summary>
    private const string Setting = "NEXAFLOW_DIAGRAM_SNAPSHOTS_TEXT";

    /// <summary>The width a document is drawn at here — a comfortable window, not a column.</summary>
    private const double DocumentWidth = 1300;

    /// <summary><paramref name="markdown"/> drawn as the viewer draws it, as a PNG.</summary>
    private static byte[] Page(string markdown, double width, double text)
    {
        var surface = new MarkdownSurface { Markdown = markdown, Width = width, BaseFontSize = text };

        surface.Measure(new Size(width, double.PositiveInfinity));
        surface.Arrange(new Rect(0, 0, width, surface.DesiredSize.Height));
        surface.UpdateLayout();

        int w = Math.Max(1, (int)Math.Ceiling(width));
        int h = Math.Max(1, (int)Math.Ceiling(surface.DesiredSize.Height));

        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var ground = new DrawingVisual();
        using (var dc = ground.RenderOpen()) dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
        bitmap.Render(ground);
        bitmap.Render(surface);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Renders one fence at the fixed host width and returns the PNG bytes.</summary>
    private static byte[] Render(string source, StyleFormat palette)
    {
        var host = new Border { Width = HostWidth, Child = Alone.Drawn("mermaid", source, palette) };
        host.Measure(new Size(HostWidth, double.PositiveInfinity));
        host.Arrange(new Rect(0, 0, HostWidth, host.DesiredSize.Height));
        host.UpdateLayout();

        int w = Math.Max(1, (int)Math.Ceiling(host.DesiredSize.Width));
        int h = Math.Max(1, (int)Math.Ceiling(host.DesiredSize.Height));

        // An opaque ground, so an unpainted background is a visible difference rather than alpha noise.
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var ground = new DrawingVisual();
        using (var dc = ground.RenderOpen())
            dc.DrawRectangle(palette == StyleFormat.Light ? Brushes.White : Brushes.Black, null, new Rect(0, 0, w, h));
        bitmap.Render(ground);
        bitmap.Render(host);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
