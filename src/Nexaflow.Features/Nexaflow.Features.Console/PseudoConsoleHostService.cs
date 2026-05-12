using Microsoft.Win32.SafeHandles;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Nexaflow.Features.Console;

/// <summary>
/// Hosts a persistent shell process (cmd.exe or pwsh.exe) inside a Windows
/// pseudo-console (ConPTY) created via the CreatePseudoConsole Win32 API.
///
/// • One instance per terminal tab — the shell stays alive for the lifetime
///   of the tab.  Text is written into the PTY input pipe as the user types
///   commands; all PTY output is decoded and forwarded to the caller's
///   <see cref="OutputReceived"/> event.
///
/// • The VT-escape sequences emitted by ConPTY are stripped to plain text
///   before they reach the ViewModel so the view stays a simple scrolling
///   ItemsControl rather than a full VT renderer.
///
/// • Designed to be subclassed or configured: override
///   <see cref="ShellExecutable"/>/<see cref="ShellArguments"/> for PowerShell.
/// </summary>
public class PseudoConsoleHostService : IDisposable
{
    // ── Configurable ─────────────────────────────────────────────────────
    public virtual string ShellExecutable => "cmd.exe";
    public virtual string ShellArguments  => string.Empty;

    // ── Events ────────────────────────────────────────────────────────────

    /// <summary>Fired on a ThreadPool thread for every decoded output chunk.</summary>
    public event Action<string>? OutputReceived;

    /// <summary>Fired when the output pump hits an unrecoverable error.</summary>
    public event Action<string>? TerminalError;

    /// <summary>Fired when the hosted shell process exits.</summary>
    public event Action<int>? ProcessExited;

    // ── State ─────────────────────────────────────────────────────────────

    public bool IsRunning => _processHandle is { IsInvalid: false, IsClosed: false }
                          && !_processExited;

    private bool _processExited;

    // ConPTY handles
    private IntPtr _hPseudoConsole = IntPtr.Zero;
    private SafeFileHandle? _inputWritePipe;   // we write → shell reads
    private SafeFileHandle? _outputReadPipe;   // shell writes → we read

    // Process
    private SafeProcessHandle?  _processHandle;
    private SafeFileHandle?     _threadHandle;
    private IntPtr              _startupInfoBuffer = IntPtr.Zero;

    // Output pump
    private Stream?      _outputStream;
    private StreamWriter? _inputWriter;

    private readonly CancellationTokenSource _lifetimeCts = new();

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Starts the hosted shell.  Call once.</summary>
    public void Start(short cols = 220, short rows = 50)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // ── 1. Create anonymous pipe pair for PTY input (host→shell) ─────
        CreatePipe(out var inputReadPipe, out var inputWritePipe,
                   inheritable: true /*read end inherited by child*/);

        // ── 2. Create anonymous pipe pair for PTY output (shell→host) ────
        CreatePipe(out var outputReadPipe, out var outputWritePipe,
                   inheritable: true /*write end inherited by child*/);

        // ── 3. CreatePseudoConsole ────────────────────────────────────────
        var size   = new COORD { X = cols, Y = rows };
        int hr     = NativeMethods.CreatePseudoConsole(size,
                         inputReadPipe.DangerousGetHandle(),
                         outputWritePipe.DangerousGetHandle(),
                         0, out _hPseudoConsole);
        Marshal.ThrowExceptionForHR(hr);

        // Child-side pipe ends are now owned by the PTY — close our copies
        inputReadPipe.Dispose();
        outputWritePipe.Dispose();

        _inputWritePipe = inputWritePipe;
        _outputReadPipe = outputReadPipe;

        // ── 4. Build STARTUPINFOEX with ConPTY attribute ──────────────────
        _startupInfoBuffer = BuildStartupInfoEx(_hPseudoConsole, out var startupInfoEx);

        // ── 5. Spawn shell ────────────────────────────────────────────────
        var cmdLine = string.IsNullOrEmpty(ShellArguments)
            ? ShellExecutable
            : $"{ShellExecutable} {ShellArguments}";


        // EXTENDED_STARTUPINFO_PRESENT is required; CREATE_NO_WINDOW must NOT
        // be set — it prevents console allocation and breaks the ConPTY connection.
        var pi = new PROCESS_INFORMATION();
        bool ok = NativeMethods.CreateProcess(
            lpApplicationName:    null,
            lpCommandLine:        cmdLine,
            lpProcessAttributes:  IntPtr.Zero,
            lpThreadAttributes:   IntPtr.Zero,
            bInheritHandles:      false,
            dwCreationFlags:      NativeMethods.EXTENDED_STARTUPINFO_PRESENT,
            lpEnvironment:        IntPtr.Zero,
            lpCurrentDirectory:   null,
            lpStartupInfo:        ref startupInfoEx,
            lpProcessInformation: out pi);

