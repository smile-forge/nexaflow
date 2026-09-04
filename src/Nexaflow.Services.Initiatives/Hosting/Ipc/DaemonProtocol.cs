using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexaflow.Services.Initiatives.Hosting.Ipc;

/// <summary>
/// What a client says to the daemon and what comes back, and how the two find each other.
/// <para>
/// The wire form is a length-prefixed UTF-8 JSON object, and a command connection carries three of them: the
/// request out, an acknowledgement straight back, and the answer whenever it is ready. The middle one is what
/// makes a slow command distinguishable from a stuck one — see <see cref="DaemonAck"/>.
/// </para>
/// <para>
/// Nothing here is large — a command line in, its console output back — so the format that is easiest to read
/// in a log wins over the one that would be fastest, which is the opposite of the choice the graph archive
/// makes and for the same reason: pick by what the bytes are for.
/// </para>
/// </summary>
public static class DaemonProtocol
{
    /// <summary>Bumped when the shapes below change incompatibly. Since the version is part of the pipe name,
    /// a client simply does not find a daemon speaking a different one, and starts its own.</summary>
    public const int Version = 2;

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // Enums by name, for the same reason the whole frame is JSON: the one time anyone reads these bytes is
        // when something has gone wrong, and "queued" answers the question that 1 does not.
        Converters             = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// The pipe both sides derive independently: one daemon per product root <b>per build</b>.
    /// <para>
    /// The build stamp is the half that matters. A daemon is long-lived and a developer rebuilds the CLI
    /// constantly, so without it a fresh client would keep talking to a daemon running yesterday's code and
    /// be told its own new option does not exist — which is precisely the failure this repo has already
    /// spent an afternoon on, in a different guise. Keying the pipe on the binary makes an upgraded client
    /// simply not find the old daemon, start its own, and leave the stale one to idle out.
    /// </para>
    /// </summary>
    public static string PipeName(string productRoot, string clientBuild)
    {
        var key    = Path.TrimEndingDirectorySeparator(Path.GetFullPath(productRoot)).ToLowerInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{key} {clientBuild} {Version}"));
        return "nfi-" + Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
    }

    /// <summary>
    /// Identity of the running binary, for <see cref="PipeName"/>. Write times rather than the assembly
    /// version: the version does not move between two debug builds a minute apart, and those are exactly the
    /// two a developer needs told apart.
    /// <para>
    /// Every managed assembly beside the entry point counts, not just the entry point. <c>nfi.exe</c> is an
    /// apphost stub — generated once, and its write time survives rebuilds that replace every line of code in
    /// <c>nfi.dll</c> and the libraries next to it. Keying on it alone let a client whose <em>dependency</em>
    /// had been rebuilt keep reaching a daemon running the old one, which is precisely the failure the
    /// comment on <see cref="PipeName"/> says this stamp exists to prevent; the stamp asserted a guarantee it
    /// could not see. One stat per file and no assembly loads, so it stays cheap enough for every call.
    /// </para>
    /// <para>
    /// Client and daemon must agree on this, and they do: the daemon runs from a <see cref="File.Copy"/> of
    /// the build output, which carries each file's name, length and write time across unchanged.
    /// </para>
    /// </summary>
    public static string BuildStamp()
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var dll in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(dll);
                sb.Append(info.Name).Append(':').Append(info.Length).Append(':')
                  .Append(info.LastWriteTimeUtc.Ticks).Append(';');
            }

            if (sb.Length == 0) return AppContext.BaseDirectory;
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())), 0, 8).ToLowerInvariant();
        }
        catch { return AppContext.BaseDirectory; }
    }

    // ── Frames ──────────────────────────────────────────────────────────────

    /// <summary>Writes one frame: a 4-byte little-endian length, then that many bytes of UTF-8 JSON.</summary>
    public static void Write<T>(Stream stream, T value)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        Span<byte> length = stackalloc byte[4];
        BitConverter.TryWriteBytes(length, payload.Length);

        stream.Write(length);
        stream.Write(payload);
        stream.Flush();
    }

    /// <summary>Reads one frame, or null when the peer hung up — which is an ordinary way for a connection
    /// to end, not a fault.</summary>
    public static T? Read<T>(Stream stream) where T : class
    {
        Span<byte> length = stackalloc byte[4];
        if (!Fill(stream, length)) return null;

        var size = BitConverter.ToInt32(length);
        if (size is < 0 or > MaxFrame) return null;

        var payload = new byte[size];
        return Fill(stream, payload) ? JsonSerializer.Deserialize<T>(payload, Json) : null;
    }

    /// <summary>A command line and its output are kilobytes; anything claiming to be megabytes is a framing
    /// error or a peer that is not us, and reading it would be the bug rather than reporting it.</summary>
    private const int MaxFrame = 64 * 1024 * 1024;

    private static bool Fill(Stream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var got = stream.Read(buffer[read..]);
            if (got <= 0) return false;
            read += got;
        }
        return true;
    }
}

