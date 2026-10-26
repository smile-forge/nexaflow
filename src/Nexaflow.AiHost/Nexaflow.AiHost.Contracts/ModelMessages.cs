namespace Nexaflow.AiHost.Contracts;

/// <summary>
/// A model the host can run: its files, prepared in <see cref="Folder"/> by the adapter its catalog names, the runner
/// that loads them, where it runs and what it costs there. Every request about a model carries it, so a host started
/// fresh can load it without asking back.
/// </summary>
/// <param name="Id">Unique among the models the host holds: the catalog id, with its part for a model made of several.</param>
/// <param name="Runner">What loads and runs it — <c>llama-chat</c> for a language model; a family of speech models each has its own.</param>
/// <param name="Folder">Where its files were downloaded.</param>
/// <param name="File">The file it is — a language model's GGUF, a single graph; null for a folder onnxruntime-genai reads.</param>
/// <param name="Adapter">What turns the downloaded files into what the runner reads; null when they are used as downloaded.</param>
/// <param name="MemoryBytes">Memory it holds loaded where <see cref="Device"/> places it, with its context — what
/// admission counts: on a GPU against that GPU's budget, on the CPU against the host's share of this machine's
/// memory (<see cref="HostMemory"/>).</param>
/// <param name="ContextTokens">The longest context a conversation may grow to, for a language model; 0 otherwise.</param>
/// <param name="CudaOptions">CUDA options it needs beyond the host's own.</param>
/// <param name="Projector">A language model's multimodal projector, beside its GGUF in <see cref="Folder"/>: what it
/// sees pictures and hears recordings through; null for a model of text alone.</param>
/// <param name="Device">The GPU it runs on, numbered as <see cref="HostGpu.Device"/>; null runs it on the CPU.</param>
/// <param name="BatchTokens">How many tokens a language model reads in one step: a prompt is read in slices this long, and the
/// next token of every reply being written goes with them. Longer steps read a long prompt in fewer, each working in more memory.</param>
/// <param name="ContextCache">How a language model's context holds each token it has read.</param>
/// <param name="GpuLayers">How many of a language model's layers run on <see cref="Device"/>, the rest on the CPU, with the context
/// each layer keeps; null for every one of them.</param>
/// <param name="Sequences">How many sequences a language model's context holds: as many replies as it writes at once, all in the
/// context it is given, which they share — <see cref="ContextTokens"/> is what all of them together hold — and the conversations it
/// keeps and the openings it holds while fewer are written. Each costs memory of its own wherever a model attends to a window of
/// what it read, and every step reads against every one of those windows, so more is not free.</param>
/// <param name="BackgroundSequences">How many replies a language model writes at once for work nobody is watching — a document
/// translated passage by passage — in a context of its own made when such work comes and let go when it stops, each with a slice
/// of <paramref name="BackgroundTokens"/> to itself; 0 for none, all its work done in the one context.</param>
/// <param name="BackgroundTokens">How long each conversation in that context may grow.</param>
/// <param name="BackgroundBytes">What that context holds on <see cref="Device"/>: it is made only where that much is free.</param>
public sealed record ModelSpec(string Id, string Runner, string Folder, string? File, string? Adapter, long MemoryBytes, int ContextTokens,
                               IReadOnlyDictionary<string, string>? CudaOptions = null, string? Projector = null, int? Device = null,
                               int BatchTokens = ModelSpec.DefaultBatch, ContextCache ContextCache = ContextCache.F16, int? GpuLayers = null,
                               int Sequences = ModelSpec.DefaultSequences, int BackgroundSequences = 0, int BackgroundTokens = 0, long BackgroundBytes = 0)
{
    /// <summary>Whether <paramref name="other"/> runs the same model in the same place at the same size — a model held
    /// as one and asked for as the other is loaded again.</summary>
    public bool PlacedAs(ModelSpec other)
        => Device == other.Device && ContextTokens == other.ContextTokens && MemoryBytes == other.MemoryBytes
           && File == other.File && Folder == other.Folder && Projector == other.Projector
           && BatchTokens == other.BatchTokens && ContextCache == other.ContextCache && GpuLayers == other.GpuLayers && Sequences == other.Sequences
                      && BackgroundSequences == other.BackgroundSequences && BackgroundTokens == other.BackgroundTokens && BackgroundBytes == other.BackgroundBytes;

    /// <summary>The sequences a language model's context holds where nothing gives it room for more: as many as the catalog's figures
    /// were measured holding.</summary>
    public const int DefaultSequences = 8;

    /// <summary>The tokens a language model reads in a step where nothing gives it more room: llama.cpp's own.</summary>
    public const int DefaultBatch = 512;
}

