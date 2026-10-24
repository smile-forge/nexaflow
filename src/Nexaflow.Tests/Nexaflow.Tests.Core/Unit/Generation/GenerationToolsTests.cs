using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Core.Services.Generation;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Core.Unit.Voice;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Tests.Core.Unit.Generation;

/// <summary>
/// The tools that make things: each offered only where the workspace has a model for it, each working only from what the
/// user has given the assistant and saving only into a folder open in a tab — never over a file already there — or, asked
/// for nowhere, opening what it made as a draft for the user to save; telling the model where it went, or only that it opened.
/// </summary>
[TestClass]
[CoversNode("agent-generation-tools")]
public class GenerationToolsTests
{
    private string _home = "";
    private readonly List<object> _opened = [];

    [TestInitialize]
    public void Setup()
    {
        _home = Path.Combine(TestTemp.Root, "nexaflow-generated-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Context);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_home, recursive: true);

    /// <summary>A folder open in a tab: the one place the tools may save in, and where they may read pictures from.</summary>
    private string Context => Path.Combine(_home, "context");

    private string Drafts => Path.Combine(_home, "drafts");

    private GeneratedFiles Files => new(new PathScope([], [Context]), Drafts, kind => Path.Combine(_home, "offered", kind.ToString()),
                                        made => { _opened.Add(made); return Task.CompletedTask; });

    private static readonly byte[] Drawn = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    [TestMethod]
    public void EachTool_IsOfferedOnlyWhereTheWorkspaceHasAModelForIt()
    {
        string[] Offered(IImageGenerationProvider? images, ITextToSpeechProvider? voice, IVideoGenerationProvider? videos)
            => [.. new GenerationTools(images, null, voice, videos, null, Files).Tools.Select(t => t.Name)];

        CollectionAssert.AreEqual(Array.Empty<string>(), Offered(null, null, null));
        CollectionAssert.AreEqual(new[] { "generate_image" }, Offered(new Painter(), null, null));
        CollectionAssert.AreEqual(new[] { "generate_image", "generate_speech", "generate_video" }, Offered(new Painter(), new FakeSpeaker(), new Filmer()));
        CollectionAssert.AreEqual(new[] { "generate_video" }, new GenerationTools(null, null, null, null, new Filmer(), Files).Tools.Select(t => t.Name).ToArray(),
                                  "a high-quality video model alone offers clips too");
    }

    [TestMethod]
    public async Task APicture_AskedForNowhere_OpensAsADraft_ShownToTheModel_ButNeverGivenIt()
    {
        var painter = new Painter();

        var result = await new GenerateImageTool(painter, null, Files).InvokeAsync(new JsonObject { ["prompt"] = "A red fox, asleep in the snow at dusk", ["shape"] = "landscape" }, default);

        Assert.IsTrue(result.Success, result.ModelText);
        var draft = (DraftFile)_opened.Single();
        Assert.AreEqual("A red fox asleep in the.png", Path.GetFileName(draft.Path), "named from its prompt");
        StringAssert.StartsWith(draft.Path, Drafts, "kept in a temp folder until the user saves it");
        Assert.AreEqual(Path.Combine(_home, "offered", "Picture"), draft.OfferedFolder, "Save as starts in the user's own folder for pictures");
        CollectionAssert.AreEqual(Drawn, File.ReadAllBytes(draft.Path));
        Assert.IsNull(result.Attachments, "a draft is not handed to the conversation");
        Assert.IsFalse(result.ModelText.Contains(draft.Path), "the model is told it opened, not where it is");
        StringAssert.Contains(result.ModelText, "not saved yet");
        CollectionAssert.AreEqual(Drawn, result.Images!.Single().Bytes, "the model sees what it made");
        StringAssert.Contains(result.ModelText, "a fox, curled up", "what the model said about it is passed on");
        Assert.AreEqual(new MediaAspect(3, 2), painter.Asked.Single().Aspect, "landscape, at the model's own proportions for it");
    }

    [TestMethod]
    public async Task APicture_IsAskedForWithWhatItsModelOffers_AndOnlyThat()
    {
        var painter = new Painter
        {
            Settings = new([new(1, 1), new(16, 9), new(9, 16), new(21, 9)])
            {
                Resolutions = ["1K", "2K"], Details = [ImageDetail.Low, ImageDetail.High], Seeds = true, NegativePrompts = true, TransparentBackgrounds = true,
            },
        };
        var tool = new GenerateImageTool(painter, null, Files);

        var offered = tool.Parameters.ToDictionary(p => p.Name);
        Assert.AreEqual("square, landscape, portrait, 1:1, 16:9, 9:16, 21:9", string.Join(", ", offered["shape"].Choices!));
        Assert.AreEqual("1K, 2K", string.Join(", ", offered["resolution"].Choices!));
        Assert.AreEqual("low, high", string.Join(", ", offered["detail"].Choices!));
        Assert.AreEqual("integer", offered["seed"].Type);
        Assert.AreEqual("boolean", offered["transparent_background"].Type);
        Assert.IsTrue(offered.ContainsKey("negative_prompt"));

        var made = await tool.InvokeAsync(new JsonObject
        {
            ["prompt"] = "A lighthouse", ["shape"] = "21:9", ["resolution"] = "2k", ["detail"] = "HIGH", ["seed"] = 42,
            ["negative_prompt"] = " boats ", ["transparent_background"] = true,
        }, default);

        Assert.IsTrue(made.Success, made.ModelText);
        var asked = painter.Asked.Single();
        Assert.AreEqual((new MediaAspect(21, 9), "2K", ImageDetail.High, (int?)42, "boats", true),
                        (asked.Aspect, asked.Resolution, asked.Detail, asked.Seed, asked.NegativePrompt, asked.TransparentBackground),
                        "each as the model names it");

        var refused = await tool.InvokeAsync(new JsonObject { ["prompt"] = "A lighthouse", ["shape"] = "4:3" }, default);
        Assert.IsFalse(refused.Success);
        StringAssert.Contains(refused.ModelText, "21:9", "the refusal says what it does draw");
        Assert.AreEqual(1, painter.Asked.Count, "nothing it does not draw is asked of it");
    }

    [TestMethod]
    public void ASettingNoModelOffers_IsNotOffered_AndOneOnlyHighQualityOffers_SaysSo()
    {
        var standard = new Painter();
        var high     = new Painter { Settings = new([new(1, 1)]) { Seeds = true } };

        var names = new GenerateImageTool(standard, null, Files).Parameters.Select(p => p.Name).ToList();
        CollectionAssert.IsSubsetOf(new[] { "prompt", "shape", "path" }, names);
        CollectionAssert.DoesNotContain(names, "seed");
        CollectionAssert.DoesNotContain(names, "resolution");

        var both = new GenerateImageTool(standard, high, Files).Parameters.ToDictionary(p => p.Name);
        StringAssert.Contains(both["seed"].Description, "High quality only.");
        StringAssert.Contains(both["shape"].Description, "Standard quality: 1:1, 3:2, 2:3; high: 1:1.");
    }

    [TestMethod]
    public async Task APicture_AskedIntoAFolderOpenInATab_IsSavedThere_Opened_AndToldToTheModel()
    {
        var result = await new GenerateImageTool(new Painter(), null, Files).InvokeAsync(
            new JsonObject { ["prompt"] = "fox", ["path"] = Path.Combine(Context, "art") + Path.DirectorySeparatorChar }, default);

        var saved = Path.Combine(Context, "art", "fox.png");
        Assert.IsTrue(result.Success, result.ModelText);
        CollectionAssert.AreEqual(Drawn, File.ReadAllBytes(saved));
        CollectionAssert.AreEqual(new object[] { saved }, _opened, "opened as a double-click would");
        CollectionAssert.AreEqual(new[] { saved }, result.Attachments!.ToArray(), "the conversation carries the file");
        StringAssert.Contains(result.ModelText, saved);
    }

    [TestMethod]
    [DataRow("above", DisplayName = "the folder above the one open")]
    [DataRow("beside", DisplayName = "a folder beside it")]
    [DataRow("climbing", DisplayName = "a '..' climbing out")]
    [DataRow("relative", DisplayName = "a relative path")]
    public async Task APlaceOutsideWhatTheUserGave_IsRefused_BeforeAnythingIsMade(string where)
    {
        var asked = where switch
        {
            "above"    => _home,
            "beside"   => Path.Combine(_home, "contexts", "fox.png"),
            "climbing" => Path.Combine(Context, "..", "fox.png"),
            _          => "fox.png",
        };
        var painter = new Painter();
        var voice   = new FakeSpeaker();
        var filmer  = new Filmer();

        var picture   = await new GenerateImageTool(painter, null, Files).InvokeAsync(new JsonObject { ["prompt"] = "fox", ["path"] = asked }, default);
        var recording = await new GenerateSpeechTool(voice, Files).InvokeAsync(new JsonObject { ["text"] = "Hello.", ["path"] = asked }, default);
        var clip      = await new GenerateVideoTool(filmer, null, Files).InvokeAsync(new JsonObject { ["prompt"] = "waves", ["path"] = asked }, default);

        foreach (var result in new[] { picture, recording, clip }) Assert.IsTrue(result.IsError, result.ModelText);
        Assert.AreEqual(0, painter.Asked.Count + voice.Said.Count + filmer.Asked.Count, "nothing is made to be thrown away");
        Assert.AreEqual(0, _opened.Count);
        Assert.AreEqual(1, Directory.GetFileSystemEntries(_home).Length, "nothing is written anywhere");
    }

    [TestMethod]
    public async Task APicture_NeverReplacesOneAlreadyThere()
    {
        var tool = new GenerateImageTool(new Painter(), null, Files);

        await tool.InvokeAsync(new JsonObject { ["prompt"] = "fox", ["path"] = Context }, default);
        var second = await tool.InvokeAsync(new JsonObject { ["prompt"] = "fox", ["path"] = Context }, default);

        Assert.AreEqual(Path.Combine(Context, "fox (2).png"), second.Attachments!.Single());
        Assert.AreEqual(2, Directory.GetFiles(Context).Length);
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
        var source = Path.Combine(Context, "fox.png");
        File.WriteAllBytes(source, Drawn);
        var tool = new GenerateImageTool(new Painter { Sources = 3 }, new Painter { Sources = 0 }, Files);

        StringAssert.Contains(tool.Parameters.Single(p => p.Name == "sources").Description, "Standard takes 3, high 0");
        var result = await tool.InvokeAsync(new JsonObject { ["prompt"] = "night", ["quality"] = "high", ["sources"] = new JsonArray(source) }, default);

        Assert.IsTrue(result.IsError);
    }

    [TestMethod]
    public void TheFileAskedFor_IsAFolderToNameItIn_OrAFileGivenWhatWasMadesExtension()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Context, "art")).FullName;

