using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Core.Services.Generation;
using Nexaflow.Core.Services.Tools;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.Tools;

/// <summary>
/// The picture tools for the assistant: a picture file restored into a new one beside it — read upright, at the scale
/// asked, faces repaired unless told not to — and opened; refused, saying why, where the tools are not here.
/// </summary>
[TestClass]
[CoversNode("picture-tools")]
public class RestorePictureToolTests
{
    private string _folder = "";
    private readonly List<string> _shown = [];

    [TestInitialize]
    public void Make() => _folder = Directory.CreateTempSubdirectory("nexaflow-restore-tool-").FullName;

    [TestCleanup]
    public void Clean()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private RestorePictureTool Tool(IPictureTools tools)
        => new(tools, new GeneratedFiles(_ => Path.Combine(_folder, "made"), path => { _shown.Add(path); return Task.CompletedTask; }));

    /// <summary>A 3×2 picture: red, green, blue across the top, white, black, grey beneath.</summary>
    private static readonly byte[] Picture =
        [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255,
         255, 255, 255, 255, 0, 0, 0, 255, 128, 128, 128, 255];

    private string Png(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, PictureFiles.Encode(Picture, 3, 2, ".png"));
        return path;
    }

    [TestMethod]
    public async Task APicture_IsRestoredIntoANewFileBesideIt_AtTheScaleAsked_AndOpened()
    {
        var tools = new Enlarger();
        var original = Png("harbour.png");

        var result = await Tool(tools).InvokeAsync(new JsonObject { ["picture"] = original, ["scale"] = "4" }, CancellationToken.None);

        Assert.IsTrue(result.Success, result.ModelText);
        var saved = Path.Combine(_folder, "harbour restored.png");
        CollectionAssert.AreEqual(new[] { saved }, _shown.ToArray(), "it is saved beside the original, and opened");
        var (rgba, width, height) = PictureFiles.Read(saved);
        Assert.AreEqual((12, 8), (width, height));
        CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255 }, rgba[..4], "the picture the tools made");
        Assert.AreEqual((3, 2, 4, true), (tools.Asked!.Width, tools.Asked.Height, tools.Asked.Scale, tools.Asked.Faces), "faces are repaired unless told not to");
        CollectionAssert.AreEqual(Picture, File.Exists(original) ? PictureFiles.Read(original).Rgba : null, "the original is kept");
    }

    [TestMethod]
    public async Task ACallCanKeepTheFaces_AndSaveAJpegWhereItSays()
    {
        var tools = new Enlarger();
        var into = Path.Combine(_folder, "out", "clean.jpg");

        var result = await Tool(tools).InvokeAsync(new JsonObject { ["picture"] = Png("a.png"), ["scale"] = "1", ["faces"] = "no", ["path"] = into },
                                                   CancellationToken.None);

        Assert.IsTrue(result.Success, result.ModelText);
        Assert.IsFalse(tools.Asked!.Faces);
        Assert.IsTrue(File.Exists(into));
        Assert.AreEqual((3, 2), (PictureFiles.Read(into).Width, PictureFiles.Read(into).Height), "cleaned at its size");
    }

    [TestMethod]
    public void APhotoTurnedByItsExifOrientation_IsReadUpright()
    {
        var path = Path.Combine(_folder, "turned.jpg");
        var source = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgr24, null, new byte[3 * 2 * 3], 3 * 3);
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);   // rotate 90° clockwise to show
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source, null, metadata, null));
        using (var file = File.Create(path)) encoder.Save(file);

        var (_, width, height) = PictureFiles.Read(path);

        Assert.AreEqual((2, 3), (width, height), "a landscape-stored portrait comes out portrait");
    }

    [TestMethod]
    public async Task ToolsNotDownloaded_SayWhereToGetThem_AndRestoreNothing()
    {
        var tools = new Enlarger { State = ToolModelsState.NotDownloaded };

        var result = await Tool(tools).InvokeAsync(new JsonObject { ["picture"] = Png("a.png") }, CancellationToken.None);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ModelText, "Settings > Local > Tools");
        Assert.IsNull(tools.Asked);
    }

    [TestMethod]
    public async Task APictureTooLargeForTheScale_IsRefused()
    {
        var tools = new Enlarger { Takes = false };

        var result = await Tool(tools).InvokeAsync(new JsonObject { ["picture"] = Png("a.png"), ["scale"] = "4" }, CancellationToken.None);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ModelText, "too large");
        Assert.IsNull(tools.Asked);
    }

    /// <summary>Picture tools that make each pixel a block of the scale's side — what the file is made from is plain to see.</summary>
    private sealed class Enlarger : IPictureTools
    {
        public ToolModelsState State { get; set; } = ToolModelsState.Ready;
        public bool Takes { get; set; } = true;
        public PictureRestoreRequest? Asked { get; private set; }
        public long DownloadBytes => 0;
        public int DownloadPercent => 0;
        public event EventHandler? Changed { add { } remove { } }
        public void Download() { }
        public bool Restores(int width, int height, int scale) => Takes;
        public string ModelsVersion => "enlarger";

        public Task<PictureRestoration> RestoreAsync(PictureRestoreRequest request, IProgress<double>? progress, CancellationToken ct)
        {
            Asked = request;
            var source = File.ReadAllBytes(request.Pixels);
            int s = request.Scale, w = request.Width * s, h = request.Height * s;
            var made = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    Array.Copy(source, ((y / s) * request.Width + x / s) * 4, made, (y * w + x) * 4, 4);
            File.WriteAllBytes(request.Into, made);
            return Task.FromResult(new PictureRestoration(w, h, request.Faces ? 1 : 0));
        }
    }
}
