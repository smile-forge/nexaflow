using Microsoft.Win32.SafeHandles;
using Nexaflow.IO.Common.Processes;
using Nexaflow.IO.Common.Terminal;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Nexaflow.IO.Terminal;

/// <summary>
/// Hosts a persistent shell process (cmd.exe or pwsh.exe) inside a Windows
/// pseudo-console (ConPTY) created via the CreatePseudoConsole Win32 API — the
/// terminal transport for a shell on this computer, started by
/// <see cref="PseudoConsoleLauncher"/>.
///
/// • One instance per terminal tab — the shell stays alive for the lifetime
///   of the tab. Input bytes are written into the PTY input pipe as they are
///   typed; PTY output is raised raw on <see cref="Output"/>, decoding and VT
///   interpretation being the consumer's, and what the shell writes before
///   anyone listens is held for the first listener.
///
/// • Designed to be configured: override
///   <see cref="ShellExecutable"/>/<see cref="ShellArguments"/> for PowerShell.
///
/// • Lifetime: every child shell is assigned to a kill-on-close job object
///   (<see cref="KillOnCloseJob"/>) so it dies with this process even on a
///   crash; <see cref="Dispose"/> additionally terminates it promptly.
/// </summary>
public class PseudoConsoleHostService : ITerminalTransport
{
    // ── Configurable ─────────────────────────────────────────────────────
    public virtual string ShellExecutable => "cmd.exe";
    public virtual string ShellArguments  => string.Empty;

    /// <summary>
    /// Working directory the shell starts in (passed as <c>lpCurrentDirectory</c>). Null = inherit
    /// this process's directory. Set before <see cref="Start"/>.
    /// </summary>
    public string? StartDirectory { get; set; }

    /// <summary>
    /// Environment variables overlaid on this process's environment for the child shell. Empty/null =
    /// inherit unchanged. Set before <see cref="Start"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? EnvironmentOverrides { get; set; }

    // ── Events ────────────────────────────────────────────────────────────

    /// <summary>Fired on a ThreadPool thread for every decoded output chunk.</summary>
    private readonly HeldEvent<ReadOnlyMemory<byte>> _output = new(chunk => chunk.ToArray());
    private readonly HeldEvent<string> _failed = new();
    private readonly HeldEvent<int> _exited = new();

    /// <summary>Raw output, one read at a time, on a ThreadPool thread.</summary>
    public event Action<ReadOnlyMemory<byte>>? Output { add => _output.Add(value); remove => _output.Remove(value); }

    /// <summary>Fired when the output pump hits an unrecoverable error.</summary>
    public event Action<string>? Failed { add => _failed.Add(value); remove => _failed.Remove(value); }

    /// <summary>Fired when the hosted shell process exits.</summary>
    public event Action<int>? Exited { add => _exited.Add(value); remove => _exited.Remove(value); }

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
    private uint               _shellPid;
    private IntPtr              _startupInfoBuffer = IntPtr.Zero;
    private IntPtr              _environmentBlock  = IntPtr.Zero;

    // Output pump
    private Stream?      _outputStream;
    private Stream?       _input;

    private readonly CancellationTokenSource _lifetimeCts = new();

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Starts the hosted shell at <paramref name="columns"/> × <paramref name="rows"/> cells. Call once.</summary>
    public void Start(int columns, int rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // ── 1. Create anonymous pipe pair for PTY input (host→shell) ─────
        CreatePipe(out var inputReadPipe, out var inputWritePipe,
                   inheritable: true /*read end inherited by child*/);

        // ── 2. Create anonymous pipe pair for PTY output (shell→host) ────
        CreatePipe(out var outputReadPipe, out var outputWritePipe,
                   inheritable: true /*write end inherited by child*/);

        // ── 3. CreatePseudoConsole ────────────────────────────────────────
        var size   = Cells(columns, rows);
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

        // Optional per-environment overrides: build a Unicode environment block once; null otherwise.
        uint creationFlags = NativeMethods.EXTENDED_STARTUPINFO_PRESENT | NativeMethods.CREATE_SUSPENDED;
        if (EnvironmentOverrides is { Count: > 0 })
        {
            _environmentBlock = MarshalEnvironmentBlock(BuildEnvironmentBlock(EnvironmentOverrides));
            creationFlags |= NativeMethods.CREATE_UNICODE_ENVIRONMENT;
        }

        // EXTENDED_STARTUPINFO_PRESENT is required; CREATE_NO_WINDOW must NOT be set — it prevents
        // console allocation and breaks the ConPTY connection. CREATE_SUSPENDED lets us assign the
        // child to the kill-on-close job before it can run (and spawn its own children).
        var pi = new PROCESS_INFORMATION();
        bool ok = NativeMethods.CreateProcess(
            lpApplicationName:    null,
            lpCommandLine:        cmdLine,
            lpProcessAttributes:  IntPtr.Zero,
            lpThreadAttributes:   IntPtr.Zero,
            bInheritHandles:      false,
            dwCreationFlags:      creationFlags,
            lpEnvironment:        _environmentBlock,
            lpCurrentDirectory:   string.IsNullOrEmpty(StartDirectory) ? null : StartDirectory,
            lpStartupInfo:        ref startupInfoEx,
            lpProcessInformation: out pi);

        if (!ok)
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());

