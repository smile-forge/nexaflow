using System.Text.Json;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// One model the host loads as part of a model Nexaflow runs itself — a language model is one part; listening is a
/// voice activity detector, a recogniser, a transcriber and a turn detector; speaking is a synthesizer for each set of
/// voices it has.
/// </summary>
/// <param name="Role">What it does in its model — <see cref="OwnedRoles"/>.</param>
/// <param name="Runner">What in the host loads and runs it.</param>
/// <param name="Folder">Where its files are, relative to the asset store.</param>
/// <param name="File">The file it is, for a model that is a single graph.</param>
/// <param name="Adapter">What prepares its files for the runner, if they need it.</param>
/// <param name="MemoryBytes">GPU memory it holds loaded — a language model's with no context, its projector at work
/// included; 0 for a part that runs on the CPU.</param>
/// <param name="ContextTokens">The conversation a language model is given where its GPU has the room.</param>
/// <param name="BytesPerToken">What each token of a language model's context holds on the GPU.</param>
/// <param name="CudaOptions">CUDA options it needs beyond the host's own.</param>
/// <param name="ExecutionProvider">The one runtime it is built for (<c>cuda</c> or <c>cpu</c>), or null for either.</param>
/// <param name="LoadsOnDemand">A part of a model kept loaded that is not kept with it, but loaded by the host the first
/// time a request uses it — a voice for a language seldom spoken.</param>
/// <param name="Name">What it is called in its model: its role, unless the model has several parts in that role.</param>
/// <param name="Projector">A language model's multimodal projector, in its folder: what it sees and hears through.</param>
/// <param name="Layers">How many layers a language model's GGUF has, as its header says: what it is cut into where only part
/// of it fits on a GPU; 0 where it is never cut.</param>
/// <param name="BatchBytesPerToken">What more a language model holds on the GPU for each token it reads in a step past
/// <see cref="ModelSpec.DefaultBatch"/>, as measured; 0 where that is not known, and it reads in steps of that many.</param>
/// <param name="BytesPerSequence">What more a language model holds on the GPU for each sequence its context holds past
/// <see cref="ModelSpec.DefaultSequences"/>, held in 16 bits, as measured — the window of what it read that each keeps; 0 where
/// that is not known, and it holds that many.</param>
/// <param name="BackgroundBytes">What a language model's context for work nobody is watching holds on the GPU while it is made —
/// <see cref="ModelPlacement.BackgroundSequences"/> conversations of <see cref="ModelPlacement.BackgroundTokens"/> each, held in 8
/// bits — as measured; 0 where that is not known, and it has none.</param>
/// <param name="StateBytes">What one save of a language model's partial state holds — its recurrent layers' state, its sliding window's
/// cells held in 8 bits, at their fullest — as measured: what each of the saves it keeps to cut a conversation back
/// (<see cref="ModelSpec.Checkpoints"/>) costs this machine's memory; 0 for a model with no such state, cut back anywhere, or where
/// it is not known, and it keeps none.</param>
/// <param name="ExpertBytes">What a mixture-of-experts language model's routed experts hold, every layer's together, as its GGUF's
/// tensors say: what it keeps in this machine's memory where its GPU has room for the rest of it alone; 0 for a model with none.</param>
/// <param name="Streamed">The same part built to be streamed — its weights in this machine's memory, brought to the GPU a group at a
/// time — for a GPU whose budget does not hold it whole; null where it has no such build.</param>
/// <param name="Tiled">How the part — a decoder — runs in tiles for a GPU whose budget does not hold it whole; null where it does not.</param>

internal sealed record OwnedPart(string Role, string Runner, string Folder, string? File, string? Adapter, long MemoryBytes, int ContextTokens,
                                 IReadOnlyDictionary<string, string>? CudaOptions, string? ExecutionProvider, string Name, string? Projector = null,
                                 long BytesPerToken = 0, bool LoadsOnDemand = false, int Layers = 0, long BatchBytesPerToken = 0,
                                 long BytesPerSequence = 0, long BackgroundBytes = 0, long StateBytes = 0, long ExpertBytes = 0, OwnedStreamedBuild? Streamed = null,
                                 OwnedTiledBuild? Tiled = null);

/// <summary>A part's streamed build, beside its whole one in its folder, sharing its weights' file.</summary>
/// <param name="File">Its manifest (<c>*.stream.json</c>): the groups it is run as, and where each one's weights lie.</param>
/// <param name="MemoryBytes">What it holds on the GPU at its peak: two groups' weights, the graph's small ones, one group's work.</param>
/// <param name="HostBytes">What it holds in this machine's memory: its streamed weights, pinned.</param>
internal sealed record OwnedStreamedBuild(string File, long MemoryBytes, long HostBytes);

