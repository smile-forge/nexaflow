using System.Text.Json.Serialization;

namespace Nexaflow.AiHost.Contracts;

/// <summary>
/// One value on the AI host's pipe. The host opens the pipe and every client — an app — connects to it; the client speaks first
/// (<see cref="Hello"/>) and the host answers <see cref="Welcome"/>, or turns it away (<see cref="HelloRefused"/>). After that,
/// any number of requests run at once:
/// each carries an id, every reply names it, and each request ends in exactly one <see cref="Completed"/> or
/// <see cref="Failed"/>. A request may also take input while it runs (<see cref="RequestInput"/>) — audio to hear,
/// text to speak — until the app sends <see cref="InputEnd"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$m")]
[JsonDerivedType(typeof(Hello), "hello")]
[JsonDerivedType(typeof(Welcome), "welcome")]
[JsonDerivedType(typeof(HelloRefused), "hello-refused")]
[JsonDerivedType(typeof(Ping), "ping")]
[JsonDerivedType(typeof(Pong), "pong")]
[JsonDerivedType(typeof(CancelRequest), "cancel")]
[JsonDerivedType(typeof(AllocateSection), "allocate-section")]
[JsonDerivedType(typeof(SectionAllocated), "section-allocated")]
[JsonDerivedType(typeof(SectionRefused), "section-refused")]
[JsonDerivedType(typeof(ReleaseSection), "release-section")]
[JsonDerivedType(typeof(SectionClosed), "section-closed")]
[JsonDerivedType(typeof(AudioInput), "audio")]
[JsonDerivedType(typeof(InputEnd), "input-end")]
[JsonDerivedType(typeof(DescribeHost), "describe")]
[JsonDerivedType(typeof(HostDescription), "description")]
[JsonDerivedType(typeof(Progress), "progress")]
[JsonDerivedType(typeof(Completed), "completed")]
[JsonDerivedType(typeof(Failed), "failed")]
[JsonDerivedType(typeof(HoldModels), "hold-models")]
[JsonDerivedType(typeof(ModelsPlaced), "models-placed")]
[JsonDerivedType(typeof(ModelsAway), "models-away")]
[JsonDerivedType(typeof(ModelsBack), "models-back")]
[JsonDerivedType(typeof(Generate), "generate")]
[JsonDerivedType(typeof(ForgetConversation), "forget-conversation")]
[JsonDerivedType(typeof(TextDelta), "delta")]
[JsonDerivedType(typeof(ReasoningDelta), "reasoning-delta")]
[JsonDerivedType(typeof(ToolCallMade), "tool-call")]
[JsonDerivedType(typeof(GenerationEnded), "generated")]
[JsonDerivedType(typeof(Listen), "listen")]
[JsonDerivedType(typeof(ListenActivity), "heard-activity")]
[JsonDerivedType(typeof(ListenPartial), "heard-partial")]
[JsonDerivedType(typeof(ListenFinal), "heard-final")]
[JsonDerivedType(typeof(Speak), "speak")]
[JsonDerivedType(typeof(TextToSpeak), "text")]
[JsonDerivedType(typeof(SpokenAudio), "spoken-audio")]
[JsonDerivedType(typeof(SpokenSegmentEnd), "spoken-end")]
[JsonDerivedType(typeof(Draw), "draw")]
[JsonDerivedType(typeof(Drawn), "drawn")]
[JsonDerivedType(typeof(Film), "film")]
[JsonDerivedType(typeof(FilmedFrames), "filmed-frames")]
[JsonDerivedType(typeof(RestorePicture), "restore-picture")]
[JsonDerivedType(typeof(PictureRestored), "picture-restored")]
[JsonDerivedType(typeof(SelectInPicture), "select-in-picture")]
[JsonDerivedType(typeof(PictureEncoded), "picture-encoded")]
[JsonDerivedType(typeof(SelectByPoints), "select-by-points")]
[JsonDerivedType(typeof(PointsSelected), "points-selected")]
[JsonDerivedType(typeof(SelectByPhrase), "select-by-phrase")]
[JsonDerivedType(typeof(PhraseFound), "phrase-found")]
[JsonDerivedType(typeof(FindTextInPicture), "find-text-in-picture")]
[JsonDerivedType(typeof(TextFoundInPicture), "text-found-in-picture")]
[JsonDerivedType(typeof(ErasePicture), "erase-picture")]
[JsonDerivedType(typeof(PictureErased), "picture-erased")]
[JsonDerivedType(typeof(CheckPicture), "check-picture")]
[JsonDerivedType(typeof(PictureChecked), "picture-checked")]
[JsonDerivedType(typeof(EstimateSceneDepth), "estimate-scene-depth")]
[JsonDerivedType(typeof(SceneDepthEstimated), "scene-depth-estimated")]
[JsonDerivedType(typeof(DescribeModelStore), "describe-model-store")]
[JsonDerivedType(typeof(ModelStoreDescribed), "model-store-described")]
[JsonDerivedType(typeof(FetchModelFiles), "fetch-model-files")]
[JsonDerivedType(typeof(NoRoomForModelFiles), "no-room-for-model-files")]
[JsonDerivedType(typeof(RemoveModelFiles), "remove-model-files")]
[JsonDerivedType(typeof(ModelFilesChanged), "model-files-changed")]
public abstract record HostMessage;

