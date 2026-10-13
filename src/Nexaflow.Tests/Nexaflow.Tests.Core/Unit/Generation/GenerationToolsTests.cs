using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Core.Services.Generation;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Core.Unit.Voice;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Tests.Core.Unit.Generation;

/// <summary>
/// The tools that make things: each offered only where the workspace has a model for it, each saving what it made
/// where it was asked — never over a file already there — opening it, and telling the model where it went.
/// </summary>
[TestClass]
[CoversNode("agent-generation-tools")]
public class GenerationToolsTests
{
    private string _home = "";
    private readonly List<string> _opened = [];

    [TestInitialize]
    public void Setup() => Directory.CreateDirectory(_home = Path.Combine(Path.GetTempPath(), "nexaflow-generated-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_home, recursive: true);

    private GeneratedFiles Files => new(kind => Path.Combine(_home, kind.ToString()), path => { _opened.Add(path); return Task.CompletedTask; });

    private static readonly byte[] Drawn = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    [TestMethod]
    public void EachTool_IsOfferedOnlyWhereTheWorkspaceHasAModelForIt()
    {
        string[] Offered(IImageGenerationProvider? images, ITextToSpeechProvider? voice, IVideoGenerationProvider? videos)
            => [.. new GenerationTools(images, null, voice, videos, Files).Tools.Select(t => t.Name)];

        CollectionAssert.AreEqual(Array.Empty<string>(), Offered(null, null, null));
        CollectionAssert.AreEqual(new[] { "generate_image" }, Offered(new Painter(), null, null));
        CollectionAssert.AreEqual(new[] { "generate_image", "generate_speech", "generate_video" }, Offered(new Painter(), new FakeSpeaker(), new Filmer()));
    }

    [TestMethod]
    public async Task APicture_IsSavedUnderItsPrompt_Opened_AndShownToTheModel()
    {
        var painter = new Painter();

        var result = await new GenerateImageTool(painter, null, Files).InvokeAsync(new JsonObject { ["prompt"] = "A red fox, asleep in the snow at dusk", ["shape"] = "landscape" }, default);

        var saved = Path.Combine(_home, "Picture", "A red fox asleep in the.png");
        Assert.IsTrue(result.Success, result.ModelText);
        CollectionAssert.AreEqual(Drawn, File.ReadAllBytes(saved));
        CollectionAssert.AreEqual(new[] { saved }, _opened, "opened as a double-click would");
        CollectionAssert.AreEqual(new[] { saved }, result.Attachments!.ToArray(), "the conversation carries the file");
        CollectionAssert.AreEqual(Drawn, result.Images!.Single().Bytes, "the model sees what it made");
        StringAssert.Contains(result.ModelText, saved);
        StringAssert.Contains(result.ModelText, "a fox, curled up", "what the model said about it is passed on");
        Assert.AreEqual(MediaShape.Landscape, painter.Asked.Single().Shape);
    }

    [TestMethod]
    public async Task APicture_NeverReplacesOneAlreadyThere()
    {
        var tool = new GenerateImageTool(new Painter(), null, Files);

        await tool.InvokeAsync(new JsonObject { ["prompt"] = "fox" }, default);
        var second = await tool.InvokeAsync(new JsonObject { ["prompt"] = "fox" }, default);

        Assert.AreEqual(Path.Combine(_home, "Picture", "fox (2).png"), second.Attachments!.Single());
        Assert.AreEqual(2, Directory.GetFiles(Path.Combine(_home, "Picture")).Length);
    }

    [TestMethod]
    public async Task WithBothQualities_StandardDraws_UnlessHighIsAskedFor_WhichIsApprovedAndAnnouncedFirst()
    {
        var standard = new Painter();
        var high     = new Painter();
        var tool     = new GenerateImageTool(standard, high, Files);
        var asked    = new JsonObject { ["prompt"] = "a fox", ["quality"] = "high" };

        CollectionAssert.AreEqual(new[] { "standard", "high" }, tool.Parameters.Single(p => p.Name == "quality").Choices!.ToArray());
        Assert.AreEqual(ToolSafety.SafeOperation, tool.SafetyOf(new JsonObject { ["prompt"] = "a fox" }));
        Assert.IsNull(tool.Announce(new JsonObject { ["prompt"] = "a fox" }), "a standard picture is just drawn");
        Assert.AreEqual(ToolSafety.RequiresApproval, tool.SafetyOf(asked), "a high-quality picture is slow and dearer");
        Assert.AreEqual(Str.Get("Shell.Generate.Announce.HighQualityPicture"), tool.Announce(asked)!.Target);

        await tool.InvokeAsync(new JsonObject { ["prompt"] = "a fox" }, default);
        await tool.InvokeAsync(asked, default);

        Assert.AreEqual(1, standard.Asked.Count);
        Assert.AreEqual(1, high.Asked.Count);
    }

    [TestMethod]
    public void WithOneQuality_ThereIsNothingToChoose()
    {
        Assert.IsFalse(new GenerateImageTool(new Painter(), null, Files).Parameters.Any(p => p.Name == "quality"));
        var onlyHigh = new GenerateImageTool(null, new Painter(), Files);
        Assert.IsFalse(onlyHigh.Parameters.Any(p => p.Name == "quality"));
        Assert.AreEqual(ToolSafety.RequiresApproval, onlyHigh.SafetyOf(new JsonObject { ["prompt"] = "a fox" }),
                        "the high-quality model is approved first even when it is the only one");
    }

    [TestMethod]
    public async Task AQualityThatTakesNoSources_IsToldSo_WhenTheOtherDoes()
    {
        var source = Path.Combine(_home, "fox.png");
        File.WriteAllBytes(source, Drawn);
        var tool = new GenerateImageTool(new Painter { Sources = 3 }, new Painter { Sources = 0 }, Files);

        StringAssert.Contains(tool.Parameters.Single(p => p.Name == "sources").Description, "Standard takes 3, high 0");
        var result = await tool.InvokeAsync(new JsonObject { ["prompt"] = "night", ["quality"] = "high", ["sources"] = new JsonArray(source) }, default);

        Assert.IsTrue(result.IsError);
    }

    [TestMethod]
    public void TheFileAskedFor_IsAFolderToNameItIn_OrAFileGivenWhatWasMadesExtension()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_home, "art")).FullName;

