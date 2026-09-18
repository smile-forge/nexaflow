using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writes the help page's figures of the diagrams drawn on the shared layout tree — each from the example the page itself
/// shows, and through the picture a reader copies or saves from the block, so the figure is what the app draws. Writing
/// is opt-in: set <c>NEXAFLOW_WRITE_FIGURES</c> to the output folder (the help's <c>images/markdown</c> to refresh them).
/// That the examples draw with nothing wrong is checked every run.
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("writes documentation figures")]
public class MermaidFigureWriter
{
    /// <summary>Each figure: the help page's heading over the example, and the file it is written to.</summary>
    private static readonly (string Heading, string File)[] Figures =
    [
        ("Flowchart", "mermaid-flowchart.png"),
        ("Pie chart", "mermaid-pie.png"),
        ("Venn diagram", "mermaid-venn.png"),
        ("Radar chart", "mermaid-radar.png"),
        ("XY chart", "mermaid-xychart.png"),
        ("Quadrant chart", "mermaid-quadrant.png"),
        ("Ishikawa (fishbone) diagram", "mermaid-ishikawa.png"),
        ("Gantt chart", "mermaid-gantt.png"),
        ("Kanban board", "mermaid-kanban.png"),
        ("Mindmap", "mermaid-mindmap.png"),
        ("Cynefin diagram", "mermaid-cynefin.png"),
        ("Timeline", "mermaid-timeline.png"),
        ("User journey", "mermaid-journey.png"),
        ("Git graph", "mermaid-gitgraph.png"),
        ("Block diagram", "mermaid-block.png"),
        ("Architecture diagram", "mermaid-architecture.png"),
        ("Sankey diagram", "mermaid-sankey.png"),
    ];

    /// <summary>The help pages the diagram examples live on, in the order a reader meets them.</summary>
    private static readonly string[] DiagramPages =
    [
        "DiagramsFlowAndStructure.md",
        "DiagramsPlanning.md",
        "DiagramsCharts.md",
    ];

    [TestMethod]
    public void TheHelpPagesExamplesDrawWithNothingWrong() => UiThread.Run(() =>
    {
        foreach (var (heading, _) in Figures)
        {
            var block = Drawn(Example(heading));
            Assert.AreEqual(0, block.Diagnostics.Count, $"{heading}: {string.Join(" | ", block.Diagnostics.Select(d => d.Message))}");
        }
    });

    [TestMethod]
    public void WriteFigures()
    {
        var folder = Environment.GetEnvironmentVariable("NEXAFLOW_WRITE_FIGURES");
        if (string.IsNullOrEmpty(folder)) Assert.Inconclusive("set NEXAFLOW_WRITE_FIGURES to write the figures");

        Directory.CreateDirectory(folder);

        UiThread.Run(() =>
        {
            foreach (var (heading, file) in Figures)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(Drawn(Example(heading)).Picture()));

                using var stream = File.Create(Path.Combine(folder, file));
                encoder.Save(stream);
            }
        });
    }

    /// <summary>A block drawn as the help page's own figures are: in the dark theme, laid out for the page's width.</summary>
    private static ContentElement Drawn(string source)
    {
        var block = (ContentElement)DiagramRenderer.Render("mermaid", source, MarkdownPalette.Dark);
        block.Measure(new Size(720, double.PositiveInfinity));
        block.Arrange(new Rect(block.DesiredSize));
        return block;
    }

    /// <summary>The <c>mermaid</c> example a diagram help page shows under a heading.</summary>
    private static string Example(string heading)
    {
        foreach (var name in DiagramPages)
        {
            var page = File.ReadAllText(Path.Combine(HelpFolder(), name)).Replace("\r\n", "\n");
            var at = page.IndexOf($"\n## {heading}\n", StringComparison.Ordinal);
            if (at < 0) continue;

            var example = Regex.Match(page[at..], "```mermaid\n(?<source>.*?)\n```", RegexOptions.Singleline);
            Assert.IsTrue(example.Success, $"{name} shows no mermaid example under '{heading}'");
            return example.Groups["source"].Value;
        }

        Assert.Fail($"no diagram help page has a '{heading}' heading");
        return string.Empty;
    }

    /// <summary>
    /// The Markdown feature's help folder, found from the repo root rather than by looking for the folder itself:
    /// the build copies the help's images beside the test, so a folder of that name exists under bin/ too, holding
    /// none of the pages.
    /// </summary>
    private static string HelpFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "Nexaflow.slnx")))
                return Path.Combine(folder.FullName, "src", "Nexaflow.Features", "Nexaflow.Features.Markdown",
                                    "Localization", "en", "help");

        Assert.Fail("the repo root (the folder holding Nexaflow.slnx) is not above the test's folder");
        return string.Empty;
    }
}