/// <summary>A client's first message: the protocol it speaks, the app it is, and — for a host its app started for itself — the token
/// the host was started with, which it read on its standard input. Who the client is the host reads from the pipe, never from this.</summary>
/// <param name="Token">The host's token; null for a host whose account alone admits a client.</param>
/// <param name="App">The app's name for itself: what tells apart two apps of one account, whose conversations the host keeps apart.</param>
public sealed record Hello(string? Token, int ProtocolVersion, string App) : HostMessage;

/// <summary>The host's answer to a <see cref="Hello"/> it takes: the client's session has begun.</summary>
/// <param name="ProcessId">The host's process — for a client that started it, the one it started.</param>
/// <param name="Memory">The share of this machine's memory the host's models on the CPU may hold — for an app on the machine's
/// service, whose administrator sets it, to weigh models by and show; null from a host that does not say.</param>
public sealed record Welcome(int ProcessId, HostMemory? Memory = null) : HostMessage;

/// <summary>The host's answer to a <see cref="Hello"/> it turns away — another protocol, another token — before it closes the pipe.</summary>
/// <param name="Protocol">The protocol the host speaks, where the client speaks another — for a client that can start a host of its own
/// build instead; null where it was turned away for something else.</param>
public sealed record HelloRefused(string Reason, int? Protocol = null) : HostMessage;

/// <summary>The app's heartbeat. The host answers on the thread that reads the pipe, never behind a running request,
/// so a missing <see cref="Pong"/> means the process itself has stopped answering.</summary>
public sealed record Ping(long Sequence) : HostMessage;

public sealed record Pong(long Sequence) : HostMessage;

/// <summary>Asks the host to stop the request with this id. The request still ends with its own
/// <see cref="Failed"/> (<see cref="HostErrorKind.Cancelled"/>) or, if it finished first, <see cref="Completed"/>.</summary>
public sealed record CancelRequest(long RequestId) : HostMessage;

/// <summary>
/// Asks the host for a shared section of at least <paramref name="Length"/> bytes to write a large block into before sending the request
/// that carries it (<see cref="SharedBytes"/>). The host gives one it already holds for this app where one is free, else makes one —
/// only the host does, open to this app's account alone — and answers <see cref="SectionAllocated"/> or <see cref="SectionRefused"/>;
/// the section is the host's again when the request that names it ends, or when the app releases it unused.
/// </summary>
public sealed record AllocateSection(long Ticket, int Length) : HostMessage;

/// <summary>The section asked for: the host's id for it, the name the app opens it by, and how many bytes it holds.</summary>
public sealed record SectionAllocated(long Ticket, long Id, string Name, int Capacity) : HostMessage;

/// <summary>No section could be made for the asking — the app's sections already hold all they may.</summary>
public sealed record SectionRefused(long Ticket, string Message) : HostMessage;

/// <summary>
/// The app is done with section <paramref name="Id"/>: a reply's it has read, or one it asked for and will not send. The host takes
/// it back to reuse; the app keeps its view of it mapped for when it is given the section again.
/// </summary>
public sealed record ReleaseSection(long Id) : HostMessage;

/// <summary>
/// The host has closed section <paramref name="Id"/> — idle a while, or in the way of a new one — and will never name it again: the
/// app closes its view of it once nothing of its own still reads it.
/// </summary>
public sealed record SectionClosed(long Id) : HostMessage;

/// <summary>What the app sends into a request while it runs. The host hands it to the request's handler in the order
/// sent; input for a request that has ended, or that takes none, is dropped.</summary>
public abstract record RequestInput(long RequestId) : HostMessage;

/// <summary>Audio captured as the request runs: 16 kHz, 16-bit signed little-endian, mono PCM.</summary>
public sealed record AudioInput(long RequestId, byte[] Pcm) : RequestInput(RequestId);

/// <summary>No more input is coming: the request finishes what it has been given and ends.</summary>
public sealed record InputEnd(long RequestId) : RequestInput(RequestId);

/// <summary>A request from the app. The id is the client's, unique for the life of one connection.</summary>
public abstract record HostRequest(long RequestId) : HostMessage;

/// <summary>A reply to the request with <see cref="RequestId"/>.</summary>
public abstract record HostReply(long RequestId) : HostMessage;

/// <summary>Work under way — a model loading, a file being prepared. Also what keeps a long request from looking
/// stalled to the client's watchdog.</summary>
public sealed record Progress(long RequestId, string Stage, long Done, long Total) : HostReply(RequestId)
{
    /// <summary>The stage the host says for a request waiting behind more urgent work — for a turn on its device, a slot to answer
    /// in, room in its model's context, a model loading — when it has said nothing itself for a while.</summary>
    public const string Queued = "queued";