        if (!ok)
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());

        _processHandle = new SafeProcessHandle(pi.hProcess, ownsHandle: true);
        _threadHandle  = new SafeFileHandle(pi.hThread,   ownsHandle: true);

        // ── 6. Wrap pipes in .NET streams ─────────────────────────────────
        // Use isAsync: false for both streams (required for non-overlapped handles)
        _outputStream = new FileStream(_outputReadPipe, FileAccess.Read, bufferSize: 4096, isAsync: false);
        _inputWriter  = new StreamWriter(
            new FileStream(_inputWritePipe, FileAccess.Write, bufferSize: 1, isAsync: false),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
            NewLine   = "\r\n"
        };

        // ── 7. Start output pump ──────────────────────────────────────────
        _ = PumpOutputAsync(_lifetimeCts.Token);

        // ── 8. Watch for process exit ─────────────────────────────────────
        _ = WatchExitAsync(_lifetimeCts.Token);
    }

    /// <summary>
    /// Writes a command line into the PTY input as if the user pressed Enter.
    /// The caller should NOT append \r\n — this method does it.
    /// </summary>
    public void WriteInput(string text)
    {
        if (_inputWriter is null || !IsRunning) return;
        try { _inputWriter.WriteLine(text); }
        catch (IOException) { /* shell exited */ }
    }

    /// <summary>Sends Ctrl-C into the PTY.</summary>
    public void SendCtrlC()
    {
        if (_inputWriter is null) return;
        try { _inputWriter.Write('\x03'); }
        catch (IOException) { }
    }

    /// <summary>
    /// Resizes the pseudo-console viewport.
    /// </summary>
    public void Resize(short cols, short rows)
    {
        if (_hPseudoConsole == IntPtr.Zero) return;
        NativeMethods.ResizePseudoConsole(_hPseudoConsole, new COORD { X = cols, Y = rows });
    }

    // ── Output pump ───────────────────────────────────────────────────────

    // Pipe handles are non-overlapped (synchronous).  We use a dedicated
    // thread-pool thread via Task.Run so the blocking Read never ties up
    // the UI thread.  VT stripping is the consumer's responsibility so
    // we fire OutputReceived for every decoded byte regardless of content.
    private Task PumpOutputAsync(CancellationToken ct) => Task.Run(() =>
    {
        var buffer  = new byte[4096];
        var decoder = Encoding.UTF8.GetDecoder();
        var charBuf = new char[4096];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read;
                try   { read = _outputStream!.Read(buffer, 0, buffer.Length); }
                catch (IOException) { break; }   // pipe closed / shell exited normally

                if (read == 0) break;            // EOF

                int charCount = decoder.GetChars(buffer, 0, read, charBuf, 0);
                if (charCount > 0)
                    OutputReceived?.Invoke(new string(charBuf, 0, charCount));
            }
        }
        catch (OperationCanceledException) { }   // shutdown requested
        catch (Exception ex)
        {
            // Surface the error to the caller instead of swallowing silently
            TerminalError?.Invoke(
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }, CancellationToken.None);  // don't cancel the Task itself; let Read unblock via pipe close

    private async Task WatchExitAsync(CancellationToken ct)
    {
        if (_processHandle is null) return;
        try
        {
            await Task.Run(() =>
                NativeMethods.WaitForSingleObject(_processHandle.DangerousGetHandle(),
                    NativeMethods.INFINITE), ct).ConfigureAwait(false);

            _processExited = true;
            NativeMethods.GetExitCodeProcess(_processHandle.DangerousGetHandle(), out uint code);
            ProcessExited?.Invoke((int)code);
        }
        catch (OperationCanceledException) { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static void CreatePipe(out SafeFileHandle readEnd, out SafeFileHandle writeEnd,
                                   bool inheritable)
    {
        var sa = new SECURITY_ATTRIBUTES
        {
            nLength        = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = inheritable ? 1 : 0
        };
        if (!NativeMethods.CreatePipe(out readEnd, out writeEnd, ref sa, 0))
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
    }

    private static IntPtr BuildStartupInfoEx(IntPtr hPCon, out STARTUPINFOEX si)
    {
        si = new STARTUPINFOEX();
        si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

        // Query required attribute list size
        nuint attrListSize = 0;
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrListSize);

        var buffer = Marshal.AllocHGlobal((int)attrListSize);

        NativeMethods.InitializeProcThreadAttributeList(buffer, 1, 0, ref attrListSize);

        NativeMethods.UpdateProcThreadAttribute(
            buffer,
            0,
            (IntPtr)NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
            hPCon,              // pass HPCON value directly — the attribute list stores this pointer
            (IntPtr)IntPtr.Size,
            IntPtr.Zero,
            IntPtr.Zero);

        si.lpAttributeList = buffer;
        return buffer;   // caller must free via DeleteProcThreadAttributeList + FreeHGlobal
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();

        _inputWriter?.Dispose();
        _outputStream?.Dispose();
        _inputWritePipe?.Dispose();
        _outputReadPipe?.Dispose();
        _threadHandle?.Dispose();
        _processHandle?.Dispose();

        if (_startupInfoBuffer != IntPtr.Zero)
        {
            NativeMethods.DeleteProcThreadAttributeList(_startupInfoBuffer);
            Marshal.FreeHGlobal(_startupInfoBuffer);
            _startupInfoBuffer = IntPtr.Zero;
        }

        if (_hPseudoConsole != IntPtr.Zero)
        {
            NativeMethods.ClosePseudoConsole(_hPseudoConsole);
            _hPseudoConsole = IntPtr.Zero;
        }
    }
}