/// <summary>A decoder run in tiles: the latent cut into overlapping squares of <paramref name="Cells"/> cells, each decoded alone
/// and blended back (<see cref="ModelSpec.TileCells"/>).</summary>
/// <param name="MemoryBytes">What it holds on the GPU at its peak, a tile at a time.</param>
/// <param name="HostBytes">What it holds in this machine's memory: for a decoder that carries a cache from frame to frame, every
/// tile's cache, pinned between its calls; 0 for one that carries none.</param>
internal sealed record OwnedTiledBuild(int Cells, long MemoryBytes, long HostBytes = 0);

/// <summary>What each part of an owned model does.</summary>
internal static class OwnedRoles
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
    public static IReadOnlyList<string> Of(ModelKind kind, OwnedSampler? sampler = null, OwnedFilming? filming = null) => kind switch
    {
        ModelKind.Language        => [Chat],
        ModelKind.SpeechToText    => [VoiceActivity, Recognizer, Transcriber, TurnDetector],
        ModelKind.TextToSpeech    => [Synthesizer],
        ModelKind.ImageGeneration => DrawingParts.Of((sampler ?? throw new ArgumentNullException(nameof(sampler), "an image model's parts are its drawing method's")).Method),
        ModelKind.VideoGeneration => FilmingParts.Of((filming ?? throw new ArgumentNullException(nameof(filming), "a video model's parts are its filming method's")).Method),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no owned model is of this kind"),
    };

    /// <summary>The parts a tool of each kind is made of.</summary>
    public static IReadOnlyList<string> Of(OwnedToolKind kind) => kind switch
    {
        OwnedToolKind.PictureRestoration => [UpscalerX2, UpscalerX4, FaceDetector, FaceRestorer, FaceParser],
        OwnedToolKind.PictureSelection   => [ImageEncoder, ClickDecoder, TextEncoder, ExemplarEncoder, Finder],
        OwnedToolKind.PageReading        => [Reader, TextFinder],
        OwnedToolKind.PictureErasing     => [Inpainter],
        OwnedToolKind.PictureChecking    => [SafetyClassifier],
        OwnedToolKind.SceneDepth         => [DepthEstimator],
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no owned tool is of this kind"),
    };

    /// <summary>Whether a model may have several parts in <paramref name="role"/>, each named: a voice model's
    /// synthesizers — the German voice is a fine-tune of its own.</summary>
    public static bool Repeats(string role) => role == Synthesizer;
}

/// <summary>
/// An entry of the owned catalog — what Nexaflow runs in its AI host: a model a provider serves, or the models a tool
/// works with. Its catalog entry lists every file any machine might need; its parts are what the host loads, a part built
/// for one runtime fetched and run only on that runtime.
/// </summary>
internal abstract record OwnedEntry(CatalogModel Catalog, IReadOnlyList<OwnedPart> Parts)
{
    public string Id => Catalog.Id;

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
    public IReadOnlyList<OwnedPart> PartsFor(string executionProvider)
        => [.. Parts.Where(p => p.ExecutionProvider is null || p.ExecutionProvider == executionProvider)];

    public OwnedPart Part(string role, string executionProvider) => PartsFor(executionProvider).Single(p => p.Role == role);

    /// <summary>The part called <paramref name="name"/> that runs on <paramref name="executionProvider"/>.</summary>
    public OwnedPart PartNamed(string name, string executionProvider) => PartsFor(executionProvider).Single(p => p.Name == name);

    /// <summary>The catalog entry as it stands on a machine running <paramref name="executionProvider"/>: only the files
    /// its parts there need.</summary>
    public CatalogModel CatalogFor(string executionProvider)
    {
        var folders = PartsFor(executionProvider).Select(p => p.Folder + "/").ToList();
        return Catalog with { Files = [.. Catalog.Files.Where(f => folders.Any(folder => f.RelativePath.StartsWith(folder, StringComparison.Ordinal)))] };
    }
}

/// <summary>What a tool of the owned catalog does; the parts it has follow from it (<see cref="OwnedRoles.Of(OwnedToolKind)"/>).</summary>
internal enum OwnedToolKind
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
/// The models a tool works with, run in the host like an owned model's but serving no provider: a tool is used by a person
/// or by an assistant, local or in the cloud, and nothing in the cloud stands in for it. Its parts load on demand.
/// </summary>
internal sealed record OwnedTool(CatalogModel Catalog, OwnedToolKind Kind, IReadOnlyList<OwnedPart> Parts) : OwnedEntry(Catalog, Parts)
{
    public override bool LoadsOnDemand => true;
}

