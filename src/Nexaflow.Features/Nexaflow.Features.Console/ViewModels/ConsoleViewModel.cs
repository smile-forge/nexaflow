using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Console;
using Nexaflow.Features.Console.Models;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace Nexaflow.Features.Console.ViewModels;

/// <summary>
/// Backs the ConsoleView.  Owns one <see cref="PseudoConsoleHostService"/>
/// whose shell stays alive for the lifetime of the tab.
///
/// Each call to <see cref="SendCommandAsync"/> writes a line into the PTY
/// and begins a new <see cref="ConsoleEntry"/>.  All output that arrives
/// before the next command is sent is attributed to that entry.
///
/// Raw PTY output arrives as arbitrary-length chunks; this class splits them
/// into lines and appends them to the live entry on the UI thread.
/// </summary>
public partial class ConsoleViewModel : ObservableObject, IDisposable, IQueryHandler
{
    // ── Terminal back-end ─────────────────────────────────────────────────
    protected readonly PseudoConsoleHostService _pty;

    // VT screen buffer: raw PTY bytes are rendered into a cols×rows grid;
    // correct line text is read back from that grid after each chunk.
    private readonly TerminalScreen _screen;
    private readonly int _cols;

    // The entry currently receiving output (null = shell startup / idle)
    private ConsoleEntry? _activeEntry;

    // The echo of the command we just sent — suppress it from output
    private string? _pendingEcho;

    // ── Observable state ──────────────────────────────────────────────────

    /// <summary>All entries (commands + their output) shown in the centre panel.</summary>
    public ObservableCollection<ConsoleEntry> Entries { get; } = [];

    /// <summary>Current path shown in the top bar — updated by parsing shell prompts.</summary>
    [ObservableProperty] private string _currentPath =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>True while the most recently sent command is still running.</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>Live process environment snapshot for the left panel.</summary>
    public ObservableCollection<EnvVar> EnvVars { get; } = [];

    /// <summary>Most-recent-first command history for the right panel.</summary>
    public ObservableCollection<string> CommandHistory { get; } = [];

    // ── Input-prompt overlay (shared with env-var edit) ───────────────────
    [ObservableProperty] private bool   _inputPromptVisible;
    [ObservableProperty] private string _inputPromptTitle  = string.Empty;
    [ObservableProperty] private string _inputPromptLabel  = string.Empty;
    [ObservableProperty] private string _inputPromptValue  = string.Empty;

    private Action<string>? _onPromptConfirm;
    private Action?         _onPromptCancel;

    public void ShowInputPrompt(string title, string label, string initialValue,
                                Action<string> onConfirm, Action? onCancel = null)
    {
        InputPromptTitle   = title;
        InputPromptLabel   = label;
        InputPromptValue   = initialValue;
        _onPromptConfirm   = onConfirm;
        _onPromptCancel    = onCancel;
        InputPromptVisible = true;
    }

    [RelayCommand]
    private void ConfirmInputPrompt()
    {
        InputPromptVisible = false;
        var value = InputPromptValue;
        _onPromptConfirm?.Invoke(value);
        _onPromptConfirm = null;
        _onPromptCancel  = null;
    }

    [RelayCommand]
    private void CancelInputPrompt()
    {
        InputPromptVisible = false;
        _onPromptCancel?.Invoke();
        _onPromptConfirm = null;
        _onPromptCancel  = null;
    }

    // ── Events ────────────────────────────────────────────────────────────
    public event EventHandler? ScrollRequested;

    // ── Construction / startup ────────────────────────────────────────────

    public ConsoleViewModel() : this(new PseudoConsoleHostService()) { }

    /// <summary>Injectable constructor — pass a subclass for PowerShell etc.</summary>
    protected ConsoleViewModel(PseudoConsoleHostService pty, int cols = 220, int rows = 50)
    {
        _cols   = cols;
        _screen = new TerminalScreen(cols, rows);
        _pty    = pty;
        _pty.OutputReceived += OnPtyOutput;
        _pty.TerminalError  += OnTerminalError;
        _pty.ProcessExited  += OnProcessExited;
        _pty.Start((short)cols, (short)rows);

        RefreshEnvVars();
    }

    // ── Public API called by ShellViewModel ───────────────────────────────

    /// <summary>
    /// Writes <paramref name="command"/> into the live PTY and begins a new
    /// <see cref="ConsoleEntry"/> to accumulate the response.
    /// </summary>
    public void SendCommand(string command)
    {
        var trimmed = command.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        // History — unique, most-recent-first: remove any prior occurrence then prepend
        CommandHistory.Remove(trimmed);
        CommandHistory.Insert(0, trimmed);

        // Close off the previous entry
        if (_activeEntry is { IsRunning: true })
            _activeEntry.IsRunning = false;

        // Clear any prompt text sitting in the screen's current row so it
        // doesn't bleed into the next command's output.
        _screen.ClearCurrentRow();

        // Open a fresh entry for this command
        var entry = new ConsoleEntry { Command = trimmed, IsRunning = true };
        Entries.Add(entry);
        _activeEntry = entry;
        IsBusy       = true;
        _pendingEcho = trimmed;   // suppress PTY echo of this line

        ScrollRequested?.Invoke(this, EventArgs.Empty);

        _pty.WriteInput(trimmed);
    }

    /// <summary>Sends Ctrl-C to the hosted shell.</summary>
    public void SendCtrlC()
    {
        _pty.SendCtrlC();
        if (_activeEntry is { IsRunning: true })
        {
            _activeEntry.Lines.Add(new ConsoleOutputLine { Text = "^C", IsError = true });
            _activeEntry.IsRunning = false;
        }
        IsBusy = false;
    }

