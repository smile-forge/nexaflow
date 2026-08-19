using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// The machine-wide half of the UI-test guard rails. <c>[DoNotParallelize]</c> serialises UI tests
/// <i>within</i> an assembly and a <c>static</c> field remembers consent <i>within</i> a process — but a
/// whole-suite run is several test hosts at once, and neither reaches across them. So four projects each
/// launched their own Nexaflow and each asked their own human: apps stacked up stealing one another's
/// clicks, and the prompt arrived once per assembly.
/// <para>
/// This lives in Fixtures because it is the one library every test project already references, and it
/// stays free of FlaUI and WPF so it can. The pieces are deliberately separate: <see cref="Acquire"/>
/// serialises, <see cref="RecordedConsent"/> shares the answer, <see cref="EnsureDpiAware"/> makes the
/// host's pixels agree with UIA's, and <see cref="BringToForeground"/> puts the window under the pointer.
/// </para>
/// </summary>
public static class UiTestGate
{
    /// <summary>Session-scoped (not <c>Global\</c>): UI tests belong to one interactive desktop.</summary>
    private const string GateName = "Nexaflow.UiTests.Gate";

    /// <summary>
    /// Consent is remembered on a sliding window — long enough that one run asks once however many hosts
    /// it starts, short enough that a run begun later in the day asks again rather than assuming.
    /// </summary>
    private static readonly TimeSpan ConsentLifetime = TimeSpan.FromMinutes(30);

    private static string ConsentFile =>
        Path.Combine(Path.GetTempPath(), "nexaflow-uitest-consent.txt");

    /// <summary>
    /// Declares this test host per-monitor DPI aware, which it must be before it clicks anything.
    /// <para>
    /// UIA reports an element's bounds in physical pixels. A DPI-unaware process is handed virtualised
    /// coordinates instead, so on a display at 150% the two disagree by exactly that factor: aim for a row
    /// at (556, 399) and the pointer arrives at (834, 599). Large targets are missed as badly as small
    /// ones — it only looked selective because the app opens with the AI box already focused, so typing
    /// still worked and hid it. The failure that surfaces is "action not found in the ActionStrip",
    /// because the row was never selected.
    /// </para>
    /// Must run before the process creates a window or an automation object; calling it more than once is
    /// harmless (Windows refuses the second, and the first already won).
    /// </summary>
    public static void EnsureDpiAware()
    {
        // -4 = DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2.
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
        catch { /* older Windows, or awareness already fixed by a manifest — nothing better to do */ }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    /// <summary>
    /// Waits for the right to drive the mouse and keyboard, and returns the token that gives it back.
    /// One UI test runs at a time across every test host on this desktop.
    /// <para>
    /// A named semaphore rather than a mutex: MSTest does not promise <c>[TestInitialize]</c> and
    /// <c>[TestCleanup]</c> share a thread, and a mutex may only be released by the thread that took it.
    /// A host killed mid-test therefore leaks its token instead of abandoning it — so the wait times out
    /// rather than blocking forever, and a leak degrades the suite to the behaviour it had before this
    /// existed instead of deadlocking it.
    /// </para>
    /// </summary>
    public static IDisposable Acquire(TimeSpan timeout) => new Token(timeout);

    /// <summary>
    /// The answer a sibling test host already got from the machine's owner, or null if nobody has asked
    /// recently. Reading refreshes the window, so a long run keeps its answer alive.
    /// </summary>
    public static bool? RecordedConsent
    {
        get
        {
            try
            {
                if (!File.Exists(ConsentFile)) return null;

                var parts = File.ReadAllText(ConsentFile).Split('|');
                if (parts.Length != 2) return null;
                if (!DateTime.TryParse(parts[1], CultureInfo.InvariantCulture,
                                       DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                                       out var written))
                    return null;
                if (DateTime.UtcNow - written > ConsentLifetime) return null;

                var allowed = parts[0] == "allowed";
                RecordConsent(allowed);         // slide the window forward
                return allowed;
            }
            catch { return null; }              // unreadable/racing — ask again rather than guess
        }
    }

    /// <summary>Publishes this host's answer so the rest of the run reads it instead of re-asking.</summary>
    public static void RecordConsent(bool allowed)
    {
        try
        {
            File.WriteAllText(ConsentFile,
                (allowed ? "allowed" : "declined") + "|" +
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        catch { /* best-effort: a lost record only costs an extra prompt */ }
    }

    /// <summary>
    /// Puts <paramref name="windowHandle"/> in front and confirms it got there.
    /// <para>
    /// A launched app does not reliably come forward, and from behind another window it still reports its
    /// real on-screen bounds — so FlaUI clicks those coordinates and hits whatever is on top instead. The
    /// test then fails on a missing element, nowhere near the cause.
    /// </para>
    /// <para>
    /// Asking politely is not enough: Windows rejects <c>SetForegroundWindow</c> from a process that is
    /// not itself foreground, silently, so a test host that merely calls it and waits mostly waits in
    /// vain. Attaching our input queue to the current foreground thread for the duration makes us
    /// entitled to the change, which is the long-standing way to do this. Retried until the deadline,
    /// because whatever holds the foreground may be letting go.
    /// </para>
    /// </summary>
    public static bool BringToForeground(IntPtr windowHandle, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            if (GetForegroundWindow() == windowHandle) return true;

            try
            {
                ShowWindow(windowHandle, SW_RESTORE);

                var foreground = GetForegroundWindow();
                var otherThread = GetWindowThreadProcessId(foreground, out _);
                var ourThread = GetCurrentThreadId();
                var attached = otherThread != 0 && otherThread != ourThread
                               && AttachThreadInput(ourThread, otherThread, true);
                try
                {
                    SetForegroundWindow(windowHandle);
                    BringWindowToTop(windowHandle);
                }
                finally
                {
                    if (attached) AttachThreadInput(ourThread, otherThread, false);
                }
            }
            catch { /* nothing better to do than try again until the deadline */ }

            Thread.Sleep(150);
        }
        while (DateTime.UtcNow < deadline);

        return GetForegroundWindow() == windowHandle;
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attachTo, uint attachFrom, bool attach);

    private sealed class Token : IDisposable
    {
        private readonly Semaphore _gate;
        private readonly bool _held;
        private bool _released;

        internal Token(TimeSpan timeout)
        {
            _gate = new Semaphore(1, 1, GateName);
            try { _held = _gate.WaitOne(timeout); }
            catch (AbandonedMutexException) { _held = true; }
        }

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            try { if (_held) _gate.Release(); }
            catch { /* already released or torn down */ }
            _gate.Dispose();
        }
    }
}
