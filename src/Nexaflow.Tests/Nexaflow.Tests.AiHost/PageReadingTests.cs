using System.Text;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// Unlimited-OCR in the real host, held to the makers' own reading of the recipe's sample pages — where a machine has
/// them. <c>NEXAFLOW_TEST_PAGE_READING</c> names the published folder (<c>reader\</c>, <c>text-finder\</c>);
/// <c>NEXAFLOW_TEST_PAGE_READING_REFERENCE</c> the recipe's reference (<c>pages\</c>, <c>reader\</c>, <c>text-finder\</c>);
/// <c>NEXAFLOW_TEST_RUNTIME_PACK</c>, when set, runs it on CUDA. The reader is decoded as its makers decode it: greedy,
/// the prompt and the last 128 tokens written in view, no 35-token run written twice within 128.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("ai-host")]
public class PageReadingTests
{
    private static readonly ReplyDecoding Makers = new(WrittenWindow: 128, RepeatBan: new RepeatBan(35, 128), WritesControlTokens: true);

    private static AiHostClient? _client;
    private static ModelSpec? _reader;

    private static ModelSpec? _finder;
    private static string _reference = "";

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_PAGE_READING") is not { Length: > 0 } folder ||
            Environment.GetEnvironmentVariable("NEXAFLOW_TEST_PAGE_READING_REFERENCE") is not { Length: > 0 } reference) return;

        _reference = reference;
        string? pack = Environment.GetEnvironmentVariable("NEXAFLOW_TEST_RUNTIME_PACK") is { Length: > 0 } p ? p : null;
        _client = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe"),
                                                             TestTemp.CreateSubdirectory("nexaflow-aihost-").FullName))
        {
            Environment = new AiHostEnvironment(pack),
        };
        _reader = new ModelSpec("unlimited-ocr", "llama-chat", Path.Combine(folder, "reader"), "unlimited-ocr-q8_0.gguf", null,
                                pack is null ? 0 : 7L << 30, 8192, Projector: "unlimited-ocr-mmproj-f16.gguf",
                                Device: pack is null ? null : 0);
        _finder = new ModelSpec("text-finder", "text-finder", Path.Combine(folder, "text-finder"), "pp-ocrv5-mobile-det.onnx", null, 0, 0);
        await foreach (var _ in _client.SendAsync(id => new LoadModel(id, _reader), TimeSpan.FromMinutes(3))) { }
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private static async Task<(string Text, GenerationEnded Ended)> ReadAsync(string page)
    {
        if (_client is null || _reader is null)
            Assert.Inconclusive("NEXAFLOW_TEST_PAGE_READING and NEXAFLOW_TEST_PAGE_READING_REFERENCE name no page reading on this machine.");
        var turns = new List<ChatTurn> { new(TurnRole.User, "document parsing.", [new ChatMedia(MediaKind.Image, page, null)]) };
        var text = new StringBuilder();
        GenerationEnded? ended = null;
        await foreach (var reply in _client!.SendAsync(id => new Generate(id, _reader!, turns, 8192, RequestPriority.Job, Thinks: false, Decoding: Makers),
                                                        TimeSpan.FromMinutes(2)))
        {
            if (reply is TextDelta delta) text.Append(delta.Text);
            if (reply is GenerationEnded end) ended = end;
        }
        return (text.ToString(), ended!);
    }

    private sealed record Block(string Kind, int[] Box, string Content);

    /// <summary>A reading's blocks — <c>&lt;|det|&gt;kind [x0, y0, x1, y1]&lt;|/det|&gt;content</c>, the content running to
    /// the next block, the first one's opening marker sometimes left out — each with its whitespace set aside.</summary>
    private static List<Block> Blocks(string reading)
        => [.. System.Text.RegularExpressions.Regex.Matches(reading, @"(?:<\|det\|>|^)(\w+) \[(\d+), (\d+), (\d+), (\d+)\]<\|/det\|>(.*?)(?=<\|det\|>|$)",
                                                            System.Text.RegularExpressions.RegexOptions.Singleline)
               .Select(m => new Block(m.Groups[1].Value, [.. Enumerable.Range(2, 4).Select(g => int.Parse(m.Groups[g].Value))],
                                      string.Join(" ", m.Groups[6].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))))];

    /// <summary>Characters to change, over the reference's length — after the 8-bit build's few differences in the
    /// makers' bf16 reading, nothing more.</summary>
    private static double ErrorRate(string read, string reference)
    {
        var previous = Enumerable.Range(0, read.Length + 1).ToArray();
        for (int i = 1; i <= reference.Length; i++)
        {
            var current = new int[read.Length + 1];
            current[0] = i;
            for (int j = 1; j <= read.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + (reference[i - 1] == read[j - 1] ? 0 : 1));
            previous = current;
        }
        return reference.Length == 0 ? read.Length : (double)previous[read.Length] / reference.Length;
    }

    /// <summary>Where two readings part, with a little of each from there.</summary>
    private static string Divergence(string read, string reference)
    {
        int at = 0;
        while (at < read.Length && at < reference.Length && read[at] == reference[at]) at++;
        string Around(string s) => s.Substring(Math.Max(0, at - 40), Math.Min(s.Length, at + 80) - Math.Max(0, at - 40));
        return $"read:      …{Around(read)}…\nreference: …{Around(reference)}…";
    }

    /// <summary>Each page read into what the makers' reading says, in blocks of the kinds theirs are, where theirs are — to
    /// the 8-bit build's differences from their bf16 run: a box's edge a few thousandths over, a citation written as maths or
    /// as text, a column now and then split in two blocks (where the blocks are not one for one, their boxes are not
    /// compared).</summary>
    [TestMethod]
    public async Task EachSamplePage_ReadsAsTheMakersReadIt()
    {
        foreach (var page in SamplePages())
        {
            string name = Path.GetFileNameWithoutExtension(page);
            var (text, ended) = await ReadAsync(page);
            var read = Blocks(text);
            var theirs = Blocks(File.ReadAllText(Path.Combine(_reference, "reader", name + ".md")));

            Assert.AreEqual(StopReason.EndOfTurn, ended.Reason, $"{name} ends where the page does ({ended})");
            CollectionAssert.AreEquivalent(theirs.Select(b => b.Kind).Distinct().ToList(), read.Select(b => b.Kind).Distinct().ToList(),
                                           $"{name}'s kinds of block — read:\n{text}");
            string said = string.Join("\n", read.Select(b => b.Content)), theySaid = string.Join("\n", theirs.Select(b => b.Content));
            double rate = ErrorRate(said, theySaid);
            Assert.IsTrue(rate < 0.02, $"{name} says what the makers' reading says but for {rate:P2} of its characters, first at\n{Divergence(said, theySaid)}");
            if (read.Count != theirs.Count) continue;
            for (int i = 0; i < read.Count; i++)
                Assert.IsTrue(read[i].Kind == theirs[i].Kind && read[i].Box.Zip(theirs[i].Box).All(e => Math.Abs(e.First - e.Second) <= 15),
                              $"{name}'s {read[i].Kind} {i} stands at [{string.Join(", ", read[i].Box)}], their {theirs[i].Kind} at [{string.Join(", ", theirs[i].Box)}]");
        }
    }

    /// <summary>A page long enough that the reader writes past its window many times over reads to its end once —
    /// llama.cpp's own attention, every token seeing all that was written, reads its passages again.</summary>
    [TestMethod]
    public async Task APageLongerThanTheWindow_IsReadToItsEndOnce()
    {
        var (text, ended) = await ReadAsync(Path.Combine(_reference, "pages", "small-print.png"));
        string reference = File.ReadAllText(Path.Combine(_reference, "reader", "small-print.md"));

        Assert.IsTrue(ended.GeneratedTokens > 2000, $"the page is read past the window ({ended.GeneratedTokens} tokens)");
        Assert.IsTrue(text.Length < reference.Length * 1.02, $"read once: {text.Length} characters against the makers' {reference.Length}");
    }

    private sealed record FoundText(int PictureWidth, int PictureHeight, int Width, int Height, List<TextRegion> Regions);

    private static FoundText Found(string name)
        => System.Text.Json.JsonSerializer.Deserialize<FoundText>(File.ReadAllText(Path.Combine(_reference, "text-finder", name + ".json")),
                                                                   new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

    private static IEnumerable<string> Pictures()
    {
        if (_client is null) Assert.Inconclusive("NEXAFLOW_TEST_PAGE_READING and NEXAFLOW_TEST_PAGE_READING_REFERENCE name no page reading on this machine.");
        return Directory.GetFiles(Path.Combine(_reference, "text-finder"), "*.json").Select(Path.GetFileNameWithoutExtension).Order()!;
    }

    private static IEnumerable<string> SamplePages()
    {
        if (_client is null || _reader is null)
            Assert.Inconclusive("NEXAFLOW_TEST_PAGE_READING and NEXAFLOW_TEST_PAGE_READING_REFERENCE name no page reading on this machine.");
        return Directory.GetFiles(Path.Combine(_reference, "pages"), "*.png").Order();
    }

    /// <summary>The detector's own maps read into the regions PaddleOCR's box-at-a-time post-processing reads, in whatever
    /// order.</summary>
    [TestMethod]
    public void TheMakersMaps_ReadIntoTheMakersRegions()
    {
        foreach (var name in Pictures())
        {
            var reference = Found(name);
            var bytes = File.ReadAllBytes(Path.Combine(_reference, "text-finder", name + ".prob"));
            var map = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes);
            var regions = Nexaflow.AiHost.Reading.TextFinding.Regions(map, reference.Width, reference.Height);

            Assert.AreEqual(reference.Regions.Count, regions.Count, $"{name}'s regions");
            foreach (var (ours, theirs) in regions.OrderBy(r => r.Y).ThenBy(r => r.X).Zip(reference.Regions.OrderBy(r => r.Y).ThenBy(r => r.X)))
                Assert.IsTrue(Math.Abs(ours.X - theirs.X) < 1e-4 && Math.Abs(ours.Y - theirs.Y) < 1e-4 && Math.Abs(ours.Width - theirs.Width) < 1e-4
                              && Math.Abs(ours.Height - theirs.Height) < 1e-4 && Math.Abs(ours.Score - theirs.Score) < 1e-4,
                              $"{name}: {ours} against {theirs}");
        }
    }

    /// <summary>A picture through the host finds the text the makers' detector finds there: as many regions, to a few, each
    /// of theirs overlapped by one of ours — the picture brought to size by a bilinear resize in floating point, where
    /// OpenCV's runs in fixed point, which moves a region's faintest edge and can tip one scored at the cut either way; one
    /// scored clearly over it is always found.</summary>
    [TestMethod]
    public async Task APicture_HasTheTextTheMakersDetectorFinds()
    {
        foreach (var name in Pictures())
        {
            var reference = Found(name);
            TextFoundInPicture? found = null;
            await foreach (var reply in _client!.SendAsync(id => new FindTextInPicture(id, _finder!, Path.Combine(_reference, "text-finder", name + ".rgba"),
                                                                                        reference.PictureWidth, reference.PictureHeight), TimeSpan.FromMinutes(1)))
                if (reply is TextFoundInPicture text) found = text;

            Assert.IsTrue(Math.Abs(found!.Regions.Count - reference.Regions.Count) <= Math.Max(1, reference.Regions.Count / 20),
                          $"{name}: {found.Regions.Count} regions against the detector's {reference.Regions.Count}");
            foreach (var theirs in reference.Regions.Where(r => r.Score >= 0.7f))
                Assert.IsTrue(found.Regions.Any(ours => Overlap(ours, theirs) > 0.7), $"{name}: nothing found where the detector found {theirs}");
        }

        static double Overlap(TextRegion a, TextRegion b)
        {
            double w = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X), h = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
            double both = Math.Max(0, w) * Math.Max(0, h);
            return both / (a.Width * a.Height + b.Width * b.Height - both);
        }
    }

    /// <summary>A picture prepared as the host prepares it gives, through the detector, the makers' map — to the resize: their
    /// picture is brought to size by OpenCV in fixed point, rounded as its vector code rounds, ours in floating point, so a
    /// probability is near theirs, a few at a region's edge further, and one pixel in a few thousand on the other side of
    /// the 0.3 a region is made of.</summary>
    [TestMethod]
    public void APicturePreparedAsTheHostPreparesIt_GivesTheMakersMap()
    {
        var spec = _finder;
        foreach (var name in Pictures())
        {
            using var finder = (Nexaflow.AiHost.Reading.TextFinder)new Nexaflow.AiHost.Reading.TextFinderRunner().Load(spec!);
            var reference = Found(name);
            var rgba = File.ReadAllBytes(Path.Combine(_reference, "text-finder", name + ".rgba"));
            var (input, width, height) = Nexaflow.AiHost.Reading.TextFinding.Prepare(rgba, reference.PictureWidth, reference.PictureHeight);
            var map = finder.Find(input, width, height);
            var theirs = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(Path.Combine(_reference, "text-finder", name + ".prob"))).ToArray();

            Assert.AreEqual((reference.Width, reference.Height), (width, height), $"{name}'s size");
            double mean = map.Zip(theirs).Average(p => Math.Abs(p.First - p.Second));
            int flipped = map.Zip(theirs).Count(p => p.First > 0.3f != p.Second > 0.3f);
            Assert.IsTrue(mean < 1e-3, $"{name}: on average {mean:F5} from the makers' map");
            Assert.IsTrue(flipped < map.Length / 2000, $"{name}: {flipped} of {map.Length} pixels are text in one map and not the other");
        }
    }
}
