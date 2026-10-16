using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Core.Services.Generation;
using Nexaflow.Core.Services.Tools;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.Tools;

/// <summary>
/// Picture selection for the assistant: what a phrase names, or what a box holds, cut out of a picture file into a new PNG
/// beside it — transparent around it, cropped to it — and opened; nothing saved where nothing is found, and refused,
/// saying why, where the tool is not here.
/// </summary>
[TestClass]
[CoversNode("picture-selection")]
public class CutOutToolTests
{
    private string _folder = "";
    private readonly List<string> _shown = [];

    [TestInitialize]
    public void Make() => _folder = Directory.CreateTempSubdirectory("nexaflow-cut-out-tool-").FullName;

    [TestCleanup]
    public void Clean()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private CutOutTool Tool(IPictureSelection selection)
        => new(selection, new GeneratedFiles(_ => Path.Combine(_folder, "made"), path => { _shown.Add(path); return Task.CompletedTask; }));

    /// <summary>A 4×3 picture, each pixel its own colour: red rising across, green down.</summary>
    private static byte[] Picture()
    {
        var rgba = new byte[4 * 3 * 4];
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 4; x++)
                rgba.AsSpan((y * 4 + x) * 4, 4).SetBytes((byte)(x * 60), (byte)(y * 100), 7, 255);
        return rgba;
    }

    private string Png(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, PictureFiles.Encode(Picture(), 4, 3, ".png"));
        return path;
    }

    /// <summary>A mask of the 4×3 picture over the pixels given as (x, y).</summary>
    private static PictureMask Mask(params (int X, int Y)[] pixels)
    {
        var mask = new byte[12];
        foreach (var (x, y) in pixels) mask[y * 4 + x] = 255;
        return new PictureMask(mask, 4, 3, new PictureBox(pixels.Min(p => p.X), pixels.Min(p => p.Y), pixels.Max(p => p.X) + 1, pixels.Max(p => p.Y) + 1));
    }

    [TestMethod]
    public async Task EverythingAPhraseNames_IsCutOutIntoATransparentPngBesideIt_CroppedToIt_AndOpened()
    {
        var selection = new FakeSelection
        {
            Finds = [new PictureFind(0.9, new PictureBox(1, 0, 2, 1), Mask((1, 0))), new PictureFind(0.7, new PictureBox(2, 1, 3, 2), Mask((2, 1)))],
        };
        var original = Png("street.png");

        var result = await Tool(selection).InvokeAsync(new JsonObject { ["picture"] = original, ["what"] = "lamp" }, CancellationToken.None);

        Assert.IsTrue(result.Success, result.ModelText);
        var saved = Path.Combine(_folder, "street lamp.png");
        CollectionAssert.AreEqual(new[] { saved }, _shown.ToArray(), "saved beside the original, and opened");
        var (rgba, width, height) = PictureFiles.Read(saved);
        Assert.AreEqual((2, 2), (width, height), "cropped to the two found");
        CollectionAssert.AreEqual(new byte[] { 60, 0, 7, 255 }, rgba[..4], "the first find's pixel, as it was");
        Assert.AreEqual(0, rgba[7], "transparent where nothing was found");
        CollectionAssert.AreEqual(new byte[] { 120, 100, 7, 255 }, rgba[12..16], "the second find's pixel");
        Assert.AreEqual("lamp", selection.Phrase);
        Assert.AreEqual((4, 3), (selection.MaskWidth, selection.MaskHeight), "masks at the picture's own size");
        StringAssert.Contains(result.ModelText, "2 things");
        Assert.IsTrue(selection.Ended, "the selection is let go");
    }

    [TestMethod]
    public async Task OnlyTheSurest_IsCutOut_WhenAskedSo()
    {
        var selection = new FakeSelection
        {
            Finds = [new PictureFind(0.9, new PictureBox(1, 0, 2, 1), Mask((1, 0))), new PictureFind(0.7, new PictureBox(2, 1, 3, 2), Mask((2, 1)))],
        };

        var result = await Tool(selection).InvokeAsync(new JsonObject { ["picture"] = Png("a.png"), ["what"] = "lamp", ["which"] = "surest" }, CancellationToken.None);

        Assert.IsTrue(result.Success, result.ModelText);
        Assert.AreEqual((1, 1), (PictureFiles.Read(Path.Combine(_folder, "a lamp.png")).Width, PictureFiles.Read(Path.Combine(_folder, "a lamp.png")).Height));
    }

    [TestMethod]
    public async Task ABox_CutsOutTheOneThingInIt()
    {
        var selection = new FakeSelection { Selected = Mask((0, 1), (1, 1), (0, 2)) };

        var result = await Tool(selection).InvokeAsync(new JsonObject { ["picture"] = Png("a.png"), ["box"] = "0,1,2,3" }, CancellationToken.None);

        Assert.IsTrue(result.Success, result.ModelText);
        Assert.AreEqual(new PictureBox(0, 1, 2, 3), selection.Box);
        var (_, width, height) = PictureFiles.Read(Path.Combine(_folder, "a cut out.png"));
        Assert.AreEqual((2, 2), (width, height));
    }

    [TestMethod]
    public async Task NothingFound_SavesNothing_AndSaysSo()
    {
        var result = await Tool(new FakeSelection()).InvokeAsync(new JsonObject { ["picture"] = Png("a.png"), ["what"] = "giraffe" }, CancellationToken.None);

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.ModelText, "nothing was saved");
        Assert.AreEqual(0, _shown.Count);
    }

    [TestMethod]
    public async Task ToolNotDownloaded_SaysWhereToGetIt_AndOpensNothing()
    {
        var selection = new FakeSelection { State = ToolModelsState.NotDownloaded };

        var result = await Tool(selection).InvokeAsync(new JsonObject { ["picture"] = Png("a.png"), ["what"] = "lamp" }, CancellationToken.None);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ModelText, "Settings > Local > Tools");
        Assert.IsFalse(selection.Opened);
    }

    [TestMethod]
    public async Task NeitherPhraseNorBox_IsRefused()
    {
        var result = await Tool(new FakeSelection()).InvokeAsync(new JsonObject { ["picture"] = Png("a.png") }, CancellationToken.None);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ModelText, "'what'");
    }

    private sealed class FakeSelection : IPictureSelection, IPictureSelectionSession
    {
        public IReadOnlyList<PictureFind> Finds { get; init; } = [];
        public PictureMask? Selected { get; init; }
        public string? Phrase { get; private set; }
        public PictureBox? Box { get; private set; }
        public int MaskWidth { get; private set; }
        public int MaskHeight { get; private set; }
        public bool Opened { get; private set; }
        public bool Ended { get; private set; }

        public ToolModelsState State { get; init; } = ToolModelsState.Ready;

        public string ModelsVersion => "fake-1";
        public long DownloadBytes => 0;
        public int DownloadPercent => 0;

        public string? DownloadFailure => null;
        public event EventHandler? Changed { add { } remove { } }
        public void Download() { }
        public int Width { get; private set; }
        public int Height { get; private set; }

        public Task<IPictureSelectionSession> OpenAsync(ReadOnlyMemory<byte> rgba, int width, int height, CancellationToken ct)
        {
            (Opened, Width, Height) = (true, width, height);
            return Task.FromResult<IPictureSelectionSession>(this);
        }

        public Task<PictureMask> SelectAsync(IReadOnlyList<PicturePoint> points, PictureBox? box, int maskWidth, int maskHeight, CancellationToken ct)
        {
            (Box, MaskWidth, MaskHeight) = (box, maskWidth, maskHeight);
            return Task.FromResult(Selected ?? new PictureMask(new byte[maskWidth * maskHeight], maskWidth, maskHeight, null));
        }

        public Task<IReadOnlyList<PictureFind>> FindAsync(string phrase, IReadOnlyList<PictureExample> examples, int maskWidth, int maskHeight, CancellationToken ct)
        {
            (Phrase, MaskWidth, MaskHeight) = (phrase, maskWidth, maskHeight);
            return Task.FromResult(Finds);
        }

        public ValueTask DisposeAsync()
        {
            Ended = true;
            return ValueTask.CompletedTask;
        }
    }
}

file static class Bytes
{
    public static void SetBytes(this Span<byte> span, params byte[] values) => values.CopyTo(span);
}
