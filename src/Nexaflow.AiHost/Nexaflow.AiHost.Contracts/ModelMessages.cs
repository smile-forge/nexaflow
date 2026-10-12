namespace Nexaflow.AiHost.Contracts;

/// <summary>
/// A model the host can run: its files, prepared in <see cref="Folder"/> by the adapter its catalog names, and what
/// it costs. Every request about a model carries it, so a host started fresh can load it without asking back.
/// </summary>
/// <param name="Id">The catalog id — what the ability grid names.</param>
/// <param name="Folder">Where its files were downloaded.</param>
/// <param name="Adapter">What turns the downloaded files into a model onnxruntime-genai runs.</param>
/// <param name="MemoryBytes">GPU memory it holds loaded, with a working context — what admission counts.</param>
/// <param name="ContextTokens">The longest context a conversation may grow to.</param>
public sealed record ModelSpec(string Id, string Folder, string Adapter, long MemoryBytes, int ContextTokens);

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

public enum TurnRole { System, User, Assistant }

public enum MediaKind { Image, Audio }

/// <summary>An image or a recording in a turn: a file on this machine, or its bytes.</summary>
public sealed record ChatMedia(MediaKind Kind, string? Path, byte[]? Bytes);

/// <summary>One turn of a conversation, as the app holds it.</summary>
public sealed record ChatTurn(TurnRole Role, string Text, IReadOnlyList<ChatMedia>? Media = null);

/// <summary>
/// Generates the model's next turn of <see cref="Turns"/>. The text streams back as <see cref="TextDelta"/>s and the
/// request ends with <see cref="GenerationEnded"/>. A conversation whose earlier turns the host has already read
/// costs only its new turns: the host keeps what each conversation has read, and appends.
/// </summary>
public sealed record Generate(long RequestId, ModelSpec Model, IReadOnlyList<ChatTurn> Turns, int MaxNewTokens,
                              RequestPriority Priority) : HostRequest(RequestId);

public sealed record TextDelta(long RequestId, string Text) : HostReply(RequestId);

public enum StopReason
{
    /// <summary>The model ended its turn.</summary>
    EndOfTurn,
    /// <summary>The request's limit on new tokens was reached.</summary>
    MaxTokens,
    /// <summary>The conversation reached the model's context.</summary>
    ContextFull,
}

/// <param name="PromptTokens">The whole conversation, as the model has now read it.</param>
/// <param name="ReadTokens">What this request had the model read — all of the prompt when it was rebuilt, only the
/// new turns when it was appended.</param>
public sealed record GenerationEnded(long RequestId, StopReason Reason, int PromptTokens, int ReadTokens, int GeneratedTokens)
    : HostReply(RequestId);
