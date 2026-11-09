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

/// <summary>
/// Pages read: the one document they make, each page's text on its own, its figures left out, and what became of each figure
/// shown to the image model to be drawn again — why one stays a picture is kept, to be looked into.
/// </summary>
public sealed record PagesRead(string Markdown, IReadOnlyList<string> Pages, IReadOnlyList<FigureDrawing>? Drawings = null);

/// <summary>A figure shown to the image model, by its number and its file, and what became of it (<see cref="DiagramVerdict"/>).</summary>
public sealed record FigureDrawing(int Figure, string File, DiagramVerdict Verdict);

/// <summary>
/// Pages of writing — pictures of them — read into one Markdown document (<see cref="PageMarkdown"/>). Each page is
/// read by <see cref="IPageReading"/>, its blocks shown as they come; each figure is cut from its page at the page's own
/// resolution and saved beside the document, then — where an <see cref="IAIService"/> is given — shown to the
/// workspace's image model to be drawn again as a Mermaid diagram of its kind (<see cref="DiagramDrawing"/>). A figure is cut
/// whole — the parts the reading finds inside it are its own (<see cref="PageFigures.Whole"/>) — and described by its caption.
/// A drawing shown to hold what the picture does replaces it, the picture linked under it; any other figure stays one.
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
        var drawings = new List<FigureDrawing>();
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
                lock (gate) read[p] = PageFigures.Whole(blocks);
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
                                         [.. read.Select(blocks => PageMarkdown.Write([blocks], (_, _) => null))], [.. drawings]);

        async Task CutFiguresAsync(int p, string picture)
        {
            var blocks = read[p];
            var figureBlocks = blocks.Select((block, at) => (block, at)).Where(f => f.block.Kind == PageBlockKind.Figure).ToList();
            if (figureBlocks.Count == 0) return;
            Directory.CreateDirectory(figures);
            var cut = await Task.Run(() => PageFigures.Cut(picture, figureBlocks.Select(f => f.block).ToList()), ct);
            for (int f = 0; f < figureBlocks.Count; f++)
            {
                if (cut[f] is not { } png) continue;
                string name = $"figure-{++figureCount}.png";
                await File.WriteAllBytesAsync(Path.Combine(figures, name), png, ct);
                var at = (p, figureBlocks[f].at);

                // Described by its caption where it has one, so a reader who cannot see it — and a translation — has its words.
                string described = PageFigures.CaptionOf(blocks, figureBlocks[f].at) is { } caption ? PageMarkdown.Described(caption) : $"figure {figureCount}";
                lock (gate) drawn[at] = $"![{described}]({figuresLink}/{name})";
                if (ai is null)
                {
                    Report();
                    continue;
                }

                // The picture shows while the image model draws it again; a drawing shown to hold what the picture does takes
                // its place, the picture linked under it so nothing of it is lost.
                Report(figureCount);
                var verdict = await DiagramDrawing.DrawAsync(ai, png, ct);
                lock (gate)
                {
                    drawings.Add(new FigureDrawing(figureCount, name, verdict));
                    if (verdict.Diagram is { } diagram) drawn[at] = "```mermaid\n" + diagram + "\n```\n\n" + $"[{name}]({figuresLink}/{name})";
                }
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

    /// <summary>How much of a block must lie inside a figure for it to be a part of that figure.</summary>
    private const double Inside = 0.9;

    /// <summary>
    /// <paramref name="blocks"/> with each figure whole. The reading finds a figure and, as often, the parts inside it too — each
    /// panel, a label set under one, a letter naming it — and cut one by one they would stand as pictures of their own, the figure
    /// in pieces and its labels lost between them. A figure lying inside another is a part of it, and so is words read inside one,
    /// which the figure shows: both are left to it. Two figures each inside the other are one, the first read. And panels read one
    /// after another with nothing between them, under or over one caption, are that one figure — side by side or one above the
    /// other — cut as the box that holds them all.
    /// </summary>
    public static IReadOnlyList<PageBlock> Whole(IReadOnlyList<PageBlock> blocks)
    {
        var figures = Enumerable.Range(0, blocks.Count).Where(at => blocks[at].Kind == PageBlockKind.Figure).ToList();

        bool PartOfAFigure(int at) =>
            blocks[at].Kind is PageBlockKind.Figure or PageBlockKind.Text or PageBlockKind.Equation or PageBlockKind.Title
            && figures.Any(figure => figure != at && Within(blocks[at], blocks[figure])
                                     && (blocks[at].Kind != PageBlockKind.Figure || !Within(blocks[figure], blocks[at]) || figure < at));

        var kept = Enumerable.Range(0, blocks.Count).Where(at => !PartOfAFigure(at)).Select(at => blocks[at]).ToList();

        var whole = new List<PageBlock>(kept.Count);
        for (int at = 0; at < kept.Count; at++)
        {
            int end = at;
            while (kept[at].Kind == PageBlockKind.Figure && end + 1 < kept.Count && kept[end + 1].Kind == PageBlockKind.Figure) end++;

            bool captioned = end > at && (end + 1 < kept.Count && kept[end + 1].Kind == PageBlockKind.Caption || CaptionsFromAbove(kept, at));
            whole.Add(captioned ? Around(kept.Skip(at).Take(end - at + 1)) : kept[at]);
            if (captioned) at = end;
        }
        return whole;
    }

    /// <summary>
    /// The caption of the figure at <paramref name="figure"/> among <paramref name="blocks"/>, as read: the caption read right after
    /// it, or else one read right before it that captions no figure before that — or null where it has none.
    /// </summary>
    public static string? CaptionOf(IReadOnlyList<PageBlock> blocks, int figure)
    {
        if (figure + 1 < blocks.Count && blocks[figure + 1].Kind == PageBlockKind.Caption) return blocks[figure + 1].Content;
        return CaptionsFromAbove(blocks, figure) ? blocks[figure - 1].Content : null;
    }

    /// <summary>Whether the block read right before <paramref name="figure"/> is a caption over it, rather than the one under the figure before.</summary>
    private static bool CaptionsFromAbove(IReadOnlyList<PageBlock> blocks, int figure) =>
        figure > 0 && blocks[figure - 1].Kind == PageBlockKind.Caption && (figure < 2 || blocks[figure - 2].Kind != PageBlockKind.Figure);

    /// <summary>A figure as large as the box holding all of <paramref name="panels"/>.</summary>
    private static PageBlock Around(IEnumerable<PageBlock> panels)
    {
        var all = panels.ToList();
        return new PageBlock(PageBlockKind.Figure, all.Min(panel => panel.Left), all.Min(panel => panel.Top), all.Max(panel => panel.Right), all.Max(panel => panel.Bottom), "");
    }

    /// <summary>Whether at least <see cref="Inside"/> of <paramref name="part"/> lies inside <paramref name="whole"/>.</summary>
    private static bool Within(PageBlock part, PageBlock whole)
    {
        double area = (part.Right - part.Left) * (part.Bottom - part.Top);
        if (area <= 0) return false;

        double across = Math.Max(0, Math.Min(part.Right, whole.Right) - Math.Max(part.Left, whole.Left));
        double down = Math.Max(0, Math.Min(part.Bottom, whole.Bottom) - Math.Max(part.Top, whole.Top));
        return across * down / area >= Inside;
    }
}

/// <summary>
/// What became of a figure shown to the image model to be drawn again (<see cref="DiagramDrawing"/>).
/// </summary>
/// <param name="Kind">What the image model says the figure is; null where it could not say.</param>
/// <param name="Diagram">The Mermaid source standing in the picture's place; null where the picture stays.</param>
/// <param name="Why">Why the picture stays — or, where it does not, what its drawing was held to.</param>
public sealed record DiagramVerdict(string? Kind, string? Diagram, string Why);

/// <summary>
/// A figure drawn again as a Mermaid diagram by the workspace's image model, and put in the picture's place only once it is shown
/// to hold what the picture does — a drawing that only parses can say something else entirely: a drawing of charged plates as
/// columns of plus signs, a circuit as a flowchart. Each step can only keep the picture:
/// <list type="number">
/// <item>the model says what kind of figure it is, from a list that names what Mermaid cannot draw as well as what it can — a
/// circuit, a sketch, a plot of data — and only a kind Mermaid draws is drawn;</item>
/// <item>the drawing must be one Nexaflow draws as written (<see cref="MermaidDiagrams.Troubles"/>);</item>
/// <item>its labels must be the picture's: what the model reads written in the picture, against the words Nexaflow draws for the
/// diagram (<see cref="DiagramPicture.WordsAsync"/>) — none left out, none added;</item>
/// <item>and shown the two side by side (<see cref="DiagramPicture.SideBySideAsync"/>), in a question of its own, the model must
/// find nothing the picture shows that the drawing leaves out or shows otherwise — asked for what differs, not whether they are
/// the same, which a model drawing its own work too readily says.</item>
/// </list>
/// How often a drawing gets through depends on the model; a picture is never lost for a drawing that does not.
/// </summary>
public static partial class DiagramDrawing
{
    /// <summary>
    /// The kinds of figure Mermaid draws, as the model is asked to name them, each with the kind of Mermaid diagram it is drawn
    /// as: a graph — points joined by lines, a network's — as a flowchart.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Drawable = new Dictionary<string, string>
    {
        ["flowchart"] = "flowchart", ["graph"] = "flowchart", ["sequence"] = "sequence", ["class"] = "class", ["state"] = "state",
        ["er"] = "er", ["mindmap"] = "mindmap", ["timeline"] = "timeline", ["quadrant"] = "quadrant", ["pie"] = "pie",
        ["xychart"] = "xychart", ["sankey"] = "sankey", ["block"] = "block", ["architecture"] = "architecture", ["venn"] = "venn",
    };

    /// <summary>The kinds of figure it does not, which the model is offered so a figure need not be called a diagram to be named.</summary>
    public static readonly IReadOnlyList<string> Undrawable = ["circuit", "schematic", "sketch", "plot", "photograph", "table", "other"];

    /// <summary>What the image model is asked first: what kind of figure it is.</summary>
    public static readonly string KindInstruction =
        "This picture is a figure cut from a document. Say what kind of figure it is with one word from this list, and nothing else: " +
        string.Join(", ", Drawable.Keys.Concat(Undrawable)) + ".";

    /// <summary>What the image model is asked to read: the words written in the picture.</summary>
    public const string LabelsInstruction =
        "Write out every piece of text in this picture — each label, name, letter, number and formula — on a line of its own, exactly " +
        "as it is written, and nothing else. If it holds no text, answer only NONE.";

    /// <summary>What the image model is asked of the picture and its drawing side by side.</summary>
    public const string DifferencesInstruction =
        "On the left, under A, is a figure cut from a document; on the right, under B, a drawing made of it. List every box, symbol, " +
        "label, connection, arrow or arrow's direction that A shows and B leaves out or shows differently, one per line, and nothing " +
        "else. Only what they say counts: not colour, line weight, the font, how a label is typeset — a subscript written out — nor " +
        "where things are placed; and the letters A and B over them are part of neither. If B shows everything A does, answer only NONE.";

    /// <summary>What the image model is asked to draw a figure of <paramref name="kind"/> as.</summary>
    public static string Instruction(string kind) =>
        $"This picture is a figure cut from a document: a {kind}. Write the Mermaid code for a {Drawable[kind]} diagram that carries " +
        "every box, label and connection in it, each label written exactly as the picture writes it, in a single fenced mermaid " +
        "block and nothing else. If Mermaid cannot carry all of it, answer only NONE.";

    /// <summary>The figure in <paramref name="png"/> drawn again, or why it stays a picture.</summary>
    public static async Task<DiagramVerdict> DrawAsync(IAIService ai, byte[] png, CancellationToken ct)
    {
        var picture = new ContextImage(png, "image/png", "figure");

        if (Kind(await AskAsync(ai, KindInstruction, picture, ct)) is not { } kind) return new(null, null, "no kind of figure could be had for it");
        if (!Drawable.ContainsKey(kind)) return new(kind, null, $"its kind, {kind}, is not one Mermaid draws");
        if (Diagram(await AskAsync(ai, Instruction(kind), picture, ct)) is not { } diagram) return new(kind, null, "no diagram Nexaflow draws as written came back");

        if (Lines(await AskAsync(ai, LabelsInstruction, picture, ct)) is not { } written) return new(kind, null, "what is written in it could not be read");
        var (missing, added) = Labels(written, await DiagramPicture.WordsAsync(diagram));
        if (missing.Count > 0) return new(kind, null, $"its drawing leaves out {string.Join(", ", missing)}");
        if (added.Count > 0) return new(kind, null, $"its drawing adds {string.Join(", ", added)}");

        var both = new ContextImage(await DiagramPicture.SideBySideAsync(png, diagram), "image/png", "figure and drawing");
        return Lines(await AskAsync(ai, DifferencesInstruction, both, ct)) switch
        {
            null => new(kind, null, "its drawing could not be compared with it"),
            [] => new(kind, diagram, "its labels and its drawing are the picture's"),
            var differences => new(kind, null, "its drawing differs: " + string.Join("; ", differences)),
        };
    }

    /// <summary>A model's answer, or null where it failed — the figure stays a picture.</summary>
    private static async Task<string?> AskAsync(IAIService ai, string instruction, ContextImage picture, CancellationToken ct)
    {
        try { return await ai.AskAboutPictureAsync(instruction, picture, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }
    }

    /// <summary>The kind a reply names — the first word of it that is one of the kinds offered — or null.</summary>
    public static string? Kind(string? reply) =>
        reply is null ? null
        : Words().Matches(reply.ToLowerInvariant()).Select(word => word.Value).FirstOrDefault(word => Drawable.ContainsKey(word) || Undrawable.Contains(word));

    /// <summary>
    /// A reply listing things a line each: its lines with what a list puts in front of them taken off; none where it says NONE,
    /// and null where there is no reply.
    /// </summary>
    public static IReadOnlyList<string>? Lines(string? reply)
    {
        if (reply is null) return null;
        var lines = reply.Split('\n').Select(line => Listed().Replace(line, "").Trim()).Where(line => line.Length > 0 && !line.StartsWith("```", StringComparison.Ordinal)).ToList();
        return lines is [var only] && only.TrimEnd('.').Equals("NONE", StringComparison.OrdinalIgnoreCase) ? [] : lines;
    }

    /// <summary>
    /// The words written in the picture that its drawing leaves out, and those it draws that the picture never wrote: each word as
    /// its letters and digits alone, whatever case, so a subscript written <c>U_a</c> in one and <c>Ua</c> in the other is the same.
    /// </summary>
    public static (IReadOnlyList<string> Missing, IReadOnlyList<string> Added) Labels(IEnumerable<string> written, IEnumerable<string> drawn)
    {
        var (had, drew) = (Spelled(written), Spelled(drawn));
        return ([.. had.Where(word => !drew.Contains(word))], [.. drew.Where(word => !had.Contains(word))]);

        static HashSet<string> Spelled(IEnumerable<string> lines) =>
            [.. lines.SelectMany(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                     .Select(word => new string([.. word.Where(char.IsLetterOrDigit)]).ToLowerInvariant())
                     .Where(word => word.Length > 0)];
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

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Words();

    [GeneratedRegex(@"^\s*(?:[-*•]|\d+[.)])\s+")]
    private static partial Regex Listed();
}
