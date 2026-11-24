namespace Nexaflow.AiHost.Contracts;

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

/// <summary>A part of a model or tool the host's catalog names: the catalog's id for the model or tool, and the part's name in it. What
/// the part is, which build of it runs here and where, the host decides.</summary>
public sealed record ModelRef(string Model, string Part)
{
    /// <summary>The host's name for the part, as placements and notices name it.</summary>
    public string Id => $"{Model}/{Part}";

    public override string ToString() => Id;
}

/// <summary>
/// Holds <see cref="Models"/> loaded for as long as the request runs — until it is cancelled, its input ends, or the app goes. The host
/// plans where every held part runs together, those held at a higher priority placed first, and loads each there; a part held by several
/// holds is loaded once, and goes once none holds it and nothing is using it. Replies with <see cref="Progress"/> while it fetches a build
/// the plan needs of a model that is here and loads, then <see cref="ModelsPlaced"/> — again whenever a later hold moves its parts. While
/// a picture is drawn, a held part nothing is using may make way for it: <see cref="ModelsAway"/> says so, and <see cref="ModelsBack"/>
/// once it is loaded again. A part no GPU has room for, with no CPU build, is held nowhere until the plan changes. A part the catalog
/// does not name, or of a model whose files are not in the store, fails the request (<see cref="HostErrorKind.NotFound"/>).
/// </summary>
public sealed record HoldModels(long RequestId, IReadOnlyList<ModelRef> Models, RequestPriority Priority) : HostRequest(RequestId);

/// <summary>Where a held part runs, and what it holds there.</summary>
/// <param name="Device">The GPU it runs on, numbered as <see cref="HostGpu.Device"/>; null for the CPU.</param>
/// <param name="MemoryBytes">What it holds where it runs, its context with it.</param>
/// <param name="HostBytes">What it holds in this machine's memory besides, running on a GPU.</param>
/// <param name="ContextTokens">The longest context a conversation may grow to, for a language model; 0 otherwise.</param>
/// <param name="Sequences">How many replies a language model writes at once.</param>
/// <param name="BackgroundSequences">How many replies a language model writes at once for work nobody is watching, in a context of
/// their own; 0 where it does such work in its one context.</param>
/// <param name="BackgroundTokens">How long each of those may grow.</param>
/// <param name="GpuLayers">How many of a language model's layers are on its GPU, where it is cut between the GPU and the CPU; null for
/// all of them.</param>
/// <param name="ExpertsInHost">Whether a mixture of experts keeps its routed experts in this machine's memory.</param>
public sealed record PartPlacement(ModelRef Part, int? Device, long MemoryBytes, long HostBytes = 0, int ContextTokens = 0, int Sequences = 0,
                                   int BackgroundSequences = 0, int BackgroundTokens = 0, int? GpuLayers = null, bool ExpertsInHost = false);

/// <summary>A held part placed nowhere, and why.</summary>
public sealed record PartUnplaced(ModelRef Part, string Reason);

/// <summary>Where a hold's parts run now — loaded there — and those placed nowhere.</summary>
public sealed record ModelsPlaced(long RequestId, IReadOnlyList<PartPlacement> Placed, IReadOnlyList<PartUnplaced> Unplaced) : HostReply(RequestId);

/// <summary>Held parts that made way on their GPU for a picture: unloaded until the last picture they made way for is done. Until
/// then a request for one waits.</summary>
public sealed record ModelsAway(long RequestId, IReadOnlyList<ModelRef> Parts) : HostReply(RequestId);

/// <summary>Held parts that made way for a picture, loaded again.</summary>
public sealed record ModelsBack(long RequestId, IReadOnlyList<ModelRef> Parts) : HostReply(RequestId);

/// <summary>Who a turn is from. <see cref="Tool"/> is what came of one of the model's tool calls.</summary>
public enum TurnRole { System, User, Assistant, Tool }

/// <summary>A tool the model may call, in its own protocol: its name, what it does, and its arguments as a JSON Schema
/// object.</summary>
public sealed record ChatTool(string Name, string Description, string Parameters);

