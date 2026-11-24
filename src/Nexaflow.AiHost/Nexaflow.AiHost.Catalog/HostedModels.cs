using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Provisioning;

namespace Nexaflow.AiHost.Catalog;

/// <summary>What a model of the catalog does for the app that asks for it.</summary>
public enum HostedKind
{
    /// <summary>Text, and for some pictures and recordings, in; text out.</summary>
    Language,

    /// <summary>Speech in, text out.</summary>
    SpeechToText,

    /// <summary>Text in, speech out.</summary>
    TextToSpeech,

    /// <summary>A prompt, and for some pictures, in; a picture out.</summary>
    ImageGeneration,

    /// <summary>A prompt, and for some a picture, in; a clip out.</summary>
    VideoGeneration,
}

/// <summary>Which job of its kind a model is made for.</summary>
public enum HostedTier
{
    /// <summary>The everyday job: quick, and good enough.</summary>
    Standard,

    /// <summary>The job where quality is worth the wait — a high-quality picture.</summary>
    HighQuality,
}

/// <summary>The shapes a picture or a clip is made in.</summary>
public enum HostedShape
{
    Square,
    Landscape,
    Portrait,
}

/// <summary>What a tool of the catalog does; the parts it has follow from it (<see cref="HostedRoles.Of(HostedToolKind)"/>).</summary>
public enum HostedToolKind
{
    /// <summary>Restores a picture: Real-ESRGAN's upscalers, and GFPGAN with the detector and parser it remakes faces with.</summary>
    PictureRestoration,

    /// <summary>Picks things out of a picture: SAM 3.1's picture encoder, click decoder, text encoder, exemplar encoder and finder.</summary>
    PictureSelection,

    /// <summary>Reads pages of writing: Unlimited-OCR, and PP-OCRv5's text detector, which finds where a picture holds writing.</summary>
    PageReading,

    /// <summary>Erases from a picture what a mask covers, filling it from what is round it: LaMa's big-lama inpainter.</summary>
    PictureErasing,

    /// <summary>Checks how fit a picture is for a general audience: OwenElliott's image safety classifier, M, on the CPU.</summary>
    PictureChecking,

    /// <summary>Estimates how far each part of a picture lies from the camera — its scene depth — from the one picture: Depth
    /// Anything V2 Small.</summary>
    SceneDepth,
}

/// <summary>
/// One model the host loads as part of a model of the catalog — a language model is one part; listening is a voice activity
/// detector, a recogniser, a transcriber and a turn detector; speaking is a synthesizer for each set of voices it has.
/// </summary>
/// <param name="Role">What it does in its model — <see cref="HostedRoles"/>.</param>
/// <param name="Runner">What in the host loads and runs it.</param>
/// <param name="Folder">Where its files are, relative to the model store.</param>
/// <param name="File">The file it is, for a model that is a single graph.</param>
/// <param name="MemoryBytes">GPU memory it holds loaded — a language model's with no context, its projector at work
/// included; 0 for a part that runs on the CPU.</param>
/// <param name="ContextTokens">The conversation a language model is given where its GPU has the room.</param>
/// <param name="CudaOptions">CUDA options it needs beyond the host's own.</param>
/// <param name="ExecutionProvider">The one runtime it is built for (<see cref="HostCatalog.Cuda"/> or <see cref="HostCatalog.Cpu"/>),
/// or null for either.</param>
/// <param name="Name">What it is called in its model: its role, unless the model has several parts in that role.</param>
/// <param name="Projector">A language model's multimodal projector, in its folder: what it sees and hears through.</param>
/// <param name="BytesPerToken">What each token of a language model's context holds on the GPU.</param>
/// <param name="LoadsOnDemand">A part of a model kept loaded that is not kept with it, but loaded by the host the first
/// time a request uses it — a voice for a language seldom spoken.</param>
/// <param name="Layers">How many layers a language model's GGUF has, as its header says: what it is cut into where only part
/// of it fits on a GPU; 0 where it is never cut.</param>
/// <param name="BatchBytesPerToken">What more a language model holds on the GPU for each token it reads in a step past
/// <see cref="ModelSpec.DefaultBatch"/>, as measured; 0 where that is not known, and it reads in steps of that many.</param>
/// <param name="BytesPerSequence">What more a language model holds on the GPU for each sequence its context holds past
/// <see cref="ModelSpec.DefaultSequences"/>, held in 16 bits, as measured — the window of what it read that each keeps; 0 where
/// that is not known, and it holds that many.</param>
/// <param name="BackgroundBytes">What a language model's context for work nobody is watching holds on the GPU while it is made, held
/// in 8 bits, as measured; 0 where that is not known, and it has none.</param>
/// <param name="StateBytes">What one save of a language model's partial state holds — its recurrent layers' state, its sliding window's
/// cells held in 8 bits, at their fullest — as measured: what each of the saves it keeps to cut a conversation back
/// (<see cref="ModelSpec.Checkpoints"/>) costs this machine's memory; 0 for a model with no such state, cut back anywhere, or where
/// it is not known, and it keeps none.</param>
/// <param name="ExpertBytes">What a mixture-of-experts language model's routed experts hold, every layer's together, as its GGUF's
/// tensors say: what it keeps in this machine's memory where its GPU has room for the rest of it alone; 0 for a model with none.</param>
/// <param name="Streamed">The same part built to be streamed — its weights in this machine's memory, brought to the GPU a group at a
/// time — for a GPU whose budget does not hold it whole; null where it has no such build.</param>
/// <param name="Tiled">How the part — a decoder — runs in tiles for a GPU whose budget does not hold it whole; null where it does not.</param>
public sealed record HostedPart(string Role, string Runner, string Folder, string? File, long MemoryBytes, int ContextTokens,
                                IReadOnlyDictionary<string, string>? CudaOptions, string? ExecutionProvider, string Name, string? Projector = null,
                                long BytesPerToken = 0, bool LoadsOnDemand = false, int Layers = 0, long BatchBytesPerToken = 0,
                                long BytesPerSequence = 0, long BackgroundBytes = 0, long StateBytes = 0, long ExpertBytes = 0,
                                HostedStreamedBuild? Streamed = null, HostedTiledBuild? Tiled = null);