/// <summary>
/// A model Nexaflow runs itself: its catalog entry — every file any machine might need — its parts, and, for a model
/// that speaks, its voices. A part built for one runtime (Whisper's fp16 build for CUDA, its int8 build for the CPU) is
/// fetched and run only on that runtime. A model that draws says how it samples, and one that films how it does; a
/// language model, how it chooses its tokens (<see cref="OwnedTokenSampling"/>).
/// </summary>
internal sealed record OwnedModel(CatalogModel Catalog, ModelKind Kind, IReadOnlyList<OwnedPart> Parts, IReadOnlyList<OwnedVoice> Voices,
                                  bool Images, bool Audio, bool ToolCalls = false, OwnedSampler? Sampler = null,
                                  OwnedTokenSampling? TokenSampling = null, OwnedFilming? Filming = null) : OwnedEntry(Catalog, Parts)
{
    /// <summary>A picture's and a clip's parts load for their stage alone.</summary>
    public override bool LoadsOnDemand => Kind is ModelKind.ImageGeneration or ModelKind.VideoGeneration;

    /// <summary>The LoRAs an image model draws with when asked; none for most.</summary>
    public IReadOnlyList<OwnedLora> Loras { get; init; } = [];
}

/// <summary>A voice of a model that speaks: the language it reads, the part that speaks it, its file in that part's
/// folder, and — for a language read from a lexicon — the lexicon's file there.</summary>
internal sealed record OwnedVoice(string Language, string Part, string File, string? Lexicon = null);

/// <summary>
/// How a model that draws samples: the method it draws with, its number of denoising steps, the shift of the noise levels
/// they run through, how far each step steers away from the empty prompt (0 for a model that does not), the size it
/// draws each shape at — sizes it was trained on — whether what it steers away from can be a negative prompt, and how it
/// works from pictures, for a model that does.
/// </summary>
internal sealed record OwnedSampler(DrawingMethod Method, int Steps, float Shift, float Guidance, IReadOnlyDictionary<MediaShape, (int Width, int Height)> Sizes,
                                   bool NegativePrompts = false, OwnedEditing? Editing = null);

/// <summary>
/// How a model works from pictures: how many it reads, whether a mask over the first keeps what it does not cover, and
/// the size it reads each at — its own proportions, within <paramref name="Pixels"/>, each side a multiple of
/// <paramref name="Multiple"/>, by its <paramref name="Sizing"/>. A picture made from them is the first one's size.
/// </summary>
internal sealed record OwnedEditing(int Sources, bool Masks, int Pixels, int Multiple, OwnedSizing Sizing = OwnedSizing.Within)
{
    /// <summary>The size a picture <paramref name="width"/> × <paramref name="height"/> is read at — its proportions kept
    /// to within the multiple.</summary>
    public (int Width, int Height) SizeOf(int width, int height)
    {
        if (Sizing == OwnedSizing.Filled) return PatchFitting.SizeOf(width, height, (int)Math.Sqrt(Pixels), Multiple);
        if (Sizing == OwnedSizing.Rounded)
        {
            double ratio = (double)width / height, w = Math.Sqrt(Pixels * ratio), h = w / ratio;
            return ((int)Math.Round(w / Multiple) * Multiple, (int)Math.Round(h / Multiple) * Multiple);
        }
        double scale = Math.Min(1.0, Math.Sqrt((double)Pixels / ((long)width * height)));
        int Fit(int side) => Math.Max(Multiple, (int)(side * scale) / Multiple * Multiple);
        return (Fit(width), Fit(height));
    }
}

/// <summary>How a model that works from pictures sizes each to its pixels.</summary>
internal enum OwnedSizing
{
    /// <summary>Scaled down, never up, to within them, each side then cut to the multiple — as diffusers' pipelines do.</summary>
    Within,

    /// <summary>Scaled up or down to fill them, the largest size of whole multiples within them — as HiDream-O1 reads
    /// every picture (<see cref="PatchFitting"/>); its pixels a square's.</summary>
    Filled,

    /// <summary>Scaled up or down to about them, each side rounded to the multiple — as Qwen-Image-Edit's
    /// calculate_dimensions sizes every picture.</summary>
    Rounded,
}

