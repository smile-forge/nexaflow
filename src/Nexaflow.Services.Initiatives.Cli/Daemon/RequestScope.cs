using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using Nexaflow.Services.Initiatives.Hosting;

namespace Nexaflow.Services.Initiatives.Cli.Daemon;

/// <summary>
/// Everything about one request that the process serving it cannot see from where it stands: the directory the
/// caller was in, the shell it typed into, what it piped in, which warm host answers it, and whether it is still
/// there to hear the answer.
/// </summary>
internal sealed record RequestContext(string Directory)
{
    /// <summary>The caller's values for the environment variables that change how its arguments arrived —
    /// see <see cref="RequestScope.CallerVariable"/>. Null when the caller did not say.</summary>
    public IReadOnlyDictionary<string, string>? Shell { get; init; }

    /// <summary>What the caller piped in, read by the client because only it has the console.</summary>
    public string? Stdin { get; init; }

    /// <summary>The warm host for the tree, when this is being served by the resident process.</summary>
    public InitiativesHost? Host { get; init; }

    /// <summary>Cancelled when the caller has gone — nobody is left to read the answer.</summary>
    public CancellationToken Cancellation { get; init; }
}

/// <summary>
/// The per-request half of a process that serves several at once: where a command's output goes, and
/// everything else a verb would otherwise have read from the process (see <see cref="RequestContext"/>).
/// <para>
/// All of it used to be process-global — <c>Console.SetOut</c>, <c>SetCurrentDirectory</c>, and two statics on
/// <c>Program</c> for the piped input and the warm host — which forced the daemon to answer one caller at a
/// time, and then, once it stopped doing that, let two callers on different trees clear each other's input
/// and host mid-command. The boundary is the request, so that is where the state lives.
/// </para>
/// <para>
/// <see cref="AsyncLocal{T}"/> rather than a thread-local: a request is a task, it may hop threads at any
/// await, and the state has to follow the work rather than the thread that happened to start it.
/// </para>
/// </summary>
internal static class RequestScope
{
    private static readonly AsyncLocal<Scope?> Active = new();
    private static int _installed;

    private sealed record Scope(TextWriter Out, TextWriter Error, RequestContext Context);

    /// <summary>The variables a client reports from its own environment, because the daemon's is whichever
    /// shell happened to start it.</summary>
    internal static readonly string[] ShellVariables = ["MSYSTEM", "MSYS2_ARG_CONV_EXCL"];

    /// <summary>Where this request's output should go, or null on a thread doing something else — the accept
    /// loop, a timer — whose writes belong on the real console.</summary>
    internal static TextWriter? Out => Active.Value?.Out;

    internal static TextWriter? Error => Active.Value?.Error;

    /// <summary>The directory the caller ran the command in, or null when this is not serving one.</summary>
    internal static string? Directory => Active.Value?.Context.Directory;

    /// <summary>What the caller piped in, or null.</summary>
    internal static string? Stdin => Active.Value?.Context.Stdin;

    /// <summary>The warm host serving this request, or null.</summary>
    internal static InitiativesHost? Host => Active.Value?.Context.Host;

    /// <summary>Whether a request is being served at all — as opposed to a test or a timer calling in.</summary>
    internal static bool Serving => Active.Value is not null;

    /// <summary>Cancelled once the caller has gone. <see cref="CancellationToken.None"/> outside a request.</summary>
    internal static CancellationToken Cancellation => Active.Value?.Context.Cancellation ?? CancellationToken.None;

    /// <summary>
    /// An environment variable as the <b>caller</b> has it. The daemon inherits whichever shell started it, so
    /// asking <see cref="Environment"/> answered for the first caller of the day: a Git Bash session that
    /// started the process made every later PowerShell caller look like Git Bash, and the reverse hid the
    /// warning from the one shell it is for. Outside a request, or when the client did not report its shell,
    /// this process's own environment is the caller's.
    /// </summary>
    internal static string? CallerVariable(string name) =>
        Active.Value?.Context.Shell is { } shell
            ? shell.GetValueOrDefault(name)
            : Environment.GetEnvironmentVariable(name);

    /// <summary>
    /// Points <c>Console.Out</c> and <c>Console.Error</c> at writers that follow the request. Done once, for
    /// the life of the process: replacing them per request is the process-global behaviour this exists to
    /// escape.
    /// </summary>
    internal static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1) return;

        var console = (Console.Out, Console.Error);
        Console.SetOut(new Routed(() => Out ?? console.Out));
        Console.SetError(new Routed(() => Error ?? console.Error));
    }

    /// <summary>Runs one request's worth of work with its own output and directory. Disposing restores
    /// whatever was in scope before, which on the serving path is nothing.</summary>
    internal static IDisposable Begin(TextWriter output, TextWriter error, string directory) =>
        Begin(output, error, new RequestContext(directory));

    internal static IDisposable Begin(TextWriter output, TextWriter error, RequestContext context)
    {
        var restore = Active.Value;
        Active.Value = new Scope(output, error, context);
        return new Scoped(() => Active.Value = restore);
    }

    private sealed class Scoped(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    /// <summary>A writer that asks, on every call, where this request's output is going. The indirection is
    /// the point: one instance installed once, serving every concurrent caller correctly.</summary>
    private sealed class Routed(Func<TextWriter> target) : TextWriter
    {
        public override Encoding Encoding => target().Encoding;

        public override void Write(char value)                       => target().Write(value);
        public override void Write(string? value)                    => target().Write(value);
        public override void Write(char[] buffer, int index, int count) => target().Write(buffer, index, count);
        public override void WriteLine()                             => target().WriteLine();
        public override void WriteLine(string? value)                => target().WriteLine(value);
        public override void Flush()                                 => target().Flush();
    }
}
