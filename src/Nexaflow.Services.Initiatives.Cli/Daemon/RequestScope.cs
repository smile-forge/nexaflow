using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Nexaflow.Services.Initiatives.Cli.Daemon;

/// <summary>
/// The per-request half of a process that serves several at once: where a command's output goes, and which
/// directory it thinks it was run from.
/// <para>
/// Both used to be process-global — <c>Console.SetOut</c> and <c>SetCurrentDirectory</c> — which forced the
/// daemon to answer one caller at a time. That was the wrong boundary: agents work one to a worktree, so two
/// of them are usually asking about different graphs and have no reason to queue behind each other. Making
/// the console and the directory flow with the request instead of with the process is what lets the lock
/// move down to the workspace, where the real contention is.
/// </para>
/// <para>
/// <see cref="AsyncLocal{T}"/> rather than a thread-local: a request is a task, it may hop threads at any
/// await, and the writer has to follow the work rather than the thread that happened to start it.
/// </para>
/// </summary>
internal static class RequestScope
{
    private static readonly AsyncLocal<Scope?> Active = new();
    private static int _installed;

    private sealed record Scope(TextWriter Out, TextWriter Error, string Directory);

    /// <summary>Where this request's output should go, or null on a thread doing something else — the accept
    /// loop, a timer — whose writes belong on the real console.</summary>
    internal static TextWriter? Out => Active.Value?.Out;

    internal static TextWriter? Error => Active.Value?.Error;

    /// <summary>The directory the caller ran the command in, or null when this is not serving one.</summary>
    internal static string? Directory => Active.Value?.Directory;

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
    internal static IDisposable Begin(TextWriter output, TextWriter error, string directory)
    {
        var restore = Active.Value;
        Active.Value = new Scope(output, error, directory);
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