/// <summary>A part's streamed build, beside its whole one in its folder, sharing its weights' file.</summary>
/// <param name="File">Its manifest (<c>*.stream.json</c>): the groups it is run as, and where each one's weights lie.</param>
/// <param name="MemoryBytes">What it holds on the GPU at its peak: two groups' weights, the graph's small ones, one group's work.</param>
/// <param name="HostBytes">What it holds in this machine's memory: its streamed weights, pinned.</param>
public sealed record HostedStreamedBuild(string File, long MemoryBytes, long HostBytes);

/// <summary>A decoder run in tiles: the latent cut into overlapping squares of <paramref name="Cells"/> cells, each decoded alone
/// and blended back (<see cref="ModelSpec.TileCells"/>).</summary>
/// <param name="MemoryBytes">What it holds on the GPU at its peak, a tile at a time.</param>
/// <param name="HostBytes">What it holds in this machine's memory: for a decoder that carries a cache from frame to frame, every
/// tile's cache, pinned between its calls; 0 for one that carries none.</param>
public sealed record HostedTiledBuild(int Cells, long MemoryBytes, long HostBytes = 0);

/// <summary>What each part of a model of the catalog does.</summary>
public static class HostedRoles
{
    public const string Chat          = "chat";
    public const string VoiceActivity = "voiceActivity";
    public const string Recognizer    = "recognizer";
    public const string Transcriber   = "transcriber";
    public const string TurnDetector  = "turnDetector";
    public const string Synthesizer   = "synthesizer";
    public const string PromptEncoder = DrawingParts.PromptEncoder;
    public const string Denoiser      = DrawingParts.Denoiser;
    public const string Decoder       = DrawingParts.Decoder;
    public const string FrameEncoder  = FilmingParts.FrameEncoder;
    public const string UpscalerX2    = RestoringParts.UpscalerX2;
    public const string UpscalerX4    = RestoringParts.UpscalerX4;
    public const string FaceDetector  = RestoringParts.FaceDetector;
    public const string FaceRestorer  = RestoringParts.FaceRestorer;
    public const string FaceParser    = RestoringParts.FaceParser;
    public const string ImageEncoder    = SelectingParts.ImageEncoder;
    public const string ClickDecoder    = SelectingParts.ClickDecoder;
    public const string TextEncoder     = SelectingParts.TextEncoder;
    public const string ExemplarEncoder = SelectingParts.ExemplarEncoder;
    public const string Finder          = SelectingParts.Finder;
    public const string Reader          = ReadingParts.Reader;
    public const string TextFinder      = ReadingParts.TextFinder;
    public const string Inpainter       = ErasingParts.Inpainter;
    public const string SafetyClassifier = CheckingParts.SafetyClassifier;
    public const string DepthEstimator   = SceneDepthParts.Estimator;

    /// <summary>The parts a model of each kind is made of — an image model's, the parts the method it draws with takes; a
    /// video model's, the parts the method it films with takes.</summary>
    public static IReadOnlyList<string> Of(HostedKind kind, HostedSampler? sampler = null, HostedFilming? filming = null) => kind switch
    {
        HostedKind.Language        => [Chat],
        HostedKind.SpeechToText    => [VoiceActivity, Recognizer, Transcriber, TurnDetector],
        HostedKind.TextToSpeech    => [Synthesizer],
        HostedKind.ImageGeneration => DrawingParts.Of((sampler ?? throw new ArgumentNullException(nameof(sampler), "an image model's parts are its drawing method's")).Method),
        HostedKind.VideoGeneration => FilmingParts.Of((filming ?? throw new ArgumentNullException(nameof(filming), "a video model's parts are its filming method's")).Method),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no model of the catalog is of this kind"),
    };