/// <summary>A call the model made: the id its result answers, the tool, and its arguments as a JSON object — or, when
/// they would not read as one, as the model wrote them, for the app to have the call written again.</summary>
public sealed record ChatToolCall(string Id, string Name, string Arguments);

public enum MediaKind { Image, Audio }

/// <summary>An image or a recording in a turn, as its bytes: an image encoded — a PNG, a JPEG — which the model's projector reads
/// whatever its format; a recording as 16-bit mono PCM at 16 kHz — decoding and resampling it is the app's.</summary>
public sealed record ChatMedia(MediaKind Kind, SharedBytes Bytes);

/// <summary>One turn of a conversation, as the app holds it.</summary>
public sealed record ChatTurn(TurnRole Role, string Text, IReadOnlyList<ChatMedia>? Media = null)
{
    /// <summary>On an <see cref="TurnRole.Assistant"/> turn: the tools it called, in order.</summary>
    public IReadOnlyList<ChatToolCall>? ToolCalls { get; init; }

    /// <summary>On a <see cref="TurnRole.Tool"/> turn: the call it answers.</summary>
    public ChatToolCall? Answers { get; init; }

    /// <summary>
    /// On an <see cref="TurnRole.Assistant"/> turn: what the model reasoned before it, as <see cref="ReasoningDelta"/>s sent it,
    /// given back for the model's template to place where its makers have it read — a call's reasoning, which the next step of the
    /// same task reads; null for none. A turn the host already holds is read as the model wrote it, its reasoning with it.
    /// </summary>
    public string? Reasoning { get; init; }
}

/// <summary>
/// Generates the model's next turn of <see cref="Turns"/>. The text streams back as <see cref="TextDelta"/>s, a call of
/// one of <see cref="Tools"/> as a <see cref="ToolCallMade"/>, and the request ends with <see cref="GenerationEnded"/>.
/// A conversation whose earlier turns the host has already read costs only its new turns: the host keeps what each
/// conversation has read, and appends.
/// </summary>
/// <param name="Model">Its chat part, as the catalog names it. Where it is not held, it is loaded for the request alone, placed as it
/// would be held.</param>
/// <param name="Tools">The tools the model may call, in its own protocol; none for a model that calls none.</param>
/// <param name="Decoding">How the model's makers have it write beyond choosing each token; none for most models.</param>
/// <param name="SendsReasoning">Send the model's reasoning as it is written (<see cref="ReasoningDelta"/>), for an app that keeps it;
/// otherwise it is read and never sent, and its progress alone is told.</param>
/// <param name="Conversation">The app's name for the conversation this turn is of. Named, the host keeps what the model read and wrote
/// for the next turn under that name — one state a name, the latest — continuing from it while the turns still match it and cutting
/// it back where they part, until the app forgets it (<see cref="ForgetConversation"/>) or the room is wanted; unnamed, the turn is a
/// one-off, read afresh and let go when its reply ends, taking nothing another conversation kept.</param>
public sealed record Generate(long RequestId, ModelRef Model, IReadOnlyList<ChatTurn> Turns, int? MaxNewTokens,
                              RequestPriority Priority, IReadOnlyList<ChatTool>? Tools = null, bool Thinks = true,
                              TokenSampling? Sampling = null, ReplyDecoding? Decoding = null, bool SendsReasoning = false,
                              string? Conversation = null) : HostRequest(RequestId);

                              /// <summary>Lets go of what the host keeps of the conversation the app named <see cref="Conversation"/>, on every model keeping it:
                              /// the app has closed it. A turn of it still being answered is let go when it ends.</summary>
                              public sealed record ForgetConversation(long RequestId, string Conversation) : HostRequest(RequestId);

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

/// <summary>The model's reasoning as it is written, for a request that asked for it (<see cref="Generate.SendsReasoning"/>): never
/// part of the reply's text.</summary>
public sealed record ReasoningDelta(long RequestId, string Text) : HostReply(RequestId);

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
