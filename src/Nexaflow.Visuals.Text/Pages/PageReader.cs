using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Common;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.IO.Common;

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
[LocalDiskOnly("page pictures and the figures cut from them, in the reading's own folder in the app's cache")]
public sealed class PageReader(IPageReading reading, IAIService? ai)
{
    /// <summary>
    /// Reads <paramref name="source"/>'s pages into Markdown, as many at once as page reading reads fastest
    /// (<see cref="IPageReading.PagesAtOnce"/>), each page's picture asked for when its reading begins and let go once its
    /// figures are cut, its figures written to <paramref name="figures"/> and linked as
    /// <paramref name="figuresLink"/>/<c>figure-N.png</c>, numbered in page order. The document so far is reported as each
    /// page is begun and after each block and each figure, with the first page still being read — the pages after it are
    /// left out of it until it is done, so the document grows in order; the whole comes back with each page's own text
    /// beside it. A page whose picture cannot be made is read as blank, so every page keeps its place.
    /// </summary>
    /// <exception cref="InvalidOperationException">Page reading is not ready here, or a page could not be read.</exception>
    public async Task<PagesRead> ReadAsync(PageSource source, string figures, string figuresLink, IProgress<PagesSoFar>? written, CancellationToken ct)
    {
        var read = new List<IReadOnlyList<PageBlock>>();
        for (int p = 0; p < source.Pages; p++) read.Add([]);
        var drawn = new Dictionary<(int Page, int Block), string>();
        int front = 0, figureCount = 0;
        object gate = new();

        // The pages from the first still being read, and up to it: those after it wait their turn to be shown.
        void Report(int? drawing = null)
        {
            if (written is null) return;
            PagesSoFar so;
            lock (gate) so = new PagesSoFar(Math.Min(front + 1, source.Pages), source.Pages,
                                            PageMarkdown.Write(read.Take(front + 1).ToList(), (p, b) => drawn.GetValueOrDefault((p, b))), drawing);
            written.Report(so);
        }

        async Task<string?> Read(int p, CancellationToken stop)
        {
            if (await source.Picture(p, stop) is not { } picture) return null;
            try
            {
                // Each block shown as it is read; the page is what the reading gives back whole.
                var shown = new List<PageBlock>();
                lock (gate) read[p] = shown;
                var blocks = await reading.ReadPageAsync(picture, new Shown(block =>
                {
                    lock (gate) shown.Add(block);
                    if (p == front) Report();
                }), stop);
                lock (gate) read[p] = blocks;
                return picture;
            }
            catch
            {
                Delete(picture);
                throw;
            }
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var inFlight = new Queue<Task<string?>>();
        int begun = 0, atOnce = Math.Max(1, reading.PagesAtOnce);
        try
        {
            for (int p = 0; p < source.Pages; p++)
            {
                while (begun < source.Pages && begun < p + atOnce) inFlight.Enqueue(Read(begun++, stop.Token));
                Report();
                string? picture = await inFlight.Dequeue();
                try
                {
                    if (picture is not null) await CutFiguresAsync(p, picture);
                }
                finally
                {
                    if (picture is not null) Delete(picture);
                }
                lock (gate) front = p + 1;
            }
        }
        finally
        {
            // Stopped or failed part-way: the pages still being read are stopped too, and their pictures let go.
            stop.Cancel();
            while (inFlight.TryDequeue(out var left))
            {
                try { if (await left is { } picture) Delete(picture); }
                catch (Exception) { }
            }
        }
        lock (gate) return new PagesRead(PageMarkdown.Write(read, (p, b) => drawn.GetValueOrDefault((p, b))),
                                         [.. read.Select(blocks => PageMarkdown.Write([blocks], (_, _) => null))]);

        async Task CutFiguresAsync(int p, string picture)
        {
            var figureBlocks = read[p].Select((block, at) => (block, at)).Where(f => f.block.Kind == PageBlockKind.Figure).ToList();
            if (figureBlocks.Count == 0) return;
            Directory.CreateDirectory(figures);
            var cut = await Task.Run(() => PageFigures.Cut(picture, figureBlocks.Select(f => f.block).ToList()), ct);
            for (int f = 0; f < figureBlocks.Count; f++)
            {
                if (cut[f] is not { } png) continue;
                string name = $"figure-{++figureCount}.png";
                await File.WriteAllBytesAsync(Path.Combine(figures, name), png, ct);
                var at = (p, figureBlocks[f].at);
                lock (gate) drawn[at] = $"![figure {figureCount}]({figuresLink}/{name})";
                if (ai is null)
                {
                    Report();
                    continue;
                }
                // The picture shows while the image model draws it again; a diagram drawn cleanly takes its place.
                Report(figureCount);
                if (await DiagramDrawing.DrawAsync(ai, png, ct) is { } diagram)
                    lock (gate) drawn[at] = "```mermaid\n" + diagram + "\n```";
                Report();
            }
        }
    }

    private static void Delete(string picture)
    {
        try { File.Delete(picture); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Each block as it is read, on the reading's own thread and in order.</summary>
    private sealed class Shown(Action<PageBlock> show) : IProgress<PageBlock>
    {
        public void Report(PageBlock value) => show(value);
    }
}

/// <summary>Figures cut from the picture of their page, at its own resolution.</summary>
[LocalDiskOnly("page pictures in the reading's own folder in the app's cache")]
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