/// <summary>How a language model's context holds the keys and values of each token it has read.</summary>
public enum ContextCache
{
    /// <summary>As 16-bit floats, as the model works them out.</summary>
    F16,

    /// <summary>
    /// As 8-bit values, a block of 32 sharing one scale: 34 bytes where <see cref="F16"/> takes 64, so near twice the context fits
    /// in the same memory, with no difference a reply shows.
    /// </summary>
    Q8,
}

/// <summary>What each way of holding a context costs.</summary>
public static class ContextCaches
{
    /// <summary>What a token held as <paramref name="cache"/> takes, where held as <see cref="ContextCache.F16"/> it takes <paramref name="asF16"/>.</summary>
    public static long BytesPerToken(this ContextCache cache, long asF16) => cache == ContextCache.Q8 ? asF16 * 34 / 64 : asF16;
}

/// <summary>Loads a model and keeps it loaded — the resident set — until it is unloaded. Replies with
/// <see cref="Progress"/> while it prepares and loads, then <see cref="ModelLoaded"/>.</summary>
public sealed record LoadModel(long RequestId, ModelSpec Model) : HostRequest(RequestId);

/// <param name="Device">The GPU it runs on, or null for the CPU.</param>
public sealed record ModelLoaded(long RequestId, int? Device, double Seconds) : HostReply(RequestId);

/// <summary>Lets a resident model go; it is unloaded once nothing is generating with it.</summary>
public sealed record UnloadModel(long RequestId, string ModelId) : HostRequest(RequestId);

/// <summary>Who is waiting on a request, highest first. The GPU takes one step at a time — a token, a slice of
/// prompt — and gives the next to the highest waiting, so a background analysis yields to a reply at every token.</summary>
public enum RequestPriority
{
    /// <summary>A live voice conversation.</summary>
    Voice,
    /// <summary>A reply someone is reading as it is written.</summary>
    Interactive,
    /// <summary>Work nobody is watching: page analysis, ranking.</summary>
    Background,
    /// <summary>Generation jobs.</summary>
    Job,
}

/// <summary>Who a turn is from. <see cref="Tool"/> is what came of one of the model's tool calls.</summary>
public enum TurnRole { System, User, Assistant, Tool }

/// <summary>A tool the model may call, in its own protocol: its name, what it does, and its arguments as a JSON Schema
/// object.</summary>
public sealed record ChatTool(string Name, string Description, string Parameters);

/// <summary>A call the model made: the id its result answers, the tool, and its arguments as a JSON object — or, when
/// they would not read as one, as the model wrote them, for the app to have the call written again.</summary>
public sealed record ChatToolCall(string Id, string Name, string Arguments);

public enum MediaKind { Image, Audio }

/// <summary>An image or a recording in a turn: a file on this machine, or its bytes.</summary>
public sealed record ChatMedia(MediaKind Kind, string? Path, byte[]? Bytes);

/// <summary>One turn of a conversation, as the app holds it.</summary>
public sealed record ChatTurn(TurnRole Role, string Text, IReadOnlyList<ChatMedia>? Media = null)
{
    /// <summary>On an <see cref="TurnRole.Assistant"/> turn: the tools it called, in order.</summary>
    public IReadOnlyList<ChatToolCall>? ToolCalls { get; init; }

