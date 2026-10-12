using System.Text.Json.Serialization;

namespace Nexaflow.AiHost.Contracts;

/// <summary>
/// One value on the AI host's pipe. The app opens the pipe and the host connects; the host speaks first
/// (<see cref="Hello"/>) and the app answers <see cref="Welcome"/>. After that, any number of requests run at once:
/// each carries an id, every reply names it, and each request ends in exactly one <see cref="Completed"/> or
/// <see cref="Failed"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$m")]
[JsonDerivedType(typeof(Hello), "hello")]
[JsonDerivedType(typeof(Welcome), "welcome")]
[JsonDerivedType(typeof(Ping), "ping")]
[JsonDerivedType(typeof(Pong), "pong")]
[JsonDerivedType(typeof(CancelRequest), "cancel")]
[JsonDerivedType(typeof(DescribeHost), "describe")]
[JsonDerivedType(typeof(HostDescription), "description")]
[JsonDerivedType(typeof(Progress), "progress")]
[JsonDerivedType(typeof(Completed), "completed")]
[JsonDerivedType(typeof(Failed), "failed")]
public abstract record HostMessage;

/// <summary>The host's first message: the token it was given on its standard input, and the protocol it speaks.</summary>
public sealed record Hello(string Token, int ProtocolVersion, int ProcessId) : HostMessage;

/// <summary>The app's answer to a valid <see cref="Hello"/>; a host that hears anything else exits.</summary>
public sealed record Welcome : HostMessage;

/// <summary>The app's heartbeat. The host answers on the thread that reads the pipe, never behind a running request,
/// so a missing <see cref="Pong"/> means the process itself has stopped answering.</summary>
public sealed record Ping(long Sequence) : HostMessage;

public sealed record Pong(long Sequence) : HostMessage;

/// <summary>Asks the host to stop the request with this id. The request still ends with its own
/// <see cref="Failed"/> (<see cref="HostErrorKind.Cancelled"/>) or, if it finished first, <see cref="Completed"/>.</summary>
public sealed record CancelRequest(long RequestId) : HostMessage;

/// <summary>A request from the app. The id is the client's, unique for the life of one connection.</summary>
public abstract record HostRequest(long RequestId) : HostMessage;

/// <summary>A reply to the request with <see cref="RequestId"/>.</summary>
public abstract record HostReply(long RequestId) : HostMessage;

/// <summary>Work under way — a model loading, a file being prepared. Also what keeps a long request from looking
/// stalled to the client's watchdog.</summary>
public sealed record Progress(long RequestId, string Stage, long Done, long Total) : HostReply(RequestId);

/// <summary>The request finished.</summary>
public sealed record Completed(long RequestId) : HostReply(RequestId);

/// <summary>The request did not finish; <see cref="Kind"/> says whether retrying could help.</summary>
public sealed record Failed(long RequestId, HostErrorKind Kind, string Message) : HostReply(RequestId);

/// <summary>What the host is and what it can run on: its runtime and the execution providers that loaded.</summary>
public sealed record DescribeHost(long RequestId) : HostRequest(RequestId);

/// <param name="RuntimePack">The pack the runtime was loaded from, or null for the install's CPU runtime.</param>
public sealed record HostDescription(long RequestId, string RuntimeVersion, IReadOnlyList<string> ExecutionProviders,
                                     string? RuntimePack) : HostReply(RequestId);

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
