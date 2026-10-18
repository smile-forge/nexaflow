using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Tools;

/// <summary>
/// Page reading as features and the assistant use it (<see cref="IPageReading"/>): PP-OCRv5's text detector and
/// Unlimited-OCR in the AI host (<see cref="OwnedPageReading"/>). The detector's regions say whether a picture is a page
/// (<see cref="IsPage"/>); the reader writes a page as blocks, each opened by a mark naming its kind and box
/// (<see cref="PageMarks"/>), handed on as each is finished.
/// </summary>
public sealed partial class PageReading : LocalToolModels, IPageReading
{
    /// <summary>A page of writing has text over at least this share of it — the sample pages 4 to 29 %, a receipt 6 %;
    /// photographs with a sign, a label or a watermark 0.1 to 1.3 %.</summary>
    public const double PageCoverage = 0.03;

    /// <summary>…in at least this many regions — the pages 17 to 123, a receipt 34; the photographs at most 13, most of
    /// those a neon sign's letters.</summary>
    public const int PageRegions = 15;

    private readonly OwnedPageReading _reading;

    public PageReading(OwnedPageReading reading, IModelAssetStore store, IBackgroundActivityManager? activities) : base(reading, store, activities)
        => _reading = reading;

    public override string Name => Str.Get("Shell.Tools.Reading.Name");

    public override string About => Str.Get("Shell.Tools.Reading.About");

    public int PagesAtOnce => OwnedPageReading.PagesAtOnce;

    /// <summary>Whether text found over <paramref name="regions"/> — in fractions of a picture — makes it a page of
    /// writing rather than a picture with a few words in it.</summary>
    public static bool IsPage(IReadOnlyList<TextRegion> regions)
        => regions.Count >= PageRegions && regions.Sum(r => (double)r.Width * r.Height) >= PageCoverage;

    public async Task<TextOnPicture> FindTextAsync(ReadOnlyMemory<byte> rgba, int width, int height, CancellationToken ct)
    {
        if (width <= 0 || height <= 0 || rgba.Length != (long)width * height * 4)
            throw new InvalidOperationException($"A {width}x{height} picture of {rgba.Length} bytes is not one the text finder reads.");
        string pixels = Path.Combine(Path.GetTempPath(), $"nexaflow-text-{Guid.NewGuid():N}.rgba");
        try
        {
            await File.WriteAllBytesAsync(pixels, rgba, ct);
            var regions = await _reading.FindTextAsync(pixels, width, height, ct);
            return new TextOnPicture([.. regions.Select(r => new PictureBox(r.X * width, r.Y * height, (r.X + r.Width) * width, (r.Y + r.Height) * height))],
                                     IsPage(regions));
        }
        finally
        {
            try { File.Delete(pixels); }
            catch (IOException) { }
        }
    }

    public async Task<IReadOnlyList<PageBlock>> ReadPageAsync(string picture, IProgress<PageBlock>? read, CancellationToken ct)
    {
        var marks = new PageMarks();
        var blocks = new List<PageBlock>();
        void Finished(IEnumerable<PageBlock> done)
        {
            foreach (var block in done)
            {
                blocks.Add(block);
                read?.Report(block);
            }
        }
        // Each piece is handed over on the reply's own thread, in order: Progress<T> would post them out of it.
        await _reading.ReadPageAsync(picture, waiting: read is not null, new Written(piece => Finished(marks.Take(piece))), ct);
        Finished(marks.Finish());
        return blocks;
    }

    private sealed class Written(Action<string> take) : IProgress<string>
    {
        public void Report(string value) => take(value);
    }
}

/// <summary>
/// The blocks in what Unlimited-OCR writes, as it writes them: each opened by <c>&lt;|det|&gt;kind [x0, y0, x1, y1]&lt;|/det|&gt;</c>,
/// its box in thousandths of the page, and running to the next. A block is finished when the next one opens, or when
/// the reply ends. The reader sometimes leaves out the first block's opening mark.
/// </summary>
internal sealed partial class PageMarks
{
    private const string Open = "<|det|>";
    private readonly StringBuilder _text = new();
    private int _from;

    /// <summary>Takes the next piece written; returns the blocks it finished.</summary>
    public IReadOnlyList<PageBlock> Take(string piece)
    {
        _text.Append(piece);
        var done = new List<PageBlock>();
        string text = _text.ToString();
        int next;
        while ((next = text.IndexOf(Open, _from + 1, StringComparison.Ordinal)) > 0)
        {
            if (Block(text[_from..next]) is { } block) done.Add(block);
            _from = next;
        }
        return done;
    }

    /// <summary>The reply has ended: the last block, if it says anything.</summary>
    public IReadOnlyList<PageBlock> Finish() => Block(_text.ToString()[_from..]) is { } block ? [block] : [];

    /// <summary>Every block in a whole reply.</summary>
    public static IReadOnlyList<PageBlock> Read(string reply)
    {
        var marks = new PageMarks();
        return [.. marks.Take(reply), .. marks.Finish()];
    }

    private static PageBlock? Block(string text)
    {
        var m = Mark().Match(text);
        if (!m.Success) return null;
        double At(string edge) => Math.Clamp(int.Parse(m.Groups[edge].ValueSpan) / 999.0, 0, 1);
        return new PageBlock(KindOf(m.Groups["kind"].Value), At("x0"), At("y0"), At("x1"), At("y1"), m.Groups["content"].Value.Trim());
    }

    /// <summary>The reader's kinds of block as a page's: a figure's and a table's captions alike; what runs along a page's
    /// edge — its head, its foot, its number — as its margin; anything else as prose.</summary>
    private static PageBlockKind KindOf(string kind) => kind switch
    {
        "title" => PageBlockKind.Title,
        "equation" => PageBlockKind.Equation,
        "table" => PageBlockKind.Table,
        "image" => PageBlockKind.Figure,
        "image_caption" or "table_caption" => PageBlockKind.Caption,
        "page_footnote" => PageBlockKind.Footnote,
        "header" or "footer" or "page_header" or "page_footer" or "page_number" => PageBlockKind.Margin,
        _ => PageBlockKind.Text,
    };

    [GeneratedRegex(@"^\s*(?:<\|det\|>)?(?<kind>\w+) \[(?<x0>\d+), (?<y0>\d+), (?<x1>\d+), (?<y1>\d+)\]<\|/det\|>(?<content>.*)$", RegexOptions.Singleline)]
    private static partial Regex Mark();
}
