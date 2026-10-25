using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.IO.Common.Terminal;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// A shell that is not there: a launcher handing over itself as the transport, recording what it was launched with,
/// sent, resized to and asked to stop, and emitting whatever a test says the shell wrote — so the terminal view-model
/// runs with no process behind it.
/// </summary>
internal sealed class FakeTerminal : ITerminalLauncher, ITerminalTransport
{
    private readonly HeldEvent<ReadOnlyMemory<byte>> _output = new(chunk => chunk.ToArray());
    private readonly HeldEvent<string> _failed = new();
    private readonly HeldEvent<int> _exited = new();
    private readonly List<byte> _written = [];

    public TerminalLaunch? Launched { get; private set; }

    /// <summary>Everything sent to the shell, as text.</summary>
    public string Written => Encoding.UTF8.GetString([.. _written]);

    public (int Columns, int Rows)? Size { get; private set; }

    public int Interrupts { get; private set; }

    public int ForegroundEnds { get; private set; }

    public bool Disposed { get; private set; }

    public ValueTask<ITerminalTransport> LaunchAsync(TerminalLaunch launch, CancellationToken ct)
    {
        Launched = launch;
        Size     = (launch.Columns, launch.Rows);
        return ValueTask.FromResult<ITerminalTransport>(this);
    }

    public event Action<ReadOnlyMemory<byte>>? Output { add => _output.Add(value); remove => _output.Remove(value); }
    public event Action<string>? Failed { add => _failed.Add(value); remove => _failed.Remove(value); }
    public event Action<int>? Exited { add => _exited.Add(value); remove => _exited.Remove(value); }

    public bool IsRunning => !Disposed;

    public void Write(ReadOnlySpan<byte> input) => _written.AddRange(input.ToArray());

    public void Resize(int columns, int rows) => Size = (columns, rows);

    public void Interrupt() => Interrupts++;

    public void TerminateForeground() => ForegroundEnds++;

    /// <summary>The shell writes <paramref name="bytes"/>.</summary>
    public void Emit(params byte[] bytes) => _output.Raise(bytes);

    public void Dispose() => Disposed = true;
}
