using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Common;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Visuals.Text.Pages;

/// <summary>How far a reading has got: the page it is on, of how many, the document so far, and the figure the
/// workspace's image model is drawing again as a diagram, while it is.</summary>
public sealed record PagesSoFar(int Page, int Pages, string Markdown, int? DrawingFigure = null);

/// <summary>Pages read: the one document they make, and each page's text on its own, its figures left out.</summary>
public sealed record PagesRead(string Markdown, IReadOnlyList<string> Pages);

/// <summary>
/// Pages of writing — pictures of them — read into one Markdown document (<see cref="PageMarkdown"/>). Each page is
/// read by <see cref="IPageReading"/>, its blocks shown as they come; each figure is cut from its page at the page's own
/// resolution and saved beside the document, then — where an <see cref="IAIService"/> is given — shown to the
/// workspace's image model to be drawn again as a Mermaid diagram of its kind (<see cref="DiagramDrawing"/>). A diagram
/// Nexaflow draws as written replaces the picture; any other figure stays one.
/// </summary>
public sealed class PageReader(IPageReading reading, IAIService? ai)
{
    /// <summary>
    /// Reads <paramref name="pages"/> — picture files, in order — into Markdown, its figures written to
    /// <paramref name="figures"/> and linked as <paramref name="figuresLink"/>/<c>figure-N.png</c>. The document so far is
    /// reported after each block and each figure, with the page it has reached; the whole comes back with each page's own
    /// text beside it.
    /// </summary>
    /// <exception cref="InvalidOperationException">Page reading is not ready here, or a page could not be read.</exception>
    public async Task<PagesRead> ReadAsync(IReadOnlyList<string> pages, string figures, string figuresLink, IProgress<PagesSoFar>? written, CancellationToken ct)
    {
        var read = new List<IReadOnlyList<PageBlock>>();
        var drawn = new Dictionary<(int Page, int Block), string>();
        string Markdown() => PageMarkdown.Write(read, (p, b) => drawn.GetValueOrDefault((p, b)));
        int figureCount = 0;

        for (int p = 0; p < pages.Count; p++)
        {
            // Each block shown as it is read; the page is what the reading gives back whole.
            var shown = new List<PageBlock>();
            read.Add(shown);
            var blocks = await reading.ReadPageAsync(pages[p], new Shown(block =>
            {
                shown.Add(block);
                written?.Report(new PagesSoFar(read.Count, pages.Count, Markdown()));
            }), ct);
            read[p] = blocks;

            var figureBlocks = blocks.Select((block, at) => (block, at)).Where(f => f.block.Kind == PageBlockKind.Figure).ToList();
            if (figureBlocks.Count == 0) continue;
            Directory.CreateDirectory(figures);
            var cut = await Task.Run(() => PageFigures.Cut(pages[p], figureBlocks.Select(f => f.block).ToList()), ct);
            for (int f = 0; f < figureBlocks.Count; f++)
            {
                if (cut[f] is not { } png) continue;
                string name = $"figure-{++figureCount}.png";
                await File.WriteAllBytesAsync(Path.Combine(figures, name), png, ct);
                var at = (p, figureBlocks[f].at);
                drawn[at] = $"![figure {figureCount}]({figuresLink}/{name})";
                if (ai is null)
                {
                    written?.Report(new PagesSoFar(read.Count, pages.Count, Markdown()));
                    continue;
                }
                // The picture shows while the image model draws it again; a diagram drawn cleanly takes its place.
                written?.Report(new PagesSoFar(read.Count, pages.Count, Markdown(), figureCount));
                if (await DiagramDrawing.DrawAsync(ai, png, ct) is { } diagram) drawn[at] = "```mermaid\n" + diagram + "\n```";
                written?.Report(new PagesSoFar(read.Count, pages.Count, Markdown()));
            }
        }
        return new PagesRead(Markdown(), [.. read.Select(blocks => PageMarkdown.Write([blocks], (_, _) => null))]);
    }