        _processHandle = new SafeProcessHandle(pi.hProcess, ownsHandle: true);
        _threadHandle  = new SafeFileHandle(pi.hThread,   ownsHandle: true);
        _shellPid      = pi.dwProcessId;

        // ── 6. Bind the child to the kill-on-close job BEFORE it runs ─────
        KillOnCloseJob.Assign(pi.hProcess);

        // ── 7. Wrap pipes in .NET streams ─────────────────────────────────
        // Use isAsync: false for both streams (required for non-overlapped handles)
        _outputStream = new FileStream(_outputReadPipe, FileAccess.Read, bufferSize: 4096, isAsync: false);
        _input        = new FileStream(_inputWritePipe, FileAccess.Write, bufferSize: 1, isAsync: false);

        // ── 8. Start output pump + exit watch, then release the suspended thread ──
        _ = PumpOutputAsync(_lifetimeCts.Token);
        _ = WatchExitAsync(_lifetimeCts.Token);
        NativeMethods.ResumeThread(pi.hThread);
    }

    /// <summary>
    /// Builds a CreateProcess Unicode environment block: this process's environment overlaid with
    /// <paramref name="overrides"/>, entries sorted case-insensitively by name, each terminated by a
    /// NUL and the whole block ended by a second NUL. Returned as a string carrying the embedded NULs.
    /// </summary>
    internal static string BuildEnvironmentBlock(IReadOnlyDictionary<string, string> overrides)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
            merged[kv.Key?.ToString() ?? string.Empty] = kv.Value?.ToString() ?? string.Empty;
        foreach (var kv in overrides)
            merged[kv.Key] = kv.Value;
        merged.Remove(string.Empty);

        var sb = new StringBuilder();
        foreach (var kv in merged.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\0');
        sb.Append('\0');   // block terminator
        return sb.ToString();
    }

    private static IntPtr MarshalEnvironmentBlock(string block)
        => Marshal.StringToHGlobalUni(block);   // copies all chars incl. embedded NULs, then a final NUL

    /// <summary>
    /// Writes input bytes into the PTY as they are typed — keystrokes and VT sequences, with any CR/ESC bytes the caller
    /// includes. This is what makes the console a real terminal: keys reach the shell live, so interactive programs and
    /// inline echo work.
    /// </summary>
    public void Write(ReadOnlySpan<byte> input)
    {
        if (_input is null || !IsRunning) return;
        try
        {
            _input.Write(input);
            _input.Flush();
        }
        catch (IOException) { /* shell exited */ }
    }

    /// <summary>Sends Ctrl-C into the PTY.</summary>
    public void Interrupt() => Write([0x03]);

    /// <summary>
    /// Hard-stop: force-terminates every descendant of the hosted shell (the foreground command and anything
    /// it spawned) while leaving the shell itself alive, so the pane falls back to a fresh prompt. This is the
    /// escalation for a wedged child that ignored Ctrl-C (a native process blocked in a syscall, or an MSYS2
    /// helper that mishandles <c>CTRL_C_EVENT</c>). Best-effort — a process we can't open is skipped.
    /// </summary>
    public void TerminateForeground()
    {
        if (_shellPid == 0) return;
        foreach (var pid in ComputeDescendants(_shellPid, SnapshotProcesses()))
            TryTerminate(pid);
    }

    /// <summary>Flat (pid, parentPid) list of every process, via a ToolHelp snapshot. Empty on failure.</summary>
    private static IReadOnlyList<(uint Pid, uint ParentPid)> SnapshotProcesses()
    {
        var list = new List<(uint, uint)>();
        var snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snapshot == NativeMethods.INVALID_HANDLE_VALUE || snapshot == IntPtr.Zero) return list;

        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (NativeMethods.Process32First(snapshot, ref entry))
                do { list.Add((entry.th32ProcessID, entry.th32ParentProcessID)); }
                while (NativeMethods.Process32Next(snapshot, ref entry));
        }
        finally { NativeMethods.CloseHandle(snapshot); }
        return list;
    }

    /// <summary>Breadth-first descendants of <paramref name="rootPid"/> from a flat (pid, parentPid) snapshot,
    /// excluding the root. A <c>seen</c> set makes it cycle- and reuse-safe (a pid is visited at most once, so a
    /// self-parent or a stale parent loop can't recurse). Pure — unit-tested.</summary>
    internal static IReadOnlyList<uint> ComputeDescendants(
        uint rootPid, IReadOnlyList<(uint Pid, uint ParentPid)> processes)
    {
        var childrenByParent = new Dictionary<uint, List<uint>>();
        foreach (var (pid, parentPid) in processes)
        {
            if (pid == parentPid) continue;                 // guard: PID 0 / self-parent
            if (!childrenByParent.TryGetValue(parentPid, out var kids))
                childrenByParent[parentPid] = kids = [];
            kids.Add(pid);
        }

        var descendants = new List<uint>();
        var seen        = new HashSet<uint> { rootPid };
        var queue       = new Queue<uint>();
        queue.Enqueue(rootPid);
        while (queue.Count > 0)
        {
            if (!childrenByParent.TryGetValue(queue.Dequeue(), out var kids)) continue;
            foreach (var kid in kids)
                if (seen.Add(kid)) { descendants.Add(kid); queue.Enqueue(kid); }
        }
        return descendants;
    }

    private static void TryTerminate(uint pid)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_TERMINATE, bInheritHandle: false, pid);
        if (handle == IntPtr.Zero) return;
        try { NativeMethods.TerminateProcess(handle, 0); }
        catch { /* already gone / access denied */ }
        finally { NativeMethods.CloseHandle(handle); }
    }

    /// <summary>
    /// Resizes the pseudo-console viewport, in character cells.
    /// </summary>
    public void Resize(int columns, int rows)
    {
        if (_hPseudoConsole == IntPtr.Zero) return;
        NativeMethods.ResizePseudoConsole(_hPseudoConsole, Cells(columns, rows));
    }

    /// <summary>A size as ConPTY takes it, each side between one cell and what a COORD holds.</summary>
    private static COORD Cells(int columns, int rows)
        => new() { X = (short)Math.Clamp(columns, 1, short.MaxValue), Y = (short)Math.Clamp(rows, 1, short.MaxValue) };

    // ── Output pump ───────────────────────────────────────────────────────

    // Pipe handles are non-overlapped (synchronous).  We use a dedicated
    // thread-pool thread via Task.Run so the blocking Read never ties up
    // the UI thread. Each read is raised raw — decoding (a code point may
    // straddle two reads) and VT interpretation are the consumer's.
    private Task PumpOutputAsync(CancellationToken ct) => Task.Run(() =>
    {
        var buffer = new byte[4096];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read;
                try   { read = _outputStream!.Read(buffer, 0, buffer.Length); }
                catch (IOException) { break; }   // pipe closed / shell exited normally

                if (read == 0) break;            // EOF

                _output.Raise(new ReadOnlyMemory<byte>(buffer, 0, read));
            }
        }
        catch (OperationCanceledException) { }   // shutdown requested
        catch (Exception ex)
        {
            // Surface the error to the caller instead of swallowing silently
            _failed.Raise($"{ex.GetType().Name}: {ex.Message}");
        }
    }, CancellationToken.None);  // don't cancel the Task itself; let Read unblock via pipe close  // don't cancel the Task itself; let Read unblock via pipe close

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
            _exited.Raise((int)code);
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
        // Standard handles stated, and left empty: without this a child of a process whose stdio is redirected (a test
        // runner, a script piping the app's output) takes those handles rather than the pseudo-console, and the shell's
        // own output never reaches it.
        si.StartupInfo.dwFlags = NativeMethods.STARTF_USESTDHANDLES;

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

    /// <summary>
    /// Waits briefly for the shell to exit after its PTY/input closed, then force-terminates it if it
    /// is still alive, so a tab close never leaves an orphaned shell. Best-effort; the kill-on-close
    /// job object guarantees cleanup even if this is skipped (e.g. an abnormal process exit).
    /// </summary>
    private void TerminateShell()
    {
        var handle = _processHandle;
        if (handle is null || handle.IsInvalid || handle.IsClosed || _processExited) return;

        try
        {
            var raw = handle.DangerousGetHandle();
            if (NativeMethods.WaitForSingleObject(raw, 1500) != NativeMethods.WAIT_OBJECT_0)
                NativeMethods.TerminateProcess(raw, 0);
        }
        catch { /* best-effort: the job object still backstops cleanup */ }
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Stop the pumps, then signal the shell to exit: closing our input pipe gives it EOF and
        // closing the pseudo-console detaches it. Most shells exit on their own from here.
        _lifetimeCts.Cancel();
        _input?.Dispose();
        _outputStream?.Dispose();

        if (_hPseudoConsole != IntPtr.Zero)
        {
            NativeMethods.ClosePseudoConsole(_hPseudoConsole);
            _hPseudoConsole = IntPtr.Zero;
        }

        // A shell still running a child command won't exit on EOF alone — give it a brief grace
        // period, then force-terminate so we never leak the process. (The job object is the last-
        // resort backstop for abnormal exit; this keeps normal teardown prompt.)
        TerminateShell();

        _lifetimeCts.Dispose();
        _inputWritePipe?.Dispose();
        _outputReadPipe?.Dispose();
        _threadHandle?.Dispose();
        _processHandle?.Dispose();

        if (_environmentBlock != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_environmentBlock);
            _environmentBlock = IntPtr.Zero;
        }

        if (_startupInfoBuffer != IntPtr.Zero)
        {
            NativeMethods.DeleteProcThreadAttributeList(_startupInfoBuffer);
            Marshal.FreeHGlobal(_startupInfoBuffer);
            _startupInfoBuffer = IntPtr.Zero;
        }
    }
}