        Assert.AreEqual(Path.Combine(folder, "a fox.png"), GeneratedFiles.Target(folder, _home, "a fox", ".png"));
        Assert.AreEqual(Path.Combine(_home, "new", "a fox.png"), GeneratedFiles.Target(@"new\", _home, "a fox", ".png"), "a path ending in a separator is a folder");
        Assert.AreEqual(Path.Combine(folder, "cover.png"), GeneratedFiles.Target(Path.Combine(folder, "cover.jpg"), _home, "a fox", ".png"));
        Assert.AreEqual(Path.Combine(_home, "cover.png"), GeneratedFiles.Target("cover", _home, "a fox", ".png"), "a relative path is taken from home");
    }

    [TestMethod]
    [DataRow("A red fox, asleep in the snow at dusk", "A red fox asleep in the")]
    [DataRow("C:\\ <> | ?", "C")]
    [DataRow("…", "generated")]
    public void AFileIsNamedFromTheFirstWordsOfWhatWasAskedFor(string about, string name)
        => Assert.AreEqual(name, GeneratedFiles.NameFrom(about));

    [TestMethod]
    public async Task PicturesToWorkFrom_AreOfferedOnlyToAModelThatTakesThem_AndReachIt()
    {
        var source = Path.Combine(_home, "fox.png");
        File.WriteAllBytes(source, Drawn);
        var drawsOnly = new Painter { Sources = 0 };
        var editor    = new Painter { Sources = 2, Masking = true };

        Assert.IsFalse(new GenerateImageTool(drawsOnly, null, Files).Parameters.Any(p => p.Name is "sources" or "mask"));
        CollectionAssert.IsSubsetOf(new[] { "sources", "mask" }, new GenerateImageTool(editor, null, Files).Parameters.Select(p => p.Name).ToArray());

        var result = await new GenerateImageTool(editor, null, Files).InvokeAsync(
            new JsonObject { ["prompt"] = "make it night", ["sources"] = new JsonArray(source), ["mask"] = source }, default);

        Assert.IsTrue(result.Success, result.ModelText);
        var asked = editor.Asked.Single();
        CollectionAssert.AreEqual(Drawn, asked.Sources.Single().Bytes);
        Assert.AreEqual("image/png", asked.Sources.Single().MimeType);
        Assert.IsNotNull(asked.Mask);
    }

    [TestMethod]
    public async Task APictureThatCannotBeRead_OrAFailedDrawing_IsToldToTheModel_AndNothingIsSaved()
    {
        var editor = new Painter { Sources = 1 };
        var missing = await new GenerateImageTool(editor, null, Files).InvokeAsync(
            new JsonObject { ["prompt"] = "night", ["sources"] = new JsonArray(Path.Combine(_home, "nope.png")) }, default);
        var refused = await new GenerateImageTool(new Painter { Refusal = "the picture was withheld: people" }, null, Files).InvokeAsync(
            new JsonObject { ["prompt"] = "a crowd" }, default);

        Assert.IsTrue(missing.IsError);
        StringAssert.Contains(missing.ModelText, "nope.png");
        Assert.AreEqual(0, editor.Asked.Count, "nothing is asked of the model with a source missing");
        Assert.IsTrue(refused.IsError);
        StringAssert.Contains(refused.ModelText, "withheld: people");
        Assert.IsFalse(Directory.Exists(Path.Combine(_home, "Picture")));
        Assert.AreEqual(0, _opened.Count);
    }

    [TestMethod]
    public async Task Text_IsReadOutASentenceAtATime_IntoAWav()
    {
        var voice = new FakeSpeaker();

        var result = await new GenerateSpeechTool(voice, Files).InvokeAsync(
            new JsonObject { ["text"] = "Bonjour à tous. Voici les nouvelles du jour.", ["language"] = "FR" }, default);

        Assert.IsTrue(result.Success, result.ModelText);
        CollectionAssert.AreEqual(new[] { ("Bonjour à tous.", "fr"), ("Voici les nouvelles du jour.", "fr") }, voice.Said.Select(s => (s.Text, s.Language)).ToArray());

        var wav = File.ReadAllBytes(result.Attachments!.Single());
        Assert.AreEqual(".wav", Path.GetExtension(result.Attachments!.Single()));
        Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.AreEqual(44 + voice.Said.Sum(s => s.Text.Length) * FakeSpeaker.SamplesPerChar * 2, wav.Length, "every sentence's audio, in one file");
        Assert.IsNull(result.Images, "a recording is not shown to the model");
    }

    [TestMethod]
    public async Task AClip_IsApprovedFirst_AndMadeAtTheNearestLengthTheModelHas()
    {
        var filmer = new Filmer();
        var tool   = new GenerateVideoTool(filmer, Files);

        Assert.AreEqual(ToolSafety.RequiresApproval, tool.Safety, "a clip costs minutes and money");
        CollectionAssert.AreEqual(new[] { "landscape", "portrait" }, tool.Parameters.Single(p => p.Name == "shape").Choices!.ToArray());
        Assert.AreEqual(4, GenerateVideoTool.Nearest(filmer.Lengths, 5));
        Assert.AreEqual(12, GenerateVideoTool.Nearest(filmer.Lengths, 10), "between two, the longer");

        var result = await tool.InvokeAsync(new JsonObject { ["prompt"] = "waves at night", ["seconds"] = 7, ["shape"] = "square" }, default);

        Assert.IsTrue(result.Success, result.ModelText);
        Assert.AreEqual(new VideoGenerationRequest("waves at night", MediaShape.Landscape, 8), filmer.Asked.Single());
        Assert.AreEqual(Path.Combine(_home, "Video", "waves at night.mp4"), result.Attachments!.Single());
    }

    [TestMethod]
    public void AClip_IsAnnouncedAtTheLengthItWillRun()
    {
        var announced = new GenerateVideoTool(new Filmer(), Files).Announce(new JsonObject { ["prompt"] = "waves", ["seconds"] = 5 })!;

        Assert.AreEqual(Str.Format("Shell.Generate.Announce.VideoFormat", 4), announced.Target);
        Assert.AreEqual(Str.Format("Shell.Generate.Announce.MakingVideoFormat", 4), announced.Doing);
    }

    [TestMethod]
    public async Task AFirstFrame_IsCutAndScaledToTheSizeTheModelStartsFrom()
    {
        var frame = Path.Combine(_home, "wide.png");
        File.WriteAllBytes(frame, Picture(300, 100));
        var filmer = new Filmer { Frame = (64, 128) };

        var result = await new GenerateVideoTool(filmer, Files).InvokeAsync(
            new JsonObject { ["prompt"] = "the sun rises", ["shape"] = "portrait", ["first_frame"] = frame }, default);

        Assert.IsTrue(result.Success, result.ModelText);
        var sent = filmer.Asked.Single().FirstFrame!;
        Assert.AreEqual("image/png", sent.MimeType);
        var decoded = BitmapFrame.Create(new MemoryStream(sent.Bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.AreEqual((64, 128), (decoded.PixelWidth, decoded.PixelHeight));
    }

    private static byte[] Picture(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private sealed class Painter : IImageGenerationProvider
    {
        public int Sources { get; init; } = 1;
        public bool Masking { get; init; }
        public string? Refusal { get; init; }
        public readonly List<ImageGenerationRequest> Asked = [];

        public string Name => "painter";
        public DataResidency Residency => DataResidency.LeavesMachine;
        public int MaxSources => Sources;
        public bool Masks => Masking;
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<GeneratedMedia> GenerateImageAsync(ImageGenerationRequest request, CancellationToken ct = default)
        {
            Asked.Add(request);
            return Refusal is { } refusal
                ? Task.FromException<GeneratedMedia>(new LlmProviderException(refusal))
                : Task.FromResult(new GeneratedMedia(new MediaBytes(Drawn, "image/png"), "a fox, curled up"));
        }
    }

    private sealed class Filmer : IVideoGenerationProvider
    {
        public (int Width, int Height)? Frame { get; init; }
        public readonly List<VideoGenerationRequest> Asked = [];

        public string Name => "filmer";
        public DataResidency Residency => DataResidency.LeavesMachine;
        public IReadOnlyList<int> Lengths => [4, 8, 12];
        public bool StartsFromImages => true;
        public (int Width, int Height)? FirstFrameSize(MediaShape shape) => Frame;
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<GeneratedMedia> GenerateVideoAsync(VideoGenerationRequest request, CancellationToken ct = default)
        {
            Asked.Add(request);
            return Task.FromResult(new GeneratedMedia(new MediaBytes([0, 0, 0, 0x18], "video/mp4")));
        }
    }
}