    /// <summary>Each block as it is read, on the reading's own thread and in order.</summary>
    private sealed class Shown(Action<PageBlock> show) : IProgress<PageBlock>
    {
        public void Report(PageBlock value) => show(value);
    }
}

/// <summary>Figures cut from the picture of their page, at its own resolution.</summary>
public static class PageFigures
{
    /// <summary>Each of <paramref name="figures"/> cut from the page in <paramref name="page"/> as a PNG; null for one
    /// too small to be a picture of anything.</summary>
    public static IReadOnlyList<byte[]?> Cut(string page, IReadOnlyList<PageBlock> figures)
    {
        BitmapSource picture;
        using (var stream = File.OpenRead(page))
            picture = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
        picture.Freeze();
        return [.. figures.Select(f => Cut(picture, f))];
    }

    private static byte[]? Cut(BitmapSource picture, PageBlock figure)
    {
        int left = (int)Math.Floor(figure.Left * picture.PixelWidth), top = (int)Math.Floor(figure.Top * picture.PixelHeight);
        int right = (int)Math.Ceiling(figure.Right * picture.PixelWidth), bottom = (int)Math.Ceiling(figure.Bottom * picture.PixelHeight);
        left = Math.Clamp(left, 0, picture.PixelWidth); right = Math.Clamp(right, left, picture.PixelWidth);
        top = Math.Clamp(top, 0, picture.PixelHeight); bottom = Math.Clamp(bottom, top, picture.PixelHeight);
        if (right - left < 8 || bottom - top < 8) return null;

        var cropped = new CroppedBitmap(picture, new Int32Rect(left, top, right - left, bottom - top));
        cropped.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(cropped));
        using var png = new MemoryStream();
        encoder.Save(png);
        return png.ToArray();
    }
}

/// <summary>
/// A figure drawn again as a Mermaid diagram of its kind by the workspace's image model, and kept only when Nexaflow
/// draws it as written (<see cref="MermaidDiagrams.Troubles"/>). How well depends on the model: a large one keeps a
/// diagram's boxes, labels and connections; a small one may run them together, and a figure that is no diagram — a
/// photograph, a plot of data points, a heat map — it is asked to leave alone.
/// </summary>
public static partial class DiagramDrawing
{
    /// <summary>What the image model is asked.</summary>
    public const string Instruction =
        "This picture is a figure cut from a document. If it is a diagram that Mermaid can draw — a flowchart, sequence, " +
        "class, state, entity-relationship, mindmap, timeline, quadrant, pie, xychart, sankey, block, architecture or " +
        "Venn diagram — write the Mermaid code for a diagram of that same kind that carries every box, label and " +
        "connection in it, in a single fenced mermaid block and nothing else. If it is a photograph, a plot of data " +
        "points Mermaid cannot draw, or anything else, answer only NONE.";

    /// <summary>The figure in <paramref name="png"/> as Mermaid source, or null: no model here sees pictures, it is no
    /// diagram, or what came back is not one Nexaflow draws as written.</summary>
    public static async Task<string?> DrawAsync(IAIService ai, byte[] png, CancellationToken ct)
    {
        string? reply;
        try
        {
            reply = await ai.AskAboutPictureAsync(Instruction, new ContextImage(png, "image/png", "figure"), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }   // the model failed: the figure stays a picture
        return Diagram(reply);
    }

    /// <summary>The Mermaid source in a model's reply — its last fenced mermaid block — when Nexaflow draws it as written.</summary>
    public static string? Diagram(string? reply)
    {
        if (reply is null) return null;
        var fences = Fence().Matches(reply);
        if (fences.Count == 0) return null;
        string source = fences[^1].Groups["source"].Value.Trim();
        return source.Length > 0 && MermaidDiagrams.Troubles(source).Count == 0 ? source : null;
    }

    [GeneratedRegex(@"```mermaid[ \t]*\r?\n(?<source>.*?)```", RegexOptions.Singleline)]
    private static partial Regex Fence();
}
