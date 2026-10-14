namespace Nexaflow.AiHost.Contracts;

/// <summary>
/// A model the host can run: its files, prepared in <see cref="Folder"/> by the adapter its catalog names, the runner
/// that loads them, and what it costs. Every request about a model carries it, so a host started fresh can load it
/// without asking back.
/// </summary>
/// <param name="Id">Unique among the models the host holds: the catalog id, with its part for a model made of several.</param>
/// <param name="Runner">What loads and runs it — <c>llama-chat</c> for a language model; a family of speech models each has its own.</param>
/// <param name="Folder">Where its files were downloaded.</param>
/// <param name="File">The file it is — a language model's GGUF, a single graph; null for a folder onnxruntime-genai reads.</param>
/// <param name="Adapter">What turns the downloaded files into what the runner reads; null when they are used as downloaded.</param>
/// <param name="MemoryBytes">GPU memory it holds loaded, with a working context — what admission counts; 0 for a model
/// that always runs on the CPU.</param>
/// <param name="ContextTokens">The longest context a conversation may grow to, for a language model; 0 otherwise.</param>
/// <param name="CudaOptions">CUDA options it needs beyond the host's own.</param>
/// <param name="Projector">A language model's multimodal projector, beside its GGUF in <see cref="Folder"/>: what it
/// sees pictures and hears recordings through; null for a model of text alone.</param>
public sealed record ModelSpec(string Id, string Runner, string Folder, string? File, string? Adapter, long MemoryBytes, int ContextTokens,
                               IReadOnlyDictionary<string, string>? CudaOptions = null, string? Projector = null);

/// <summary>Loads a model and keeps it loaded — the resident set — until it is unloaded. Replies with
/// <see cref="Progress"/> while it prepares and loads, then <see cref="ModelLoaded"/>.</summary>
public sealed record LoadModel(long RequestId, ModelSpec Model) : HostRequest(RequestId);

/// <param name="ExecutionProvider">What it runs on: <c>cuda</c> or <c>cpu</c>.</param>
public sealed record ModelLoaded(long RequestId, string ExecutionProvider, double Seconds) : HostReply(RequestId);

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
public sealed record Generate(long RequestId, ModelSpec Model, IReadOnlyList<ChatTurn> Turns, int MaxNewTokens,
                              RequestPriority Priority, IReadOnlyList<ChatTool>? Tools = null) : HostRequest(RequestId);

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
}

/// <param name="PromptTokens">The whole conversation, as the model has now read it.</param>
/// <param name="ReadTokens">What this request had the model read — all of the prompt when it was rebuilt, only the
/// new turns when it was appended.</param>
public sealed record GenerationEnded(long RequestId, StopReason Reason, int PromptTokens, int ReadTokens, int GeneratedTokens)
    : HostReply(RequestId);