/// <summary>What a connection is for. A command asks the daemon to do something; a status asks it what it is
/// already doing, and is answered without going anywhere near the work.</summary>
public enum DaemonAsk
{
    Command,
    Status,

    /// <summary>Everything on the books, for a caller asking after the process itself.</summary>
    Working,
}

/// <summary>Where a piece of accepted work has got to.</summary>
public enum WorkState
{
    /// <summary>No such ticket: either it finished long enough ago to have been forgotten, or this daemon is
    /// not the one that took it.</summary>
    Unknown,

    /// <summary>Accepted, and waiting for another command on the same working tree to finish.</summary>
    Queued,

    /// <summary>Running now.</summary>
    Running,

    /// <summary>Done. The answer is on its way back down the connection that asked for it, if it is not there
    /// already.</summary>
    Finished,
}

/// <summary>
/// One command, exactly as it was typed, plus what the daemon cannot see from where it stands: which working
/// tree the caller is in, what its current directory was, and anything piped to it.
/// </summary>
public sealed record DaemonRequest
{
    /// <summary>What this connection is for.</summary>
    public DaemonAsk Ask { get; init; } = DaemonAsk.Command;

    /// <summary>Names this piece of work. Minted by the client, so that it can ask after the work on a second
    /// connection without having first to be told what it is called.</summary>
    public string Ticket { get; init; } = "";

    /// <summary>The argv the client was invoked with, verbatim — the daemon runs the same dispatch.</summary>
    public string[] Args { get; init; } = [];

    /// <summary>The caller's working tree, or null for the main checkout.</summary>
    public string? CodeRoot { get; init; }

    /// <summary>Where the client was run, since a relative path in <see cref="Args"/> means that.</summary>
    public string WorkingDirectory { get; init; } = "";

    /// <summary>What was piped in, read by the client because only it has a console.</summary>
    public string? Stdin { get; init; }

    /// <summary>Asks the daemon to shut down after answering — the explicit stop, distinct from idling out.</summary>
    public bool Stop { get; init; }

    public static DaemonRequest Command(string ticket, string[] args, string? codeRoot,
                                        string workingDirectory, string? stdin) => new()
    {
        Ticket           = ticket,
        Args             = args,
        CodeRoot         = codeRoot,
        WorkingDirectory = workingDirectory,
        Stdin            = stdin,
    };

    public static DaemonRequest Status(string ticket) => new() { Ask = DaemonAsk.Status, Ticket = ticket };

    /// <summary>A name for one command: unique enough to tell two of them apart, short enough to sit in a
    /// message someone has to read.</summary>
    public static string NewTicket() => Guid.NewGuid().ToString("N")[..12];
}

/// <summary>
/// Sent the moment a command is understood and before any waiting begins, so that the client's next silence
/// is about the work rather than about whether anyone is there.
/// <para>
/// Without it, a slow command and a wedged process are the same observation, and the only way to tell them
/// apart is to know in advance which commands are allowed to be slow — a list that is wrong the day someone
/// adds a verb to it. With it the client stops guessing: it has been told the work was taken, and it can ask
/// after it by name.
/// </para>
/// </summary>
public sealed record DaemonAck(string Ticket, bool Accepted, string? Reason);

/// <summary>
/// What the daemon knows about a ticket. Answered immediately, and without touching the lock that the work
/// itself is holding or waiting for — a status that queued behind the work it is reporting on would say
/// nothing but its own delay, which is the one answer that is never useful.
/// </summary>
/// <param name="Behind">For queued work, the command it is waiting on and how long that has been running.</param>
public sealed record DaemonWorkStatus(
    string Ticket,
    WorkState State,
    string Command,
    double WaitedSeconds,
    double RanSeconds,
    string? Behind);

/// <summary>Everything the daemon is currently doing, for a caller asking after the process rather than
/// after one command of its own.</summary>
public sealed record DaemonWorkList(DaemonWorkStatus[] Work);

/// <summary>What the command produced, for the client to reproduce on its own console: the two streams kept
/// apart, because the difference between them is load-bearing for every caller that scripts this.</summary>
public sealed record DaemonResponse(int ExitCode, string Out, string Error);