/// <summary>
/// A LoRA an image model draws with when asked, as its makers apply it: unmerged, at <paramref name="Strength"/>, its
/// <paramref name="Trigger"/> leading the prompt. Its files — the adapter and, for a distillation that draws in its own
/// steps, its sampler — are fetched the first time a picture asks for it, not with the model.
/// </summary>
/// <param name="Offer">What it is offered as.</param>
/// <param name="Trigger">The word its makers trained it to answer to; null for one that needs none.</param>
internal sealed record OwnedLora(ImageLora Offer, string? Trigger, float Strength, ModelAsset Adapter, ModelAsset? Sampler)
{
    public IReadOnlyList<ModelAsset> Files => Sampler is null ? [Adapter] : [Adapter, Sampler];

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
internal sealed record OwnedFilming(FilmingMethod Method, int Steps, float Shift, float Guidance, IReadOnlyList<int> Timesteps,
                                   string NegativePrompt, int FramesPerSecond, IReadOnlyList<int> Seconds,
                                   IReadOnlyDictionary<MediaShape, (int Width, int Height)> Sizes, bool StartsFromPictures)
{
    /// <summary>The shapes a clip is made in.</summary>
    public static readonly IReadOnlyList<MediaShape> Shapes = [MediaShape.Landscape, MediaShape.Portrait];

    /// <summary>How many frames a clip <paramref name="seconds"/> long is: the first, then a second's worth for each second.</summary>
    public int Frames(int seconds) => seconds * FramesPerSecond + 1;
}

/// <summary>
/// How a language model chooses its tokens, as its makers recommend — in its GGUF's <c>general.sampling</c> keys or its
/// model card: <see cref="Thinking"/> when it reasons first, <see cref="Answering"/> when it answers straight away, the
/// same unless its makers say otherwise. A model whose makers recommend nothing takes the likeliest token each time.
/// </summary>
internal sealed record OwnedTokenSampling(TokenSampling Thinking, TokenSampling Answering)
{
    /// <summary>The sampling for a request that reasons first, or not.</summary>
    public TokenSampling For(bool thinks) => thinks ? Thinking : Answering;
}

/// <summary>
/// The models in <c>owned-models.json</c>: a model catalog (<see cref="ModelCatalog"/>) whose entries also say what kind
/// of model each is and how the host runs its parts.
/// </summary>
internal static class OwnedModels
{
    public const string Cuda = "cuda";
    public const string Cpu  = "cpu";

    /// <summary>The shortest context a language model is given: room for the page it works on, its tools and a reply.</summary>
    public const int ShortestContext = 8192;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed record Manifest(List<Entry>? Models, List<ToolEntry>? Tools);
    private sealed record ToolEntry(string? Id, OwnedToolKind? Kind, List<Part>? Parts);
    private sealed record Entry(string? Id, ModelKind? Kind, bool Images, bool Audio, bool ToolCalls, List<Part>? Parts, List<Voice>? Voices, Sampler? Sampler,
                                Choosing? TokenSampling, Filming? Filming, List<Lora>? Loras);
    private sealed record Lora(string? Id, string? DisplayName, string? Description, string? Trigger, float Strength, bool Adult, string? Adapter,
                               string? Sampler, List<LoraFile>? Files);
    private sealed record LoraFile(string? Path, string? Url, long Size, string? Sha256);
    private sealed record Filming(FilmingMethod? Method, int Steps, float Shift, float Guidance, List<int>? Timesteps, string? NegativePrompt,
                                  int FramesPerSecond, List<int>? Seconds, Dictionary<string, int[]>? Sizes, bool StartsFromPictures);
    private sealed record Choosing(Choice? Thinking, Choice? Answering);
    private sealed record Choice(float Temperature, int TopK, float TopP, float PresencePenalty);
    private sealed record Sampler(DrawingMethod? Method, int Steps, float Shift, float Guidance, Dictionary<string, int[]>? Sizes, bool NegativePrompts,
                                  Editing? Editing);
    private sealed record Editing(int Sources, bool Masks, int Pixels, int Multiple, OwnedSizing Sizing = OwnedSizing.Within);
    private sealed record Voice(string? Language, string? Part, string? File, string? Lexicon);
    private sealed record Part(string? Role, string? Runner, string? Folder, string? File, string? Adapter, long MemoryBytes, int ContextTokens,
                               Dictionary<string, string>? CudaOptions, string? ExecutionProvider, string? Name, string? Projector, long BytesPerToken,
                                                              bool RunsOnCpu, bool LoadsOnDemand, int Layers = 0, long BatchBytesPerToken = 0,
                                                              long BytesPerSequence = 0, long BackgroundBytes = 0, long StateBytes = 0, long ExpertBytes = 0,
                                                              StreamedBuild? Streamed = null, TiledBuild? Tiled = null);

    private sealed record StreamedBuild(string? File, long MemoryBytes, long HostBytes);
    private sealed record TiledBuild(int Cells, long MemoryBytes, long HostBytes);
    public static IReadOnlyList<OwnedModel> All { get; } = Read(Embedded());

    /// <summary>The tools' entries: the models each tool works with.</summary>
    public static IReadOnlyList<OwnedTool> Tools { get; } = ReadTools(Embedded());