    /// <summary>The parts a tool of each kind is made of.</summary>
    public static IReadOnlyList<string> Of(HostedToolKind kind) => kind switch
    {
        HostedToolKind.PictureRestoration => [UpscalerX2, UpscalerX4, FaceDetector, FaceRestorer, FaceParser],
        HostedToolKind.PictureSelection   => [ImageEncoder, ClickDecoder, TextEncoder, ExemplarEncoder, Finder],
        HostedToolKind.PageReading        => [Reader, TextFinder],
        HostedToolKind.PictureErasing     => [Inpainter],
        HostedToolKind.PictureChecking    => [SafetyClassifier],
        HostedToolKind.SceneDepth         => [DepthEstimator],
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no tool of the catalog is of this kind"),
    };

    /// <summary>Whether a model may have several parts in <paramref name="role"/>, each named: a voice model's
    /// synthesizers — the German voice is a fine-tune of its own.</summary>
    public static bool Repeats(string role) => role == Synthesizer;
}

/// <summary>
/// An entry of the catalog: a model an app asks for, or the models a tool works with. Its files are every file any machine might
/// need; its parts are what the host loads, a part built for one runtime fetched and run only on that runtime.
/// </summary>
public abstract record HostedEntry(string Id, string DisplayName, IReadOnlyList<ModelAsset> Files, IReadOnlyList<HostedPart> Parts)
{
    /// <summary>Whether its parts load when they are asked for and only for their stage — a picture's, a clip's, a
    /// tool's — rather than stay resident beside the language and speech models.</summary>
    public abstract bool LoadsOnDemand { get; }

    /// <summary>Whether it has a build for <paramref name="executionProvider"/>: every one of its parts does — an image model is built
    /// for CUDA, though its prompt encoder has a CPU build for a GPU that cannot hold it.</summary>
    public bool RunsOn(string executionProvider)
    {
        var there = PartsFor(executionProvider);
        return there.Count > 0 && Parts.Select(p => p.Name).Distinct().All(name => there.Any(p => p.Name == name));
    }

    /// <summary>The parts that run on <paramref name="executionProvider"/>: one per role.</summary>
    public IReadOnlyList<HostedPart> PartsFor(string executionProvider)
        => [.. Parts.Where(p => p.ExecutionProvider is null || p.ExecutionProvider == executionProvider)];

    public HostedPart Part(string role, string executionProvider) => PartsFor(executionProvider).Single(p => p.Role == role);

    /// <summary>The part called <paramref name="name"/> that runs on <paramref name="executionProvider"/>.</summary>
    public HostedPart PartNamed(string name, string executionProvider) => PartsFor(executionProvider).Single(p => p.Name == name);

    /// <summary>The files a machine running <paramref name="executionProvider"/> needs: only those of its parts there.</summary>
    public IReadOnlyList<ModelAsset> FilesFor(string executionProvider)
    {
        var folders = PartsFor(executionProvider).Select(p => p.Folder + "/").ToList();
        return [.. Files.Where(f => folders.Any(folder => f.RelativePath.StartsWith(folder, StringComparison.Ordinal)))];
    }
}

/// <summary>
/// The models a tool works with, run in the host like a model's but serving no kind of request of their own: a tool is used by a
/// person or by an assistant, and nothing in the cloud stands in for it. Its parts load on demand.
/// </summary>
public sealed record HostedTool(string Id, string DisplayName, IReadOnlyList<ModelAsset> Files, HostedToolKind Kind, IReadOnlyList<HostedPart> Parts)
    : HostedEntry(Id, DisplayName, Files, Parts)
{
    public override bool LoadsOnDemand => true;
}

/// <summary>
/// A model the host runs: what it is, its files — every file any machine might need — its parts, and, for a model that speaks,
/// its voices. A part built for one runtime (Whisper's fp16 build for CUDA, its int8 build for the CPU) is fetched and run only
/// on that runtime. A model that draws says how it samples, and one that films how it does; a language model, how it chooses
/// its tokens (<see cref="HostedTokenSampling"/>).
/// </summary>
/// <param name="Tier">Which job of its kind it is made for.</param>
public sealed record HostedModel(string Id, string DisplayName, IReadOnlyList<ModelAsset> Files, HostedTier Tier, HostedKind Kind,
                                 IReadOnlyList<HostedPart> Parts, IReadOnlyList<HostedVoice> Voices, bool Images, bool Audio, bool ToolCalls = false,
                                 HostedSampler? Sampler = null, HostedTokenSampling? TokenSampling = null, HostedFilming? Filming = null)
    : HostedEntry(Id, DisplayName, Files, Parts)
{
    /// <summary>A picture's and a clip's parts load for their stage alone.</summary>
    public override bool LoadsOnDemand => Kind is HostedKind.ImageGeneration or HostedKind.VideoGeneration;

    /// <summary>The LoRAs an image model draws with when asked; none for most.</summary>
    public IReadOnlyList<HostedLora> Loras { get; init; } = [];
}