    /// <summary>The stage the host says for a request that has gone on since its last wait, a step at a time between other work's,
    /// with nothing of its own to show yet.</summary>
    public const string Working = "working";
}

/// <summary>The request finished.</summary>
public sealed record Completed(long RequestId) : HostReply(RequestId);

/// <summary>The request did not finish; <see cref="Kind"/> says whether retrying could help.</summary>
public sealed record Failed(long RequestId, HostErrorKind Kind, string Message) : HostReply(RequestId);

/// <summary>What the host is and what it can run on: its runtime and the execution providers that loaded.</summary>
public sealed record DescribeHost(long RequestId) : HostRequest(RequestId);

/// <param name="RuntimePack">The id of the pack the runtime was loaded from, or null for the install's CPU runtime.</param>
/// <param name="Gpus">The GPUs models can run on — none without the pack.</param>
/// <param name="Held">Where every held part runs, whoever holds it (<see cref="HoldModels"/>).</param>
/// <param name="Unplaced">The held parts placed nowhere, and why.</param>
/// <param name="Machine">The machine as the host read it when it decided its runtime — what an app says of where models run.</param>
public sealed record HostDescription(long RequestId, string RuntimeVersion, IReadOnlyList<string> ExecutionProviders,
                                     string? RuntimePack, IReadOnlyList<HostGpu> Gpus, HostMemory? Memory = null,
                                     IReadOnlyList<PartPlacement>? Held = null, IReadOnlyList<PartUnplaced>? Unplaced = null,
                                     HostMachine? Machine = null) : HostReply(RequestId);

/// <summary>The machine as the host read it through NVIDIA's driver, before it chose its runtime.</summary>
/// <param name="Cards">Its NVIDIA cards, in the order the host numbers them; none where it found no driver.</param>
/// <param name="CudaDriver">The newest CUDA version their driver runs (<c>13.0</c>), or null where none answered.</param>
/// <param name="PackFilesHere">How many of the CUDA pack's files the host's store holds, each verified.</param>
public sealed record HostMachine(IReadOnlyList<HostCard> Cards, string? CudaDriver, int PackFilesHere);

/// <summary>An NVIDIA card the host found: what its driver calls it, all its memory, and its PCI address.</summary>
public sealed record HostCard(string Name, long TotalBytes, string? BusId);

/// <summary>A GPU the host runs models on.</summary>
/// <param name="Device">Its number — CUDA's own order, which llama.cpp and ONNX Runtime both count in.</param>
/// <param name="Name">What its driver calls it.</param>
/// <param name="BusId">Its PCI address (<c>domain:bus:device.function</c>), or null when the driver does not say.</param>
/// <param name="TotalBytes">Its memory.</param>
/// <param name="BudgetBytes">What models may hold on it together: what was free when the host started, less what the
/// runtime keeps for itself.</param>
/// <param name="FreeBytes">What is free on it as it was described.</param>
public sealed record HostGpu(int Device, string Name, string? BusId, long TotalBytes, long BudgetBytes, long FreeBytes)
{
    /// <summary>What the host keeps back on each GPU, beyond what other programs held when it started: the runtime's
    /// own allocations (cuBLAS workspaces, ONNX Runtime's arenas) and transients beyond each model's estimate.</summary>
    public const long ReservedBytes = 3L << 29;

    /// <summary>What the host's own work holds on it now: what was free when it started, less what is free now.</summary>
    public long HeldBytes => BudgetBytes + ReservedBytes - FreeBytes;
}

/// <summary>This machine's memory as the host weighs models on its CPU against it: all of it, and the share models may
/// hold. The share is kept by letting idle models go, never by refusing one — past it, the system pages.</summary>
public sealed record HostMemory(long TotalBytes, long BudgetBytes)
{
    /// <summary>The budget of a host that has not said: none.</summary>
    public static HostMemory Unbounded { get; } = new(long.MaxValue, long.MaxValue);

    /// <summary>The share of <paramref name="totalBytes"/> models may hold at <paramref name="percent"/>, kept within
    /// <see cref="AiHostProtocol.MemoryShares"/>.</summary>
    public static HostMemory Of(long totalBytes, int percent)
        => new(totalBytes, totalBytes / 100 * Math.Clamp(percent, AiHostProtocol.MemoryShares.Start.Value, AiHostProtocol.MemoryShares.End.Value));
}

public enum HostErrorKind
{
    /// <summary>The request was cancelled by the app.</summary>
    Cancelled,
    /// <summary>The request named something the host does not have — a model not prepared, a file missing.</summary>
    NotFound,
    /// <summary>The request was refused rather than over-commit: the VRAM budget, or a slot already in use.</summary>
    Refused,
    /// <summary>The request was malformed or of a kind this host does not handle.</summary>
    Invalid,
    /// <summary>The runtime failed while running it.</summary>
    Runtime,
}
