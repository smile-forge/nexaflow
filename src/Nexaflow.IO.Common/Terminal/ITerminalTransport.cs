namespace Nexaflow.IO.Common.Terminal;

/// <summary>
/// What sits under a terminal view: bytes in, bytes out, a size, and an exit. ConPTY is one transport and an SSH
/// channel with a <c>pty-req</c> another, so the terminal draws a local and a remote shell the same way. A transport
/// is running once its factory hands it over.
/// </summary>
public interface ITerminalTransport : IDisposable
{
    /// <summary>Bytes the shell wrote, raw — a UTF-8 code point may be split across two calls. The memory is only
    /// valid for the duration of the call. Raised on the transport's reader thread.</summary>
    event Action<ReadOnlyMemory<byte>>? Output;

    /// <summary>A failure of the transport itself (a broken pipe, a dropped channel), as text the terminal can show.</summary>
    event Action<string>? Failed;

    /// <summary>The shell ended, with its exit code.</summary>
    event Action<int>? Exited;

    bool IsRunning { get; }

    /// <summary>Sends input as the keyboard would.</summary>
    void Write(ReadOnlySpan<byte> input);

    /// <summary>Tells the shell its new size in character cells.</summary>
    void Resize(int columns, int rows);

    /// <summary>Interrupts the foreground program, as Ctrl+C does.</summary>
    void Interrupt();

    /// <summary>Ends whatever runs in the foreground, leaving the shell itself running.</summary>
    void TerminateForeground();
}