    /// <summary>The models of <paramref name="kind"/>.</summary>
    public static IEnumerable<OwnedModel> OfKind(ModelKind kind) => All.Where(m => m.Kind == kind);

    private static byte[] Embedded()
    {
        using var stream = typeof(OwnedModels).Assembly.GetManifestResourceStream("owned-models.json")
                           ?? throw new InvalidOperationException("The owned-model catalog is missing from its assembly.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <exception cref="InvalidDataException">A model does not say what it is or how its parts run, lacks a part its
    /// kind needs on a runtime, or has a file outside its parts' folders.</exception>
    internal static IReadOnlyList<OwnedModel> Read(byte[] json)
    {
        var catalog = ModelCatalog.Read(new MemoryStream(json)).ToDictionary(m => m.Id);
        var entries = JsonSerializer.Deserialize<Manifest>(json, Options)?.Models ?? [];
        return [.. entries.Select(e =>
        {
            if (e.Id is null || !catalog.TryGetValue(e.Id, out var model)) throw new InvalidDataException("An owned model has no id.");
            if (e.Kind is not { } kind) throw new InvalidDataException($"Owned model '{e.Id}' does not say what kind of model it is.");

            bool onDemand = kind is ModelKind.ImageGeneration or ModelKind.VideoGeneration;
            var parts  = (e.Parts ?? []).Select(p => ToPart("Owned model", e.Id, p, language: kind == ModelKind.Language, onDemand)).ToList();
            var voices = (e.Voices ?? []).Select(v => ToVoice(e.Id, model, parts, v)).ToList();
            if (kind == ModelKind.TextToSpeech && voices.Count == 0) throw new InvalidDataException($"Owned model '{e.Id}' speaks with no voice.");
            var sampler = e.Sampler is { } s ? ToSampler(e.Id, s) : null;
            if (kind == ModelKind.ImageGeneration && sampler is null)
                throw new InvalidDataException($"Owned model '{e.Id}' draws without saying how it samples.");
            if (kind == ModelKind.Language && (e.Images || e.Audio) && parts.Any(p => p.Projector is null))
                throw new InvalidDataException($"Owned model '{e.Id}' sees or hears with no projector to do it through.");
            if (e.TokenSampling is not null && kind != ModelKind.Language)
                throw new InvalidDataException($"Owned model '{e.Id}' chooses tokens but writes none.");
            var choosing = e.TokenSampling is { } c ? ToTokenSampling(e.Id, c) : null;
            var filming = e.Filming is { } f ? ToFilming(e.Id, f) : null;
            if ((kind == ModelKind.VideoGeneration) != (filming is not null))
                throw new InvalidDataException($"Owned model '{e.Id}' says how it films, or films without saying how.");
            var loras = (e.Loras ?? []).Select(l => ToLora(e.Id, l)).ToList();
            if (loras.Count > 0 && kind != ModelKind.ImageGeneration)
                throw new InvalidDataException($"Owned model '{e.Id}' lists LoRAs, which only a model that draws takes.");
            if (loras.GroupBy(l => l.Offer.Id).Any(g => g.Count() > 1))
                throw new InvalidDataException($"Owned model '{e.Id}' lists two LoRAs of one id.");
            if (sampler is { NegativePrompts: false } && loras.Any(l => l.Sampler is null))
                throw new InvalidDataException($"Owned model '{e.Id}' takes no negative prompt, so a LoRA it draws with in its own steps would be told one.");
            var owned = new OwnedModel(model, kind, parts, voices, e.Images, e.Audio, e.ToolCalls, sampler, choosing, filming) { Loras = loras };
            Check("Owned model", owned, OwnedRoles.Of(kind, sampler, filming));
            return owned;
        })];
    }

    /// <exception cref="InvalidDataException">A tool does not say what it does or how its parts run, lacks a part its kind
    /// needs on a runtime, or has a file outside its parts' folders.</exception>
    internal static IReadOnlyList<OwnedTool> ReadTools(byte[] json)
    {
        var catalog = ModelCatalog.ReadTools(new MemoryStream(json)).ToDictionary(m => m.Id);
        var entries = JsonSerializer.Deserialize<Manifest>(json, Options)?.Tools ?? [];
        return [.. entries.Select(e =>
        {
            if (e.Id is null || !catalog.TryGetValue(e.Id, out var tool)) throw new InvalidDataException("An owned tool has no id.");
            if (e.Kind is not { } kind) throw new InvalidDataException($"Owned tool '{e.Id}' does not say what it does.");
            var owned = new OwnedTool(tool, kind, [.. (e.Parts ?? []).Select(p => ToPart("Owned tool", e.Id, p, language: false, onDemand: true))]);
            Check("Owned tool", owned, OwnedRoles.Of(kind));
            return owned;
        })];
    }

    /// <summary>That <paramref name="entry"/> runs somewhere, with one part of each of <paramref name="needed"/> on each
    /// runtime it is built for — several synthesizers each named — and that its files and its parts' folders match.</summary>
    /// <exception cref="InvalidDataException">They do not.</exception>
    private static void Check(string what, OwnedEntry entry, IReadOnlyList<string> needed)
    {
        if (!entry.RunsOn(Cuda) && !entry.RunsOn(Cpu)) throw new InvalidDataException($"{what} '{entry.Id}' runs on no runtime.");
        foreach (var runtime in new[] { Cuda, Cpu })
        {
            var onRuntime = entry.PartsFor(runtime);
            if (onRuntime.Count == 0) continue;   // not built for this runtime
            if (!entry.RunsOn(runtime))
            {
                // Parts built for a runtime the model does not run on whole are fallbacks — a GPU part's CPU build, for a GPU that cannot
                // hold it — each beside the part it stands in for.
                if (onRuntime.FirstOrDefault(p => p.ExecutionProvider == runtime && !entry.Parts.Any(o => o.Name == p.Name && o.ExecutionProvider != runtime)) is { } alone)
                    throw new InvalidDataException($"{what} '{entry.Id}' has a {runtime} build of its {alone.Name} beside no other build of it, and does not run on {runtime} whole.");
                continue;
            }
            var roles = onRuntime.Select(p => p.Role).ToList();
            bool complete = needed.All(roles.Contains) && roles.All(needed.Contains)
                         && roles.Where(r => !OwnedRoles.Repeats(r)).GroupBy(r => r).All(g => g.Count() == 1)
                         && onRuntime.GroupBy(p => p.Name).All(g => g.Count() == 1);
            if (!complete)
                throw new InvalidDataException($"{what} '{entry.Id}' needs one part each of {string.Join(", ", needed)} on {runtime}, where it runs — several synthesizers each named — and has {string.Join(", ", onRuntime.Select(p => p.Name))}.");
        }
        var folders = entry.Parts.Select(p => p.Folder + "/").Distinct().ToList();
        if (entry.Catalog.Files.Any(f => !folders.Any(folder => f.RelativePath.StartsWith(folder, StringComparison.Ordinal))))
            throw new InvalidDataException($"{what} '{entry.Id}' has files outside its parts' folders.");
        if (entry.Parts.Any(p => !entry.Catalog.Files.Any(f => f.RelativePath.StartsWith(p.Folder + "/", StringComparison.Ordinal))))
            throw new InvalidDataException($"{what} '{entry.Id}' has a part with no files.");
    }

    /// <exception cref="InvalidDataException">The sampler does not say how it draws, how many steps and how far shifted — a
    /// shift only for a method that does not shift by the picture's size — or a size for every shape, or how it works from
    /// pictures where it says it does.</exception>
    private static OwnedSampler ToSampler(string modelId, Sampler s)
    {
        if (s is not { Method: { } method, Steps: > 0, Shift: >= 0, Guidance: >= 0 } || (s.Shift > 0) == DrawingMethods.ShiftsBySize(method))
            throw new InvalidDataException($"Owned model '{modelId}' draws without saying how it samples.");
        var sizes = new Dictionary<MediaShape, (int Width, int Height)>();
        foreach (var (name, size) in s.Sizes ?? [])
        {
            if (!Enum.TryParse<MediaShape>(name, ignoreCase: true, out var shape) || size is not [> 0, > 0])
                throw new InvalidDataException($"Owned model '{modelId}' gives '{name}' a size that is not a shape's width and height.");
            sizes[shape] = (size[0], size[1]);
        }
        // A model that only changes pictures says no sizes; one that draws from prompts, a size for every shape.
        if ((sizes.Count > 0 || s.Editing is null) && Enum.GetValues<MediaShape>().Any(shape => !sizes.ContainsKey(shape)))
            throw new InvalidDataException($"Owned model '{modelId}' does not say what size it draws each shape at.");
        OwnedEditing? editing = null;
        if (s.Editing is { } e)
        {
            if (e is not { Sources: > 0, Pixels: > 0, Multiple: > 0 })
                throw new InvalidDataException($"Owned model '{modelId}' works from pictures without saying how many, or at what size.");
            int side = (int)Math.Sqrt(e.Pixels);
            if (e.Sizing == OwnedSizing.Filled && (long)side * side != e.Pixels)
                throw new InvalidDataException($"Owned model '{modelId}' fills pictures to {e.Pixels} pixels, which is no square's.");
            editing = new OwnedEditing(e.Sources, e.Masks, e.Pixels, e.Multiple, e.Sizing);
        }
        return new OwnedSampler(method, s.Steps, s.Shift, s.Guidance, sizes, s.NegativePrompts, editing);
    }

    /// <exception cref="InvalidDataException">The LoRA does not say what it is, what it does, how strongly it is drawn
    /// with, or which of its files is its adapter and which its sampler — or lists a file that is neither.</exception>
    private static OwnedLora ToLora(string modelId, Lora l)
    {
        if (string.IsNullOrWhiteSpace(l.Id) || string.IsNullOrWhiteSpace(l.DisplayName) || string.IsNullOrWhiteSpace(l.Description) || l.Strength <= 0)
            throw new InvalidDataException($"A LoRA of owned model '{modelId}' does not say what it is, what it does or how strongly it is drawn with.");
        var files = (l.Files ?? []).Select(f => ModelCatalog.FileOf(l.Id, f.Path, f.Url, f.Size, f.Sha256)).ToList();
        var adapter = files.FirstOrDefault(f => f.RelativePath == l.Adapter)
                      ?? throw new InvalidDataException($"LoRA '{l.Id}' of owned model '{modelId}' does not list its adapter among its files.");
        ModelAsset? sampler = null;
        if (l.Sampler is not null)
            sampler = files.FirstOrDefault(f => f.RelativePath == l.Sampler)
                      ?? throw new InvalidDataException($"LoRA '{l.Id}' of owned model '{modelId}' does not list its sampler among its files.");
        if (files.Count != (sampler is null ? 1 : 2))
            throw new InvalidDataException($"LoRA '{l.Id}' of owned model '{modelId}' lists a file that is neither its adapter nor its sampler.");
        var offer = new ImageLora(l.Id, l.DisplayName, l.Description) { Adult = l.Adult, NegativePrompts = sampler is null };
        return new OwnedLora(offer, string.IsNullOrWhiteSpace(l.Trigger) ? null : l.Trigger, l.Strength, adapter, sampler);
    }

    /// <exception cref="InvalidDataException">It does not say how it films, how many steps and how far shifted — a distilled
    /// model, through which timesteps, one a step; a guided one, from which negative prompt — at what frame rate, in which
    /// lengths, or a size for each shape a clip is made in.</exception>
    private static OwnedFilming ToFilming(string modelId, Filming f)
    {
        if (f is not { Method: { } method, Steps: > 0, Shift: > 0, Guidance: >= 0, FramesPerSecond: > 0, Seconds: { Count: > 0 } seconds }
            || seconds.Any(s => s <= 0))
            throw new InvalidDataException($"Owned model '{modelId}' films without saying how it samples, at what rate or for how long.");
        var timesteps = f.Timesteps ?? [];
        if (method == FilmingMethod.WanDistilled && (timesteps.Count != f.Steps || timesteps.Any(t => t is <= 0 or > 1000) || f.Guidance != 0))
            throw new InvalidDataException($"Owned model '{modelId}' is distilled, so it steps through a timestep a step and is not guided.");
        if (method == FilmingMethod.Wan && (timesteps.Count > 0 || f.Guidance <= 0 || string.IsNullOrWhiteSpace(f.NegativePrompt)))
            throw new InvalidDataException($"Owned model '{modelId}' is guided, so it steers away from a negative prompt and its steps follow from their number.");
        var sizes = new Dictionary<MediaShape, (int Width, int Height)>();
        foreach (var (name, size) in f.Sizes ?? [])
        {
            if (!Enum.TryParse<MediaShape>(name, ignoreCase: true, out var shape) || !OwnedFilming.Shapes.Contains(shape) || size is not [> 0, > 0])
                throw new InvalidDataException($"Owned model '{modelId}' gives '{name}' a size that is not a clip shape's width and height.");
            sizes[shape] = (size[0], size[1]);
        }
        if (OwnedFilming.Shapes.Any(shape => !sizes.ContainsKey(shape)))
            throw new InvalidDataException($"Owned model '{modelId}' does not say what size it films each shape at.");
        return new OwnedFilming(method, f.Steps, f.Shift, f.Guidance, [.. timesteps], f.NegativePrompt ?? "", f.FramesPerSecond,
                                [.. seconds.Order()], sizes, f.StartsFromPictures);
    }

    /// <exception cref="InvalidDataException">It says nothing of how the model chooses when it reasons, or gives a temperature,
    /// top-k or top-p no sampler takes.</exception>
    private static OwnedTokenSampling ToTokenSampling(string modelId, Choosing c)
    {
        if (c.Thinking is not { } thinking)
            throw new InvalidDataException($"Owned model '{modelId}' says how it chooses tokens without saying how when it reasons.");
        var reasoning = Read(thinking);
        return new(reasoning, c.Answering is { } answering ? Read(answering) : reasoning);

        TokenSampling Read(Choice choice)
        {
            if (choice.Temperature <= 0 || choice.TopK < 0 || choice.TopP is <= 0 or > 1)
                throw new InvalidDataException($"Owned model '{modelId}' chooses tokens at temperature {choice.Temperature}, top-k {choice.TopK}, top-p {choice.TopP}.");
            return new TokenSampling(choice.Temperature, choice.TopK, choice.TopP, choice.PresencePenalty);
        }
    }

    /// <param name="language">A language model's part: what it and each token of its context hold must be known.</param>
    /// <param name="onDemand">A part loaded on demand against the GPU budget — a picture's, a clip's, a tool's — so what it
    /// holds at its peak must be known, unless it says it runs on the CPU on every machine: a part so small a GPU would only
    /// keep it waiting.</param>
    private static OwnedPart ToPart(string what, string id, Part p, bool language, bool onDemand)
    {
        if (string.IsNullOrWhiteSpace(p.Role) || string.IsNullOrWhiteSpace(p.Runner) || string.IsNullOrWhiteSpace(p.Folder))
            throw new InvalidDataException($"A part of {what.ToLowerInvariant()} '{id}' does not say what it is, what runs it or where it is.");
        if (p.ExecutionProvider is not (null or Cuda or Cpu))
            throw new InvalidDataException($"{what} '{id}' has a part for '{p.ExecutionProvider}', a runtime the host does not have.");
        if (language && (p.MemoryBytes <= 0 || p.BytesPerToken <= 0 || p.ContextTokens < ShortestContext))
            throw new InvalidDataException($"{what} '{id}' does not say how much it and each token of its context hold, or how long a conversation it is given.");
        if (p.RunsOnCpu && (p.MemoryBytes != 0 || p.ExecutionProvider is not null))
            throw new InvalidDataException($"{what} '{id}' runs its {p.Role} on the CPU everywhere, so it holds no GPU memory and has no build for one runtime.");
        if (onDemand && p.ExecutionProvider != Cpu && !p.RunsOnCpu && p.MemoryBytes <= 0)
            throw new InvalidDataException($"{what} '{id}' does not say how much its {p.Role} holds.");

        if (p.Streamed is { } streamed && (!onDemand || p.ExecutionProvider != Cuda || streamed.File?.EndsWith(".stream.json", StringComparison.Ordinal) != true
                                           || streamed.MemoryBytes <= 0 || streamed.HostBytes <= 0))
            throw new InvalidDataException($"{what} '{id}' has a streamed build of its {p.Role} that is not a CUDA part's loaded on demand, names no manifest, or does not say what it holds.");
        if (p.Tiled is { } tiled && (!onDemand || p.ExecutionProvider != Cuda || tiled.Cells < 16 || tiled.MemoryBytes <= 0))
            throw new InvalidDataException($"{what} '{id}' runs its {p.Role} in tiles that are not a CUDA part's loaded on demand, are under 16 cells, or do not say what they hold.");
        if (p.LoadsOnDemand && (language || onDemand))
            throw new InvalidDataException($"{what} '{id}' loads its {p.Role} on demand, which only a part of a model kept loaded can.");

        return new OwnedPart(p.Role, p.Runner, p.Folder, p.File, p.Adapter, p.MemoryBytes, p.ContextTokens, p.CudaOptions, p.ExecutionProvider,
                             p.Name ?? p.Role, p.Projector, p.BytesPerToken, p.LoadsOnDemand, p.Layers, p.BatchBytesPerToken, p.BytesPerSequence, p.BackgroundBytes,
                             p.StateBytes, p.ExpertBytes, p.Streamed is { } s ? new OwnedStreamedBuild(s.File!, s.MemoryBytes, s.HostBytes) : null,
                             p.Tiled is { } t ? new OwnedTiledBuild(t.Cells, t.MemoryBytes, t.HostBytes) : null);
    }

    private static OwnedVoice ToVoice(string modelId, CatalogModel model, List<OwnedPart> parts, Voice v)
    {
        if (string.IsNullOrWhiteSpace(v.Language) || string.IsNullOrWhiteSpace(v.File) || parts.FirstOrDefault(p => p.Name == v.Part) is not { } part)
            throw new InvalidDataException($"A voice of owned model '{modelId}' does not say what it reads, which part speaks it, or its file.");
        foreach (var file in new[] { v.File, v.Lexicon }.OfType<string>())
            if (!model.Files.Any(f => f.RelativePath == $"{part.Folder}/{file}"))
                throw new InvalidDataException($"Owned model '{modelId}' has a voice that needs {file}, which is not among its files.");
        return new OwnedVoice(v.Language, part.Name, v.File, v.Lexicon);
    }
}