/// <summary>A voice of a model that speaks: the language it reads, the part that speaks it, its file in that part's
/// folder, and — for a language read from a lexicon — the lexicon's file there.</summary>
public sealed record HostedVoice(string Language, string Part, string File, string? Lexicon = null);

/// <summary>
/// How a model that draws samples: the method it draws with, its number of denoising steps, the shift of the noise levels
/// they run through, how far each step steers away from the empty prompt (0 for a model that does not), the size it
/// draws each shape at — sizes it was trained on — whether what it steers away from can be a negative prompt, and how it
/// works from pictures, for a model that does.
/// </summary>
public sealed record HostedSampler(DrawingMethod Method, int Steps, float Shift, float Guidance, IReadOnlyDictionary<HostedShape, (int Width, int Height)> Sizes,
                                   bool NegativePrompts = false, HostedEditing? Editing = null);

/// <summary>
/// How a model works from pictures: how many it reads, and whether a mask over the first keeps what it does not cover. The
/// size it reads each at is its own, and the host's to apply; a picture made from them is drawn at the first one's.
/// </summary>
public sealed record HostedEditing(int Sources, bool Masks);

/// <summary>
/// A LoRA an image model draws with when asked, as its makers apply it: unmerged, at <paramref name="Strength"/>, its
/// <paramref name="Trigger"/> leading the prompt. Its files — the adapter and, for a distillation that draws in its own
/// steps, its sampler — are fetched the first time a picture asks for it, not with the model.
/// </summary>
/// <param name="Description">What it does, as a person choosing it reads it.</param>
/// <param name="Adult">Whether what it draws is for adults alone.</param>
/// <param name="Trigger">The word its makers trained it to answer to; null for one that needs none.</param>
public sealed record HostedLora(string Id, string DisplayName, string Description, bool Adult, string? Trigger, float Strength, ModelAsset Adapter,
                                ModelAsset? Sampler)
{
    public IReadOnlyList<ModelAsset> Files => Sampler is null ? [Adapter] : [Adapter, Sampler];

    /// <summary>Whether a picture drawn with it may be told what to steer away from: not one it draws in its own steps.</summary>
    public bool NegativePrompts => Sampler is null;

    /// <summary>The prompt as the LoRA is asked with it: its trigger first, where it has one.</summary>
    public string Prompted(string prompt) => Trigger is null ? prompt : $"{Trigger}, {prompt}";
}

/// <summary>
/// How a model that films samples: the method it films with, its number of denoising steps, the shift of the noise
/// levels they run through, how far each step steers away from the negative prompt (0 for a model that does not), the
/// timesteps a distilled model steps through (empty for one whose steps follow from the others), its makers' negative
/// prompt, the frame rate it makes clips at, the lengths it makes them in, the size it films each shape at — the sizes it
/// was trained on — and whether it carries a picture on into a clip: a model distilled from text alone cuts away from
/// one. A video model makes no square clips.
/// </summary>
public sealed record HostedFilming(FilmingMethod Method, int Steps, float Shift, float Guidance, IReadOnlyList<int> Timesteps,
                                   string NegativePrompt, int FramesPerSecond, IReadOnlyList<int> Seconds,
                                   IReadOnlyDictionary<HostedShape, (int Width, int Height)> Sizes, bool StartsFromPictures)
{
    /// <summary>The shapes a clip is made in.</summary>
    public static readonly IReadOnlyList<HostedShape> Shapes = [HostedShape.Landscape, HostedShape.Portrait];

    /// <summary>How many frames a clip <paramref name="seconds"/> long is: the first, then a second's worth for each second.</summary>
    public int Frames(int seconds) => seconds * FramesPerSecond + 1;
}

/// <summary>
/// How a language model chooses its tokens, as its makers recommend — in its GGUF's <c>general.sampling</c> keys or its
/// model card: <see cref="Thinking"/> when it reasons first, <see cref="Answering"/> when it answers straight away, the
/// same unless its makers say otherwise. A model whose makers recommend nothing takes the likeliest token each time.
/// </summary>
public sealed record HostedTokenSampling(TokenSampling Thinking, TokenSampling Answering)
{
    /// <summary>The sampling for a request that reasons first, or not.</summary>
    public TokenSampling For(bool thinks) => thinks ? Thinking : Answering;
}