        Assert.AreEqual(Path.Combine(folder, "a fox.png"), GeneratedFiles.Target(folder, "a fox", ".png"));
        Assert.AreEqual(Path.Combine(Context, "new", "a fox.png"), GeneratedFiles.Target(Path.Combine(Context, "new") + @"\", "a fox", ".png"), "a path ending in a separator is a folder");
        Assert.AreEqual(Path.Combine(folder, "cover.png"), GeneratedFiles.Target(Path.Combine(folder, "cover.jpg"), "a fox", ".png"));
        Assert.IsNull(GeneratedFiles.Target("cover", "a fox", ".png"), "a relative path names no place");
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
        var source = Path.Combine(Context, "fox.png");
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
    public async Task APictureToWorkFrom_IsReadOnlyWhereTheUserGaveIt_OpenInATab_OrInAFolderOpenInOne()
    {
        var outside = Path.Combine(_home, "fox.png");
        File.WriteAllBytes(outside, Drawn);
        var editor = new Painter { Sources = 1 };

        var refused = await new GenerateImageTool(editor, null, Files).InvokeAsync(
            new JsonObject { ["prompt"] = "night", ["sources"] = new JsonArray(outside) }, default);
        var openAlone = new GeneratedFiles(new PathScope([outside], []), Drafts, _ => _home, _ => Task.CompletedTask);
        var given = await new GenerateImageTool(editor, null, openAlone).InvokeAsync(
            new JsonObject { ["prompt"] = "night", ["sources"] = new JsonArray(outside) }, default);

        Assert.IsTrue(refused.IsError);
        StringAssert.Contains(refused.ModelText, "outside what the user has given you");
        Assert.IsTrue(given.Success, given.ModelText);
        Assert.AreEqual(1, editor.Asked.Count, "only the picture open in a tab reached the model");
    }

    [TestMethod]
    public async Task APictureThatCannotBeRead_OrAFailedDrawing_IsToldToTheModel_AndNothingIsSaved()
    {
        var editor = new Painter { Sources = 1 };
        var missing = await new GenerateImageTool(editor, null, Files).InvokeAsync(
            new JsonObject { ["prompt"] = "night", ["sources"] = new JsonArray(Path.Combine(Context, "nope.png")) }, default);
        var refused = await new GenerateImageTool(new Painter { Refusal = "the picture was withheld: people" }, null, Files).InvokeAsync(
            new JsonObject { ["prompt"] = "a crowd" }, default);

        Assert.IsTrue(missing.IsError);
        StringAssert.Contains(missing.ModelText, "nope.png");
        Assert.AreEqual(0, editor.Asked.Count, "nothing is asked of the model with a source missing");
        Assert.IsTrue(refused.IsError);
        StringAssert.Contains(refused.ModelText, "withheld: people");
        Assert.IsFalse(Directory.Exists(Drafts));
        Assert.AreEqual(0, _opened.Count);
    }

    [TestMethod]
    public async Task Text_IsReadOutASentenceAtATime_IntoAWav()
    {
        var voice = new FakeSpeaker();

        var result = await new GenerateSpeechTool(voice, Files).InvokeAsync(
            new JsonObject { ["text"] = "Bonjour à tous. Voici les nouvelles du jour.", ["language"] = "FR", ["path"] = Context }, default);

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
        var tool   = new GenerateVideoTool(filmer, null, Files);

        Assert.AreEqual(ToolSafety.RequiresApproval, tool.Safety, "a clip costs minutes and money");
        CollectionAssert.AreEqual(new[] { "landscape", "portrait", "16:9", "9:16" }, tool.Parameters.Single(p => p.Name == "shape").Choices!.ToArray());
        Assert.AreEqual(4, GenerateVideoTool.Nearest(filmer.Lengths, 5));
        Assert.AreEqual(12, GenerateVideoTool.Nearest(filmer.Lengths, 10), "between two, the longer");

        var result = await tool.InvokeAsync(new JsonObject { ["prompt"] = "waves at night", ["seconds"] = 7, ["shape"] = "square", ["path"] = Context }, default);

        Assert.IsTrue(result.Success, result.ModelText);
        Assert.AreEqual(new VideoGenerationRequest("waves at night", new MediaAspect(16, 9), 8), filmer.Asked.Single());
        Assert.AreEqual(Path.Combine(Context, "waves at night.mp4"), result.Attachments!.Single());
    }

    [TestMethod]
    public async Task AClip_AtASizeThatMakesOnlySomeLengths_IsMadeAtOneOfThem_AndAnotherIsRefused()
    {
        var filmer = new Filmer
        {
            Made     = [4, 6, 8],
            Settings = new([new(16, 9), new(9, 16)]) { Resolutions = [new("720p"), new("1080p") { OnlyLengths = [8] }], Seeds = true },
        };
        var tool = new GenerateVideoTool(filmer, null, Files);

        var made = await tool.InvokeAsync(new JsonObject { ["prompt"] = "rain on a window", ["resolution"] = "1080P", ["seconds"] = 4, ["path"] = Context }, default);
        Assert.IsFalse(made.Success, "1080p makes 8 seconds only");
        StringAssert.Contains(made.ModelText, "8 seconds only");

        made = await tool.InvokeAsync(new JsonObject { ["prompt"] = "rain on a window", ["resolution"] = "1080p", ["shape"] = "9:16", ["seed"] = 3, ["path"] = Context }, default);
        Assert.IsTrue(made.Success, made.ModelText);
        Assert.AreEqual(new VideoGenerationRequest("rain on a window", new MediaAspect(9, 16), 8) { Resolution = "1080p", Seed = 3 }, filmer.Asked.Single(),
                        "no length asked: the one it makes at that size");
    }

    [TestMethod]
    public void AClip_IsAnnouncedAtTheLengthItWillRun()
    {
        var announced = new GenerateVideoTool(new Filmer(), null, Files).Announce(new JsonObject { ["prompt"] = "waves", ["seconds"] = 5 })!;

        Assert.AreEqual(Str.Format("Shell.Generate.Announce.VideoFormat", 4), announced.Target);
        Assert.AreEqual(Str.Format("Shell.Generate.Announce.MakingVideoFormat", 4), announced.Doing);
    }

    [TestMethod]
    public async Task AFirstFrame_IsCutAndScaledToTheSizeTheModelStartsFrom()
    {
        var frame = Path.Combine(Context, "wide.png");
        File.WriteAllBytes(frame, Picture(300, 100));
        var filmer = new Filmer { Frame = (64, 128) };

        var result = await new GenerateVideoTool(filmer, null, Files).InvokeAsync(
            new JsonObject { ["prompt"] = "the sun rises", ["shape"] = "portrait", ["first_frame"] = frame }, default);

        Assert.IsTrue(result.Success, result.ModelText);
        var sent = filmer.Asked.Single().FirstFrame!;
        Assert.AreEqual("image/png", sent.MimeType);
        var decoded = BitmapFrame.Create(new MemoryStream(sent.Bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.AreEqual((64, 128), (decoded.PixelWidth, decoded.PixelHeight));
    }

    [TestMethod]
    public async Task AClipFromAPicture_IsMadeByTheHighQualityModel_AndOneFromADescriptionByTheFastOne()
    {
        var frame = Path.Combine(Context, "start.png");
        File.WriteAllBytes(frame, Picture(64, 36));
        var fast   = new Filmer { Pictures = false, Made = [5] };
        var finest = new Filmer { Made = [5] };
        var tool   = new GenerateVideoTool(fast, finest, Files);

        Assert.IsTrue(tool.Parameters.Any(p => p.Name == "first_frame"), "the high-quality model starts from a picture, so the tool takes one");

        Assert.IsTrue((await tool.InvokeAsync(new JsonObject { ["prompt"] = "a fox in the snow" }, default)).Success);
        Assert.IsTrue((await tool.InvokeAsync(new JsonObject { ["prompt"] = "the fox sits down", ["first_frame"] = frame }, default)).Success);

        Assert.AreEqual("a fox in the snow", fast.Asked.Single().Prompt, "from a description: the fast model");
        Assert.AreEqual("the fox sits down", finest.Asked.Single().Prompt, "from a picture: the high-quality model");
        Assert.IsNotNull(finest.Asked.Single().FirstFrame);
    }

    [TestMethod]
    public void WhereTheModelForPicturesMakesOtherLengths_TheToolSaysWhich()
    {
        var tool = new GenerateVideoTool(new Filmer { Pictures = false, Made = [5] }, new Filmer { Made = [4, 8] }, Files);

        StringAssert.Contains(tool.Parameters.Single(p => p.Name == "seconds").Description, "5 seconds (4, 8 seconds from a picture)");
    }

    [TestMethod]
    public void WhereNoModelStartsFromAPicture_NoneIsOffered()
    {
        var tool = new GenerateVideoTool(new Filmer { Pictures = false }, null, Files);

        Assert.IsFalse(tool.Parameters.Any(p => p.Name == "first_frame"));
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
        public ImageSettings Settings { get; init; } = new([new(1, 1), new(3, 2), new(2, 3)]);
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
        public bool Pictures { get; init; } = true;
        public IReadOnlyList<int> Made { get; init; } = [4, 8, 12];
        public readonly List<VideoGenerationRequest> Asked = [];

        public string Name => "filmer";
        public DataResidency Residency => DataResidency.LeavesMachine;
        public IReadOnlyList<int> Lengths => Made;
        public bool StartsFromImages => Pictures;
        public VideoSettings Settings { get; init; } = new([new(16, 9), new(9, 16)]);
        public (int Width, int Height)? FirstFrameSize(MediaAspect aspect, string? resolution) => Frame;
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<GeneratedMedia> GenerateVideoAsync(VideoGenerationRequest request, CancellationToken ct = default)
        {
            Asked.Add(request);
            return Task.FromResult(new GeneratedMedia(new MediaBytes([0, 0, 0, 0x18], "video/mp4")));
        }
    }
}