    /// <summary>On a <see cref="TurnRole.Tool"/> turn: the call it answers.</summary>
    public ChatToolCall? Answers { get; init; }
}

/// <summary>
/// Generates the model's next turn of <see cref="Turns"/>. The text streams back as <see cref="TextDelta"/>s, a call of
/// one of <see cref="Tools"/> as a <see cref="ToolCallMade"/>, and the request ends with <see cref="GenerationEnded"/>.
/// A conversation whose earlier turns the host has already read costs only its new turns: the host keeps what each
/// conversation has read, and appends.
/// </summary>
/// <param name="Tools">The tools the model may call, in its own protocol; none for a model that calls none.</param>
/// <param name="Decoding">How the model's makers have it write beyond choosing each token; none for most models.</param>
public sealed record Generate(long RequestId, ModelSpec Model, IReadOnlyList<ChatTurn> Turns, int? MaxNewTokens,
                              RequestPriority Priority, IReadOnlyList<ChatTool>? Tools = null, bool Thinks = true,
                              TokenSampling? Sampling = null, ReplyDecoding? Decoding = null) : HostRequest(RequestId);

/// <summary>
/// How a reply chooses each token from the model's scores, as the model's makers recommend for it: the
/// <paramref name="TopK"/> likeliest, of those the fewest whose chances make up <paramref name="TopP"/>, a token already
/// written made less likely by <paramref name="PresencePenalty"/>, and one drawn from what is left at
/// <paramref name="Temperature"/>. A request without one takes the likeliest each time. A <paramref name="Seed"/> makes
/// the draws the same each time — for tests; without one they differ.
/// </summary>
public sealed record TokenSampling(float Temperature, int TopK, float TopP, float PresencePenalty = 0, uint? Seed = null);

/// <summary>
/// How a model's makers have it write a reply, beyond how each token is chosen. <paramref name="WrittenWindow"/>: each
/// token sees the whole prompt but only the last so many tokens written before it — the reference sliding-window
/// attention Unlimited-OCR was trained with, which llama.cpp does not run itself, so the host forgets each written token
/// as it leaves the window; a conversation written so is never continued. <paramref name="RepeatBan"/>: no run of tokens
/// written again close after itself. <paramref name="WritesControlTokens"/>: the model's control tokens are part of what
/// it says — Unlimited-OCR marks each block of a page with them — so they are sent as their text, not left out.
/// </summary>
public sealed record ReplyDecoding(int? WrittenWindow = null, RepeatBan? RepeatBan = null, bool WritesControlTokens = false);

/// <summary>No token may complete a run of <paramref name="Run"/> tokens that already stands among the last
/// <paramref name="Within"/> written — the makers' guard against a reader going round a passage again.</summary>
public sealed record RepeatBan(int Run, int Within);

public sealed record TextDelta(long RequestId, string Text) : HostReply(RequestId);

/// <summary>The model called one of the tools it was offered; its turn ends with its calls.</summary>
public sealed record ToolCallMade(long RequestId, ChatToolCall Call) : HostReply(RequestId);

public enum StopReason
{
    /// <summary>The model ended its turn.</summary>
    EndOfTurn,
    /// <summary>The request's limit on new tokens was reached.</summary>
    MaxTokens,
    /// <summary>The conversation reached the model's context.</summary>
    ContextFull,
    /// <summary>The model called tools, and waits for what came of them.</summary>
    ToolCalls,
    /// <summary>The model was going in circles — the same stretch of tokens over and over, back to back — and was stopped.</summary>
    Circling,
}

/// <param name="PromptTokens">The whole conversation, as the model has now read it.</param>
/// <param name="ReadTokens">What this request had the model read — all of the prompt when it was rebuilt, only the
/// new turns when it was appended.</param>
public sealed record GenerationEnded(long RequestId, StopReason Reason, int PromptTokens, int ReadTokens, int GeneratedTokens)
    : HostReply(RequestId);