    /// <summary>Re-runs a history entry when the user clicks it.</summary>
    [RelayCommand]
    private void Rerun(string command) => SendCommand(command);

    // ── PTY output handling ───────────────────────────────────────────────
    //
    // Raw bytes from ConPTY are fed directly into the TerminalScreen, which
    // maintains a correct 2-D character grid (handling CR, LF, cursor moves,
    // erases, etc.).  After each chunk we drain any lines the screen has
    // committed (via LF / scroll) and check whether the current row looks
    // like a shell prompt.

    private void OnPtyOutput(string rawChunk)
    {
        // Feed is thread-safe for single-producer; always called on pump thread.
        // Marshal to UI thread to modify observable collections.
        Application.Current.Dispatcher.Invoke(() => ProcessScreenOutput(rawChunk));
    }

    private void ProcessScreenOutput(string rawChunk)
    {
        _screen.Feed(rawChunk);

        // Drain all fully-committed lines from the screen buffer.
        foreach (var line in _screen.TakeLines())
            ProcessLine(line);

        // Check whether the cursor row now holds a shell prompt.
        var prompt = _screen.PeekPromptLine();
        if (prompt is not null)
        {
            var path = ExtractPathFromPrompt(prompt);
            if (path is not null)
            {
                CurrentPath = path;
                RefreshEnvVars();
            }
            if (_activeEntry is { IsRunning: true })
            {
                _activeEntry.IsRunning = false;
                IsBusy = false;
            }
            _screen.ClearCurrentRow();
        }

        ScrollRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ProcessLine(string line)
    {
        // Suppress PTY echo of the command we just sent.
        // The echo can arrive as just the command ("dir") OR with the prompt
        // prepended ("D:\path>dir") depending on the shell and conhost mode.
        if (_pendingEcho is not null)
        {
            var trimmed = line.Trim();
            if (string.Equals(trimmed, _pendingEcho, StringComparison.Ordinal) ||
                trimmed.EndsWith('>' + _pendingEcho, StringComparison.Ordinal))
            {
                _pendingEcho = null;
                return;
            }
        }

        // Skip blank lines before the first command (startup banner noise).
        if (_activeEntry is null && string.IsNullOrWhiteSpace(line)) return;

        // Skip lines that are themselves prompt-shaped (they may appear as
        // completed lines during startup before we see a real prompt row).
        if (ExtractPathFromPrompt(line) is not null) return;

        if (_activeEntry is null) return;
        _activeEntry.Lines.Add(new ConsoleOutputLine { Text = line, IsError = false });
    }

    private void OnTerminalError(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_activeEntry is null)
            {
                var errEntry = new ConsoleEntry { Command = "[terminal error]", IsRunning = false };
                Entries.Add(errEntry);
                _activeEntry = errEntry;
            }
            _activeEntry.Lines.Add(new ConsoleOutputLine { Text = $"[PTY] {message}", IsError = true });
            if (_activeEntry.IsRunning) _activeEntry.IsRunning = false;
            IsBusy = false;
        });
    }

    private void OnProcessExited(int exitCode)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_activeEntry is { IsRunning: true })
                _activeEntry.IsRunning = false;
            IsBusy = false;
        });
    }

    // ── Prompt detection ──────────────────────────────────────────────────
    //
    // The screen buffer gives us clean text with cursor-move effects already
    // applied, so prompt detection is straightforward: does the row end in '>'
    // and contain a drive-letter path?

    private static readonly Regex PromptPathRegex = new(
        @"(?:PS )?[A-Za-z]:\\[^>]*>",
        RegexOptions.Compiled);

    private static string? ExtractPathFromPrompt(string line)
    {
        var t = line.TrimEnd();
        if (t.Length < 3 || !t.EndsWith('>')) return null;
        var m = PromptPathRegex.Match(t);
        if (!m.Success) return null;
        var path = m.Value[..^1];   // strip trailing '>'
        if (path.StartsWith("PS ", StringComparison.OrdinalIgnoreCase))
            path = path[3..];
        return path.Trim();
    }

    // ── Environment snapshot ──────────────────────────────────────────────

    [RelayCommand]
    private void CopyEnvVar(EnvVar? env)
    {
        if (env is null) return;
        Clipboard.SetText($"{env.Name}={env.Value}");
    }

    [RelayCommand]
    private void EditEnvVar(EnvVar? env)
    {
        if (env is null) return;
        ShowInputPrompt(
            title:        $"Edit — {env.Name}",
            label:        "New value:",
            initialValue: env.Value,
            onConfirm:    newVal =>
            {
                env.Value = newVal;
                Environment.SetEnvironmentVariable(env.Name, newVal);
            });
    }

    private void RefreshEnvVars()
    {
        EnvVars.Clear();
        foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
        {
            EnvVars.Add(new EnvVar
            {
                Name  = kv.Key?.ToString()   ?? string.Empty,
                Value = kv.Value?.ToString() ?? string.Empty
            });
        }
        // Sort in-place after bulk add
        var sorted = EnvVars.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        EnvVars.Clear();
        foreach (var v in sorted) EnvVars.Add(v);
    }

    // ── IQueryHandler ─────────────────────────────────────────────────────

    public string Description =>
        "Executes terminal/shell commands. Prefix the input with '>' to run it, e.g. '>dir'.";

    public float CanProcess(string input, IPageView? page = null) =>
        input.TrimStart().StartsWith('>') ? 1.0f : 0f;

    public Task<string?> ProcessAsync(string input, IPageView? page = null)
    {
        SendCommand(input.TrimStart()[1..].TrimStart());
        return Task.FromResult<string?>(null);
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pty.OutputReceived -= OnPtyOutput;
        _pty.TerminalError  -= OnTerminalError;
        _pty.ProcessExited  -= OnProcessExited;
        _pty.Dispose();
    }
}
