using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common.Terminal;
using Nexaflow.IO.Terminal;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Terminal.Models;
using Nexaflow.Visuals.Terminal.Shells;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace Nexaflow.Visuals.Terminal.ViewModels;

/// <summary>
/// Reusable terminal page view-model: owns one terminal transport (<see cref="ITerminalTransport"/>), started by the
/// <see cref="TerminalShellRoute"/> it is given — cmd in ConPTY on this computer, a location's shell elsewhere — whose
/// shell stays alive for the tab's lifetime, renders its output at the size the view has room for, and tracks the working
/// directory, environment snapshot and command history. How to speak to the shell — phrasing <c>cd</c>/<c>set</c>, reading
/// its prompt or following the folder it reports and the prompts it marks — is the route's
/// <see cref="TerminalShellDialect"/>; the environment source/persistence are supplied by a feature-specific subclass — so
/// cmd and a future PowerShell feature share everything here without referencing each other.
/// </summary>
public abstract partial class TerminalViewModel : ObservableObject, IDisposable, IPageViewModel, IChatEngagement
{
    // ── Terminal back-end ─────────────────────────────────────────────────
    private readonly TerminalShellRoute _route;
    private readonly Lock _transportGate = new();
    private ITerminalTransport? _transport;            // null until the launcher hands it over
    private readonly List<string> _pendingInput = [];   // typed before the shell was there

    // Output arrives as raw bytes and a code point may straddle two chunks, so one decoder carries what is left over.
    private readonly Decoder _decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetDecoder();

    // The screen's bounds in cells: a collapsed pane still gives the shell room to print, and a vast one stays a
    // size a pseudo-console takes.
    private const int MinColumns = 20, MinRows = 5, MaxColumns = 1000, MaxRows = 500;

    // VT screen buffer: raw PTY bytes are rendered into a cols×rows grid;
    // the visible screen + scrollback are read back from it after each chunk.
    private readonly TerminalScreen _screen;
    private int _cols;
    private int _rows;

    // True while the cursor row is a bare shell prompt (vs. a command running / output flowing). Drives
    // the init sequence (fire once per prompt) and gates the Enter command/query routing.
    private bool _atPrompt;

    // Set once the shell marks its prompt cycle (OSC 133): from then on its marks, not how a prompt looks, say where it is.
    private bool _marksPrompts;

    // Where the latest prompt started (A) and where typing starts after it (B), on the screen's line numbering.
    private ShellMark? _promptStart;
    private ShellMark? _inputStart;

    // ── Environment / tab ─────────────────────────────────────────────────
    protected readonly IShellServices _shell;
    protected readonly IAIService     _ai;
    protected TerminalEnvironment?    _activeEnv;

    /// <summary>Set by the page registration so the VM can update its own page title/breadcrumbs.</summary>
    public Page? Tab { get; set; }

    // Init state machine: 0 = waiting for the first prompt, 1 = cd sent, waiting for the next prompt,
    // 2 = normal operation.
    private int     _initPhase;
    private string? _pendingInitCmd;

    // ── Observable state ──────────────────────────────────────────────────

    /// <summary>Lines that have scrolled off the top of the screen — the terminal scrollback.</summary>
    public ObservableCollection<string> Scrollback { get; } = [];

    /// <summary>The live terminal screen (the visible VT grid), rendered as monospace text and rebuilt
    /// on each chunk. Together with <see cref="Scrollback"/> this is the full terminal display.</summary>
    [ObservableProperty] private string _screenText = string.Empty;

    /// <summary>The shell's folder, shown in the top bar, as its dialect names folders — read off its prompt, or followed
    /// as the shell reports it.</summary>
    [ObservableProperty] private string _currentPath =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>True while the most recently sent command is still running.</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>Live process environment snapshot for the Environment sub-tab.</summary>
    public ObservableCollection<EnvVar> EnvVars { get; } = [];

    /// <summary>Filter text for the Environment list (matches name or value, case-insensitive).</summary>
    [ObservableProperty] private string _envFilter = string.Empty;

    /// <summary>Contents of the current directory for the Files sub-tab (folders first).</summary>
    public ObservableCollection<TerminalFsEntry> Files { get; } = [];

    private ICollectionView? _envView;

    partial void OnEnvFilterChanged(string value) => _envView?.Refresh();

    private bool MatchesEnvFilter(EnvVar? e)
    {
        if (e is null) return false;
        var f = EnvFilter;
        return string.IsNullOrWhiteSpace(f)
            || e.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
            || e.Value.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Most-recent-first command history for the right panel.</summary>
    public ObservableCollection<string> CommandHistory { get; } = [];

    /// <summary>Cursor into <see cref="CommandHistory"/> while navigating with Up/Down (-1 = not navigating).</summary>
    private int _historyCursor = -1;

    // ── Events ────────────────────────────────────────────────────────────
    public event EventHandler? ScrollRequested;

    // ── Subclass hooks ────────────────────────────────────────────────────

    /// <summary>The environments this terminal can switch between (from the feature's config).</summary>
    protected abstract IReadOnlyList<TerminalEnvironment> Environments { get; }

    /// <summary>The environment name bound to a folder, or null if none is remembered.</summary>
    protected abstract string? FindBoundEnvName(string folderPath);

    /// <summary>Persists "always use this environment for this folder".</summary>
    protected abstract void PersistFolderBinding(string folderPath, string envName);

    /// <summary>How to speak to the shell this terminal runs.</summary>
    protected TerminalShellDialect Dialect => _route.Dialect;

    /// <summary>The feature's workspace-config section name, for the top-bar Configure deep link. Null
    /// when the shell exposes no configurable environments (the button is then hidden).</summary>
    protected virtual string? ConfigSectionName => null;

    /// <summary>Whether the top-bar Configure button is shown (a config section exists to link to).</summary>
    public bool ShowConfigureButton => ConfigSectionName is not null;

    /// <summary>Opens the workspace Configure overlay on this shell's environments section.</summary>
    [RelayCommand]
    private void Configure()
    {
        if (ConfigSectionName is { } section) _shell.OpenWorkspaceConfig(section);
    }

    // ── Construction / startup ────────────────────────────────────────────

    /// <param name="route">The shell this terminal runs — what starts it, how to speak to it, and where it starts.</param>
    /// <param name="cols">The width the shell starts at, until the view says how much room it has.</param>
    /// <param name="rows">The height the shell starts at, likewise.</param>
    protected TerminalViewModel(TerminalShellRoute route, IShellServices shell, IAIService ai,
                                int cols = 220, int rows = 50)
    {
        _shell    = shell;
        _ai       = ai;
        _cols     = cols;
        _rows     = rows;
        _route    = route;
        _screen   = new TerminalScreen(cols, rows);
        _screen.WorkingFolderReported += OnWorkingFolderReported;
        _screen.ShellMarked           += OnShellMarked;
        if (route.StartFolder is { } start) CurrentPath = start;

        _envView = CollectionViewSource.GetDefaultView(EnvVars);
        _envView.Filter = o => MatchesEnvFilter(o as EnvVar);

        // The shell is started in SetupInitialState — after the active environment's variable overrides
        // are known, so they can be applied at process creation.
    }

    /// <summary>
    /// Resolves the active environment, applies its variable overrides to the shell, and starts it in the route's start
    /// folder. Call once from the subclass constructor after its environment source is set.
    /// </summary>
    protected void SetupInitialState(TerminalEnvironment? env, bool pickerPending = false)
    {
        // When a launch picker is pending, start with no active environment (and no initial command) —
        // the chosen environment is applied to the running shell once the user picks.
        _activeEnv = pickerPending ? null : (env ?? Environments.FirstOrDefault());

        // Start the shell directly in the launch folder (lpCurrentDirectory) and overlay the
        // environment's variable overrides at process creation — no visible `cd` entry on open.
        var overrides = _activeEnv?.EnvOverrides is { Count: > 0 } envOverrides ? envOverrides : null;

        // Only the initial command is deferred to the first prompt; the launch folder is already applied.
        _pendingInitCmd  = _activeEnv?.InitialCommand;
        _initPhase       = _pendingInitCmd is not null ? 0 : 2;

        ShellStarted = StartAsync(new TerminalLaunch(_route.StartFolder, overrides, _cols, _rows));
        RefreshEnvVars();
        RefreshFiles();
    }

    /// <summary>The shell's start — awaited by tests; done once the launcher has handed over the transport, or failed.</summary>
    public Task ShellStarted { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Launches the shell and takes its transport: its output, failure and exit are followed, what was typed before it was
    /// there is sent, and a size the view settled on meanwhile is passed on. A launch that fails is reported in the
    /// scrollback, as a transport failure is.
    /// </summary>
    private async Task StartAsync(TerminalLaunch launch)
    {
        ITerminalTransport transport;
        try { transport = await _route.Launcher.LaunchAsync(launch, _disposeCts.Token); }
        catch (OperationCanceledException) { return; }
        catch (Exception e)
        {
            OnTerminalError(e.Message);
            return;
        }

        string[] pending;
        lock (_transportGate)
        {
            if (_disposed)
            {
                transport.Dispose();
                return;
            }
            _transport = transport;
            pending    = [.. _pendingInput];
            _pendingInput.Clear();
        }

        transport.Output += OnTransportOutput;
        transport.Failed += OnTerminalError;
        transport.Exited += OnProcessExited;
        foreach (var text in pending) transport.Write(Encoding.UTF8.GetBytes(text));
        if ((_cols, _rows) != (launch.Columns, launch.Rows)) transport.Resize(_cols, _rows);
        ShellCommandsListed = LoadShellCommandsAsync();
    }

    /// <summary>Sends <paramref name="text"/> as typed; before the shell is there it waits, and goes once it is.</summary>
    private void Send(string text)
    {
        ITerminalTransport? transport;
        lock (_transportGate)
        {
            transport = _transport;
            if (transport is null)
            {
                _pendingInput.Add(text);
                return;
            }
        }
        transport.Write(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Tells the shell the size the view has room for, in character cells, kept between a small and a large bound: the
    /// screen takes it — rows that no longer fit move into the scrollback — and the transport passes it to the shell,
    /// which redraws for it. Called on the UI thread.
    /// </summary>
    public void Resize(int columns, int rows)
    {
        columns = Math.Clamp(columns, MinColumns, MaxColumns);
        rows    = Math.Clamp(rows, MinRows, MaxRows);
        if (columns == _cols && rows == _rows) return;

        _cols = columns;
        _rows = rows;
        _screen.Resize(columns, rows);
        foreach (var line in _screen.TakeScrollback())
            Scrollback.Add(line.TrimEnd());
        ScreenText = string.Join("\n", _screen.Snapshot()).TrimEnd('\n');

        ITerminalTransport? transport;
        lock (_transportGate) transport = _transport;
        transport?.Resize(columns, rows);
    }

    protected TerminalEnvironment? FindEnvByName(string name)
        => Environments.FirstOrDefault(e =>
               string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    // ── Public API called by the shell ────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="command"/> in the shell as written: at a prompt it first empties the line, so the command is
    /// never joined onto what was half-typed, then sends the text plus a carriage return and records it in history.
    /// Used by the init sequence, the AI bar, the assistant, history re-run, env switches
    /// and <c>set</c> writes — the user types directly into the terminal instead (see <see cref="SendText"/>).
    /// </summary>
    public void SendCommand(string command)
    {
        var trimmed = command.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        RecordHistory(trimmed);
        if (_atPrompt) Send(Dialect.ClearInputLine);
        IsBusy   = true;
        _atPrompt = false;
        Send(trimmed + "\r");
        ScrollRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RecordHistory(string command)
    {
        CommandHistory.Remove(command);
        CommandHistory.Insert(0, command);
        _historyCursor = -1;
    }

    /// <summary>Sends Ctrl-C to the hosted shell (interrupt the running program / clear the line).</summary>
    public void SendCtrlC()
    {
        EngagementInvalidated?.Invoke();
        StopWatcher();
        _transport?.Interrupt();
        IsBusy = false;
    }

    /// <summary>
    /// Hard-stop (Ctrl+Break): force-kills the foreground command's process tree while keeping the shell alive,
    /// so a wedged command that ignored Ctrl-C can always be recovered from — the pane falls back to a prompt.
    /// There is deliberately no automatic timeout; a legitimately long, silent command is left to run.
    /// </summary>
    public void HardStop()
    {
        EngagementInvalidated?.Invoke();
        StopWatcher();
        _transport?.TerminateForeground();
        IsBusy = false;
    }

    /// <summary>Re-runs a history entry when the user clicks it.</summary>
    [RelayCommand]
    private void Rerun(string command) => SendCommand(command);

    // ── Terminal input (keystrokes go straight to the shell) ──────────────

    /// <summary>Forwards a printable character (or pasted text) to the shell as typed.</summary>
    public void SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        // The user is typing into the terminal: dissolve any AI banner, and — for a real character (not a
        // lone space) — take over any background-command watch.
        EngagementInvalidated?.Invoke();
        if (!string.IsNullOrWhiteSpace(text)) StopWatcher();
        Send(text);
    }

    /// <summary>
    /// Forwards a special key (arrows, Backspace, Tab, Ctrl-combos, …) to the shell. Returns true if the
    /// key was a forwardable one (so the view suppresses default handling); printable keys return false
    /// and are delivered via <see cref="SendText"/> from TextInput. Enter is handled by <see cref="HandleEnter"/>.
    /// </summary>
    public bool SendKey(Key key, ModifierKeys modifiers)
    {
        var seq = TerminalKeys.Encode(key, modifiers);
        if (seq is null) return false;
        // A special key (arrow / backspace / Ctrl-combo) is real interaction: dissolve the banner and
        // take over any background-command watch.
        EngagementInvalidated?.Invoke();
        StopWatcher();
        Send(seq);
        return true;
    }

    /// <summary>
    /// Handles Enter. What is typed in the console is the shell's, whatever it reads like: at a prompt the line runs and is
    /// recorded in history, and while a program runs Enter is simply forwarded so interactive input works.
    /// </summary>
    public void HandleEnter()
    {
        // Pressing Enter is a user action — dissolve any lingering AI banner. (A lone Enter does NOT stop
        // a background-command watch; the watcher only yields to real typing or Ctrl-C.)
        EngagementInvalidated?.Invoke();

        var decision = TerminalEnterRouting.Decide(_atPrompt ? CurrentInputLine() : null);
        if (decision.Action == TerminalEnterAction.RunAsCommand)
        {
            if (decision.Text.Length > 0) RecordHistory(decision.Text);
            IsBusy    = true;
            _atPrompt = false;
        }
        Send("\r");
    }

    /// <summary>The text typed after the prompt: from where a marking shell said typing starts, or past the prompt the cursor
    /// row starts with.</summary>
    private string? CurrentInputLine()
    {
        var snap      = _screen.Snapshot();
        int cursorRow = _screen.CursorRow;
        if (_marksPrompts && _inputStart is { } start)
        {
            int row = (int)(start.Line - _screen.TopLine);
            if (row >= 0 && row <= cursorRow)
            {
                // A line longer than the screen wraps: what is typed runs on over the rows below.
                var typed = new StringBuilder();
                for (int r = row; r <= cursorRow; r++)
                {
                    var text = snap.ElementAtOrDefault(r) ?? string.Empty;
                    typed.Append(r != row ? text : start.Column < text.Length ? text[start.Column..] : string.Empty);
                }
                return typed.ToString().TrimEnd();
            }
        }

        var line = (snap.ElementAtOrDefault(cursorRow) ?? string.Empty).TrimEnd();
        return line[(Dialect.ReadPrompt(line)?.InputColumn ?? 0)..];
    }

    /// <summary>
    /// What a line typed in the AI bar is, for this terminal's shell — by its built-ins and the programs its host has. The
    /// query handler runs a command, has a model asked about an unclear line, and leaves a request to the assistant.
    /// </summary>
    public ConsoleLineVerdict JudgeConsoleLine(string input)
        => CommandClassifier.JudgeLine(input, name => Dialect.KnowsCommand(name, _shellCommands));

    private volatile IReadOnlySet<string> _shellCommands = System.Collections.Frozen.FrozenSet<string>.Empty;

    /// <summary>The shell's programs being listed, once it has started — awaited by tests.</summary>
    public Task ShellCommandsListed { get; private set; } = Task.CompletedTask;

    /// <summary>Asks the launcher what the shell runs by name. Without an answer the built-ins stand, and more lines are
    /// unclear; nothing fails.</summary>
    private async Task LoadShellCommandsAsync()
    {
        try
        {
            if (await _route.Launcher.ListCommandsAsync(_disposeCts.Token).ConfigureAwait(false) is { } listed)
                _shellCommands = listed;
        }
        catch (Exception e)
        {
            System.Diagnostics.Trace.TraceWarning($"The shell's commands could not be listed: {e.Message}");
        }
    }

    // ── Chat-bar participation (the bar can still drive the terminal) ──────

    /// <summary>
    /// Handles AI-bar keys while a <c>&gt;</c>-prefixed command targets this terminal: Up/Down step
    /// through the command history. Returns <see cref="ChatKeyResult.NotHandled"/> otherwise so normal
    /// bar handling (and Tab ghost-completion) runs. Path completion is layered on later.
    /// </summary>
    public ChatKeyResult HandleChatKey(Key key, ModifierKeys modifiers, string text, int caretIndex)
    {
        if (modifiers != ModifierKeys.None) return ChatKeyResult.NotHandled;

        // Recall history when the bar is empty or already in console mode ('>' prefix). An empty bar
        // counts because the console tab is active — Up should behave like a terminal. A non-'>' draft
        // is left alone so Up/Down still move the caret in a plain AI message.
        if (text.Length != 0 && !text.StartsWith('>')) return ChatKeyResult.NotHandled;

        return key switch
        {
            Key.Up   => HistoryStep(+1),
            Key.Down => HistoryStep(-1),
            _        => ChatKeyResult.NotHandled,
        };
    }

    private ChatKeyResult HistoryStep(int delta)
    {
        if (CommandHistory.Count == 0) return ChatKeyResult.NotHandled;

        int next = Math.Clamp(_historyCursor + delta, -1, CommandHistory.Count - 1);
        _historyCursor = next;

        // History is most-recent-first: Up (+1) walks toward older; Down (-1) back to an empty '>'.
        var newText = next < 0 ? ">" : ">" + CommandHistory[next];
        return new ChatKeyResult(true, newText, newText.Length);
    }

    // ── PTY output handling ───────────────────────────────────────────────

    /// <summary>Decodes a chunk of the shell's output — carrying a code point split across chunks to the next — and renders
    /// it on the UI thread. Called one chunk at a time on the transport's reader thread.</summary>
    private void OnTransportOutput(ReadOnlyMemory<byte> chunk)
    {
        var chars = new char[_decoder.GetCharCount(chunk.Span, flush: false)];
        int count = _decoder.GetChars(chunk.Span, chars, flush: false);
        if (count == 0) return;
        var text = new string(chars, 0, count);
        _ = _shell.RunOnUiAsync(() => ProcessScreenOutput(text));
    }

    private void ProcessScreenOutput(string rawChunk)
    {
        _screen.Feed(rawChunk);

        // Lines that scrolled off the top become scrollback; the visible grid is the live screen.
        foreach (var line in _screen.TakeScrollback())
            Scrollback.Add(line.TrimEnd());
        ScreenText = string.Join("\n", _screen.Snapshot()).TrimEnd('\n');

        // While capturing a command's output, reset the quiet-debounce, so the capture settles once output stops.
        if (_capturing) ResetQuietTimer();

        // A shell that marks its prompts says itself when one returns (OnShellMarked). For one that does not, a bare
        // prompt on the cursor row means the previous command finished. Handle it once per prompt (not on every chunk,
        // and not while the user is mid-typing — the row then ends with the typed text, so it is no bare prompt and
        // _atPrompt simply stays set).
        if (!_marksPrompts && !_atPrompt && Dialect.ReadPrompt(_screen.CursorRowText) is { IsBare: true } prompt)
        {
            if (prompt.Folder is { } folder) MoveTo(folder);
            OnPromptReturned(knowsFolder: prompt.Folder is not null);
        }

        ScrollRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnTerminalError(string message)
        => _ = _shell.RunOnUiAsync(() => { Scrollback.Add($"[PTY] {message}"); IsBusy = false; });

    private void OnProcessExited(int exitCode)
        => _ = _shell.RunOnUiAsync(() => IsBusy = false);

    // ── Prompt detection ──────────────────────────────────────────────────
    //
    // The screen buffer gives us clean text with cursor-move effects already
    // applied, so prompt detection is straightforward: does the row end in '>'
    // and contain a drive-letter path?

    // ── Prompts and folders ───────────────────────────────────────────────
    //
    // The screen buffer gives clean text with cursor-move effects already applied, so a prompt the dialect reads off the
    // cursor row is reliable; a shell that marks its prompts (OSC 133) and reports its folder (OSC 7) needs no reading.

    /// <summary>The shell is back at a prompt: the command that ran has finished. With its folder known, the panels catch up,
    /// the start-up sequence moves on and the tab follows.</summary>
    private void OnPromptReturned(bool knowsFolder)
    {
        _atPrompt = true;
        IsBusy    = false;
        _captureTcs?.TrySetResult(true);   // a capture in flight: the command finished (clean prompt)
        if (!knowsFolder) return;

        RefreshEnvVars();
        HandlePromptDetected();
        SyncTabMeta();
    }

    /// <summary>The shell is in <paramref name="folder"/>; the Files panel is listed again only when that is a move.</summary>
    private void MoveTo(string folder)
    {
        bool moved  = !string.Equals(folder, CurrentPath, Dialect.PathComparison);
        CurrentPath = folder;
        if (moved) RefreshFiles();
    }

    /// <summary>Follows the folder the shell reports (OSC 7), where it names one this terminal reaches.</summary>
    private void OnWorkingFolderReported(TerminalWorkingFolder reported)
    {
        if (Dialect.ToFolder(reported) is { } folder) MoveTo(folder);
    }

    /// <summary>Follows a shell that marks its prompt cycle (OSC 133): a prompt has returned once typing starts after it
    /// (B), and a command runs once its output starts (C).</summary>
    private void OnShellMarked(ShellMark mark)
    {
        _marksPrompts = true;
        switch (mark.Kind)
        {
            case ShellMarkKind.PromptStart:
                _promptStart = mark;
                break;
            case ShellMarkKind.InputStart:
                _inputStart = mark;
                if (!_atPrompt) OnPromptReturned(knowsFolder: true);
                break;
            case ShellMarkKind.OutputStart:
                _atPrompt = false;
                IsBusy    = true;
                break;
        }
    }

    // ── Init state machine ────────────────────────────────────────────────
    //
    // Called each time a shell prompt is detected.  Drives the three phases:
    //   0 — awaiting the very first prompt; send cd if a path was requested
    //   1 — awaiting the post-cd prompt; send InitialCommand if defined
    //   2 — normal operation; nothing to do

    // ── Init state machine ────────────────────────────────────────────────
    //
    // Called each time a prompt returns with its folder known. The shell starts in its folder, so only the environment's
    // initial command waits for a prompt:
    //   0 — awaiting the very first prompt
    //   1 — awaiting the prompt after a cd (ApplyParams)
    //   2 — normal operation; nothing to do

    private void HandlePromptDetected()
    {
        if (_initPhase == 2) return;
        _initPhase = 2;
        if (_pendingInitCmd is not null)
        {
            var cmd = _pendingInitCmd;
            _pendingInitCmd = null;
            SendCommand(cmd);
        }
    }

    // Keeps the tab's title and PageParams["path"] in sync with actual shell state.
    // Called on every prompt detection so that Reinitialize correctly sees the current
    // path and won't issue a spurious cd.  Title is only passed when there's an active env.
    private void SyncTabMeta()
    {
        if (Tab is null) return;

        string? title = _activeEnv?.TabTitle;

        if (title is { Length: > 0 })
        {
            Tab.Title = title;
            Tab.Breadcrumbs.Clear();
            Tab.Breadcrumbs.Add(new BreadcrumbSegment { Label = title });
        }

        if (!string.IsNullOrEmpty(CurrentPath))
            Tab.PageParams = new Dictionary<string, string> { ["path"] = CurrentPath };
    }

    /// <summary>Re-applies path/env params when an existing tab is re-activated with new params.</summary>
    public void ApplyParams(string? newPath, string? envName)
    {
        if (envName is not null)
        {
            var env = FindEnvByName(envName);
            if (env is not null && env != _activeEnv)
            {
                _activeEnv = env;
                _pendingInitCmd = env.InitialCommand;
                SyncTabMeta();
            }
        }

        // Only navigate when the shell is not already at the requested path.
        // This prevents spurious cd commands when the tab is merely refocused.
        bool pathChanged = newPath is not null
            && !string.Equals(newPath, CurrentPath, Dialect.PathComparison);

        if (pathChanged)
        {
            if (Dialect.FormatCdCommand(newPath!) is { } cd)
            {
                _initPhase = _pendingInitCmd is not null ? 1 : 2;
                SendCommand(cd);
            }
            else Scrollback.Add(Str.Format("Terminal.Console.Unreachable", newPath!));
        }
        else if (_pendingInitCmd is not null && !IsBusy)
        {
            var cmd = _pendingInitCmd;
            _pendingInitCmd = null;
            SendCommand(cmd);
        }
    }

    // ── Environment snapshot ──────────────────────────────────────────────

    [RelayCommand]
    private void CopyEnvVar(EnvVar? env)
    {
        if (env is null) return;
        Clipboard.SetText($"{env.Name}={env.Value}");
    }

    /// <summary>Inserts text into the shell AI bar — backs console paste and the output drop target.</summary>
    public void InsertIntoChatInput(string text) => _shell.InsertChatInput(text);

    /// <summary>Console "paste": drops the clipboard text into the AI bar so it can be run/edited.</summary>
    [RelayCommand]
    private void PasteToBar()
    {
        if (Clipboard.ContainsText()) _shell.InsertChatInput(Clipboard.GetText());
    }

    // ── Env-var add/edit overlay (name + value + scope) ───────────────────
    [ObservableProperty] private bool     _envEditVisible;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnvEditNameReadOnly))]
    private bool     _envEditIsNew;
    [ObservableProperty] private string   _envEditName  = string.Empty;
    [ObservableProperty] private string   _envEditValue = string.Empty;
    [ObservableProperty] private EnvScope _envEditScope = EnvScope.Session;

    /// <summary>Scopes offered in the edit overlay's selector.</summary>
    public IReadOnlyList<EnvScope> EnvScopes { get; } = [EnvScope.Session, EnvScope.User, EnvScope.Machine];

    /// <summary>The name is fixed when editing an existing variable; only new variables can name themselves.</summary>
    public bool EnvEditNameReadOnly => !EnvEditIsNew;

    [RelayCommand]
    private void EditEnvVar(EnvVar? env)
    {
        if (env is null) return;
        EnvEditIsNew  = false;
        EnvEditName   = env.Name;
        EnvEditValue  = env.Value;
        EnvEditScope  = EnvScope.Session;
        EnvEditVisible = true;
    }

    [RelayCommand]
    private void AddEnvVar()
    {
        EnvEditIsNew  = true;
        EnvEditName   = string.Empty;
        EnvEditValue  = string.Empty;
        EnvEditScope  = EnvScope.Session;
        EnvEditVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmEnvEdit()
    {
        EnvEditVisible = false;
        var name = EnvEditName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        if (await WriteEnvVarAsync(EnvEditScope, name, EnvEditValue))
        {
            // Reflect the change in the snapshot. A Session write also shows up at the next prompt
            // refresh; User/Machine writes are visible immediately via GetEnvironmentVariables.
            var existing = EnvVars.FirstOrDefault(e =>
                string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) existing.Value = EnvEditValue;
            else                      EnvVars.Add(new EnvVar { Name = name, Value = EnvEditValue });
        }
    }

    [RelayCommand]
    private void CancelEnvEdit() => EnvEditVisible = false;

    // ── Launch-time environment picker ────────────────────────────────────
    [ObservableProperty] private bool _envPickerVisible;
    [ObservableProperty] private bool _alwaysUseHere;

    /// <summary>Environments offered by the launch picker.</summary>
    public ObservableCollection<TerminalEnvironment> EnvPickerOptions { get; } = [];

    private string? _pickerFolder;

    /// <summary>Shows the "which environment?" picker for a folder launched via Cmd Here.</summary>
    public void ShowEnvironmentPicker(IEnumerable<TerminalEnvironment> options, string folder)
    {
        EnvPickerOptions.Clear();
        foreach (var o in options) EnvPickerOptions.Add(o);
        _pickerFolder    = folder;
        AlwaysUseHere    = false;
        EnvPickerVisible = true;
    }

    [RelayCommand]
    private void PickEnvironment(TerminalEnvironment? env)
    {
        EnvPickerVisible = false;
        if (env is null) return;

        ApplyPickedEnvironment(env);
        if (AlwaysUseHere && _pickerFolder is not null)
            PersistFolderBinding(_pickerFolder, env.Name);
    }

    [RelayCommand]
    private void CancelEnvPicker()
    {
        EnvPickerVisible = false;
        // No choice made — fall back to the first configured environment.
        if (Environments.FirstOrDefault() is { } first) ApplyPickedEnvironment(first);
    }

    private void ApplyPickedEnvironment(TerminalEnvironment env)
    {
        _activeEnv = env;
        SyncTabMeta();

        // The shell is already running, so overrides go in via live `set` rather than the process block.
        foreach (var kv in env.EnvOverrides)
            SendCommand(Dialect.FormatSetCommand(kv.Key, kv.Value));

        if (!string.IsNullOrEmpty(env.InitialCommand))
        {
            if (!IsBusy) SendCommand(env.InitialCommand);
            else         _pendingInitCmd = env.InitialCommand;
        }
    }

    /// <summary>
    /// Writes an environment variable at the requested scope:
    /// <list type="bullet">
    /// <item><b>Session</b> — a <c>set</c>/<c>$env:</c> command into the running shell, so it actually
    /// takes effect in this terminal (the previous behaviour mutated only Nexaflow's own process and the
    /// shell never saw it).</item>
    /// <item><b>User</b> — the current user's persistent environment (no elevation).</item>
    /// <item><b>Machine</b> — the machine-wide environment via the elevated bridge (approval-gated).</item>
    /// </list>
    /// </summary>
    public async Task<bool> WriteEnvVarAsync(EnvScope scope, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        switch (scope)
        {
            case EnvScope.Session:
                SendCommand(Dialect.FormatSetCommand(name, value));
                return true;

            case EnvScope.User:
                try
                {
                    Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
                    return true;
                }
                catch (Exception ex)
                {
                    _shell.ShowError(Str.Format("Terminal.EnvEdit.UserSetFailed", name, ex.Message));
                    return false;
                }

            case EnvScope.Machine:
                var res = await _shell.RunElevatedAsync(ElevatedRequest.Single(
                    ElevatedOps.EnvSet,
                    (ElevatedArgs.EnvName,   name),
                    (ElevatedArgs.EnvValue,  value),
                    (ElevatedArgs.EnvTarget, "Machine")));
                if (!res.Success && !res.WasDeclined) _shell.ShowError(res.Message);
                return res.Success;

            default:
                return false;
        }
    }

    private void RefreshEnvVars()
    {
        var sorted = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Select(kv => new EnvVar
            {
                Name  = kv.Key?.ToString()   ?? string.Empty,
                Value = kv.Value?.ToString() ?? string.Empty
            })
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        EnvVars.Clear();
        foreach (var v in sorted) EnvVars.Add(v);
    }

    /// <summary>
    /// Lists the current directory for the Files sub-tab. Files are listed first (they're the panel's
    /// main use — drag a path onto the console/bar); folders follow for navigation.
    /// </summary>
    private void RefreshFiles()
    {
        Files.Clear();
        foreach (var entry in TerminalFileList.Enumerate(CurrentPath)) Files.Add(entry);
    }

    /// <summary>Navigates the shell into a folder from the Files panel (double-click).</summary>
    public void NavigateInto(TerminalFsEntry entry)
    {
        if (entry.IsDirectory && Dialect.FormatCdCommand(entry.FullPath) is { } cd) SendCommand(cd);
    }

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext()
    {
        var ctx = $"Terminal: '{CurrentPath}', running {Dialect.Description}.";

        // If a command the AI ran is still being watched in the background, tell the model about it so a
        // new question carries that state (it hasn't completed; here's the output so far).
        if (_watchCommand is { } watching)
        {
            ctx += $" A command `{watching}` you ran {DescribeElapsed(DateTime.Now - _watchStart)} ago is " +
                   "still running and hasn't returned." +
                   (string.IsNullOrWhiteSpace(_watchPartial)
                        ? " It has produced no output yet."
                        : $" Output so far:\n{_watchPartial}");
        }
        return ctx;
    }

    /// <summary>Aspect-4 scope: the live working directory these tools act against, so two pinned terminals
    /// at different folders don't collapse first-wins in the conversation hub. Null before the shell reports
    /// a path (nothing to disambiguate yet).</summary>
    public string? GetSecurityContext() => string.IsNullOrEmpty(CurrentPath) ? null : CurrentPath;

    /// <summary>The folder the shell works in, with everything under it.</summary>
    public ContextGrant GetContextGrant() => ContextGrant.OfFolder(CurrentPath);

    public string? GetAiSystemPromptGuidance() =>
        $"You are attached to a live terminal at '{Dialect.DescribeFolder(CurrentPath)}' running {Dialect.Description}. You can run " +
        "commands directly with the approval-gated `run_command` tool and you receive the actual output " +
        "back, so inspect results and keep going. To suggest a command for the user to run themselves " +
        "instead of running it, emit a `client_prefill` block whose text is the command prefixed with `>` " +
        "(e.g. `>git status`) — it lands in the input bar, not the terminal, so they can edit it and press " +
        "Enter to run it. Keep replies short; they appear in a small banner under the console.";

    public virtual IReadOnlyList<IClientTool> GetClientTools()
    {
        var tools = new List<IClientTool>
        {
            // Lets the model carry out what was asked of it in the AI bar by running real commands in the live shell.
            // Approval-gated.
            new DelegateClientTool(
                "run_command",
                $"Runs a command line in this terminal's running shell: {Dialect.Description}. It is the user's " +
                $"live session, currently at '{Dialect.DescribeFolder(CurrentPath)}'. The command runs there and you receive its " +
                "output back. A command that doesn't finish quickly is left running in the background and " +
                "you are told nothing further until it completes — don't wait on it.",
                [new ClientToolParameter("command", "The exact command line to run.")],
                ToolSafety.RequiresApproval,
                async (arguments, ct) =>
                {
                    var cmd = arguments["command"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(cmd))
                        return ToolResult.Error("No command provided.");
                    return await RunCommandCaptureAsync(cmd!, ct);
                }),
        };

        // The AI works only with the environment this terminal was launched in — it has no tool to
        // switch environments (that's the user's choice at launch via the "Cmd here" picker).
        return tools;
    }

    public IContext? GetContextObject()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return null;
        return new FileSystemContext
        {
            RootPath      = CurrentPath,
            CurrentPath   = CurrentPath,
            SelectedItems = []
        };
    }

    // ── IChatEngagement (inline AI banner host) ───────────────────────────

    private System.Windows.Controls.ContentControl? _engagementHost;
    private IAIResponseHandler? _bannerHandler;   // set by the shell when it wires the inline banner

    /// <summary>Called once by the View to hand the VM the banner host (bottom of the console column).</summary>
    public void SetEngagementHost(System.Windows.Controls.ContentControl host) => _engagementHost = host;

    public System.Windows.Controls.ContentControl GetHostUserControl()
        => _engagementHost ??= new System.Windows.Controls.ContentControl();

    public int? MaxResponseRows => null;

    public event Action? EngagementInvalidated;

    public void SetResponseHandler(IAIResponseHandler handler) => _bannerHandler = handler;

    // ── AI command capture + background watcher ───────────────────────────
    //
    // run_command sends a command then waits only for the display to *settle* (a clean prompt, or output
    // going quiet) — never for the command to actually finish. It then returns the plain output, the VT
    // screen, or a classification; a command that hasn't settled to a prompt is handed to a background
    // watcher that reports back to the AI when it eventually completes.

    private const int StabilizeQuietMs     = 2500;   // settle once output pauses this long
    private const int InitialCallTimeoutMs = 8000;   // bound only the run_command call, never the command
    private const int OutputCapLines       = 120;

    /// <summary>First back-off interval (ms) for the watcher; doubles up to <see cref="WatcherMaxMs"/>.</summary>
    protected virtual int WatcherInitialMs => 5000;
    /// <summary>Cap (ms) on the watcher's back-off interval.</summary>
    protected virtual int WatcherMaxMs     => 300000;

    private bool _capturing;
    private int  _captureMark;

    /// <summary>The rows above the cursor as the captured command began: an earlier command's, dropped from its output.</summary>
    private IReadOnlyList<string> _captureBefore = [];
    private TaskCompletionSource<bool>? _captureTcs;
    private Timer? _quietTimer;

    private CancellationTokenSource? _watcherCts;
    private string?  _watchCommand;
    private DateTime _watchStart;
    private string?  _watchPartial;

    /// <summary>Cancelled on Dispose; backs background-completion report turns so they outlive the watcher
    /// (a run_command the AI issues during a report must not cancel the report via the watcher token).</summary>
    private readonly CancellationTokenSource _disposeCts = new();

    private enum CommandKind { StillFinishing, LongRunning, WaitingForInput }

    // Plain is what was printed, scrollback included, with the terminal's escapes already worked out; Screen is for a
    // program drawing on the alternate screen. An escape in the output says neither: a pseudo-console always sends them.
    private sealed record CaptureSnapshot(string Plain, string Screen, bool AltScreen, bool AtPrompt);

    /// <summary>
    /// Runs <paramref name="command"/> and returns a result for the AI: the plain output (a clean,
    /// non-VT command), the terminal screen (a VT / full-screen app), a classification (a process that
    /// won't return, or is waiting for input), or — when there's nothing to say yet — an "end silently"
    /// result while a background watcher takes over and reports when it finishes.
    /// </summary>
    public async Task<ToolResult> RunCommandCaptureAsync(string command, CancellationToken ct)
    {
        // A new command supersedes any command still being watched (one capture/watch at a time — the
        // shared capture state and mark belong to this run).
        StopWatcher();

        // Arm + send on the UI thread; the settle edge arrives via _captureTcs (prompt) / the quiet timer.
        await _shell.RunOnUiAsync(() =>
        {
            _captureMark    = Scrollback.Count;
            _captureBefore  = _screen.Snapshot().Take(_screen.CursorRow).ToList();
            _captureTcs     = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _capturing      = true;
            ArmQuietTimer();
            SendCommand(command);
        });

        bool promptReturned;
        try
        {
            var settle  = _captureTcs!.Task;
            var done    = await Task.WhenAny(settle, Task.Delay(InitialCallTimeoutMs, ct));
            promptReturned = done == settle && settle.Result;
        }
        catch (OperationCanceledException) { promptReturned = false; }
        finally
        {
            await _shell.RunOnUiAsync(() => { _capturing = false; StopQuietTimer(); });
        }

        var snap = await ReadCaptureAsync();

        // Finished (a prompt returned): all it printed, or the screen for a program that draws on the alternate one.
        if (promptReturned)
            return !snap.AltScreen
                ? ToolResult.Ok($"ran: {command}", FormatPlain(command, snap.Plain))
                : ToolResult.Ok($"ran: {command}", FormatScreen(command, snap.Screen));

        // No prompt yet. Nothing on screen → hand straight to the watcher, say nothing.
        var output = snap.Plain.Length > 0 ? snap.Plain : snap.Screen;
        if (string.IsNullOrWhiteSpace(output))
        {
            StartWatcher(command, null);
            return ToolResult.EndSilently($"{command}: running, no output yet");
        }

        // Some output but no prompt → classify what kind of process this is.
        switch (await ClassifyAsync(command, output, ct))
        {
            case CommandKind.WaitingForInput:
                return ToolResult.Ok($"{command}: waiting for input",
                    $"`{command}` is waiting for input. Current screen:\n\n{Fence(output)}\n\n" +
                    "Decide what to send and call run_command with that input, or ask the user.");
            case CommandKind.LongRunning:
                return ToolResult.Ok($"{command}: long-running",
                    $"`{command}` is a long-running process that won't return to a prompt (e.g. a server). " +
                    $"Output so far:\n\n{Fence(output)}\n\nLet the user know it's running.");
            default:   // StillFinishing → watch silently
                StartWatcher(command, output);
                return ToolResult.EndSilently($"{command}: still running");
        }
    }

    private Task<CaptureSnapshot> ReadCaptureAsync()
        => _shell.RunOnUiAsync(() =>
        {
            var screenLines = _screen.Snapshot();
            var screen      = Cap(string.Join("\n", screenLines).TrimEnd());

            var plainLines = new List<string>();
            for (int i = _captureMark; i < Scrollback.Count; i++) plainLines.Add(Scrollback[i]);
            plainLines.AddRange(AbovePrompt(screenLines));

            // What was on screen already heads the capture unless the command cleared it; only an exact match is dropped.
            int earlier = 0;
            while (earlier < _captureBefore.Count && earlier < plainLines.Count
                   && plainLines[earlier].TrimEnd() == _captureBefore[earlier].TrimEnd())
                earlier++;
            if (earlier == _captureBefore.Count) plainLines.RemoveRange(0, earlier);
            // Drop the trailing fresh prompt / blank lines so plain output is just the command's output.
            while (plainLines.Count > 0 &&
                   (plainLines[^1].Trim().Length == 0
                    || (!_marksPrompts && Dialect.ReadPrompt(plainLines[^1]) is { IsBare: true })))
                plainLines.RemoveAt(plainLines.Count - 1);

            return Task.FromResult(new CaptureSnapshot(
                Cap(string.Join("\n", plainLines).TrimEnd()), screen,
                _screen.IsAltScreen, _atPrompt));
        });

    /// <summary>The screen's rows above the prompt a marking shell is at — the row the prompt starts on cut where it starts;
    /// for any other shell the whole screen, its trailing prompt left to the caller.</summary>
    private IReadOnlyList<string> AbovePrompt(IReadOnlyList<string> rows)
    {
        if (!(_marksPrompts && _atPrompt && _promptStart is { } start)) return rows;
        int row = (int)(start.Line - _screen.TopLine);
        if (row < 0) return [];
        if (row >= rows.Count) return rows;

        var above = rows.Take(row).ToList();
        if (start.Column > 0) above.Add(rows[row][..Math.Min(start.Column, rows[row].Length)]);
        return above;
    }

    private async Task<CommandKind> ClassifyAsync(string command, string output, CancellationToken ct)
    {
        try
        {
            var idx = await _ai.DisambiguateOptionAsync(
                $"In a terminal, `{command}` was run and produced this output but has not returned to a prompt:\n{output}",
                "Which best describes the command's current state?",
                [
                    ("Still finishing",   "A normal command still doing work; it will return to a prompt on its own."),
                    ("Long-running",      "A service or daemon (e.g. a web server) that keeps running until killed and won't return to a prompt."),
                    ("Waiting for input", "It is blocked, prompting the user for input."),
                ], ct);
            return idx switch
            {
                1 => CommandKind.LongRunning,
                2 => CommandKind.WaitingForInput,
                _ => CommandKind.StillFinishing,
            };
        }
        catch (OperationCanceledException) { throw; }
        catch { return CommandKind.StillFinishing; }
    }

    // ── Background watcher ─────────────────────────────────────────────────

    private void StartWatcher(string command, string? partial)
    {
        StopWatcher();
        _watchCommand = command;
        _watchStart   = DateTime.Now;
        _watchPartial = partial;
        _watcherCts   = new CancellationTokenSource();
        _ = WatchAsync(command, _watcherCts.Token);
    }

    private async Task WatchAsync(string command, CancellationToken ct)
    {
        var interval        = Math.Max(1000, WatcherInitialMs);
        var cap             = Math.Max(WatcherInitialMs, WatcherMaxMs);
        var lastSig         = string.Empty;
        var reportedWaiting = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(interval, ct);

                var snap = await ReadCaptureAsync();
                _watchPartial = Pick(snap);

                if (snap.AtPrompt) { await ReportCompletionAsync(command, snap); return; }   // finished

                if (Signature(snap) == lastSig) { interval = Math.Min(interval * 2, cap); continue; }   // quiet → back off

                // New output arrived: let the burst settle, then re-classify what kind of process this now is.
                await Task.Delay(StabilizeQuietMs, ct);
                var settled = await ReadCaptureAsync();
                _watchPartial = Pick(settled);
                lastSig       = Signature(settled);
                if (settled.AtPrompt) { await ReportCompletionAsync(command, settled); return; }

                var output = Pick(settled);
                var kind   = string.IsNullOrWhiteSpace(output)
                    ? CommandKind.StillFinishing
                    : await ClassifyAsync(command, output, ct);

                if (kind == CommandKind.WaitingForInput)
                {
                    if (!reportedWaiting) { reportedWaiting = true; await ReportWaitingAsync(command, output); }
                }
                else reportedWaiting = false;   // state moved on — a later wait can be reported again

                interval = Math.Max(1000, WatcherInitialMs);   // there's activity — check promptly again
            }
        }
        catch (OperationCanceledException) { /* the user (or a newer command) took over */ }
        finally { ClearWatch(); }
    }

    private static string Pick(CaptureSnapshot s)      => s.Plain.Length > 0 ? s.Plain : s.Screen;
    private static string Signature(CaptureSnapshot s) => $"{s.Plain}{s.Screen}{s.AtPrompt}";

    /// <summary>Cancels any background watch (the user typed / Ctrl-C'd, or a newer command superseded it).</summary>
    private void StopWatcher()
    {
        _watcherCts?.Cancel();
        _watcherCts = null;
        ClearWatch();
    }

    private void ClearWatch()
    {
        _watchCommand = null;
        _watchPartial = null;
    }

    private Task ReportCompletionAsync(string command, CaptureSnapshot snap)
    {
        var elapsed = DescribeElapsed(DateTime.Now - _watchStart);
        var output  = snap.AltScreen ? snap.Screen : snap.Plain;
        ClearWatch();
        return ReportToAiAsync(
            $"The command `{command}` you ran earlier has now finished (after {elapsed}). Its output:\n\n" +
            $"{Fence(output)}\n\nBriefly tell the user the result.");
    }

    private Task ReportWaitingAsync(string command, string output)
        => ReportToAiAsync(
            $"The command `{command}` you ran is now waiting for input. Current screen:\n\n{Fence(output)}\n\n" +
            "Decide what to send and call run_command with that input, or ask the user.");

    /// <summary>
    /// Runs a continuation turn against the page's own banner handler to deliver a background-command
    /// update, then — if the page isn't the active tab — posts a notification with an "Open" link. Uses the
    /// VM-lifetime token (not the watcher's) so a run_command the AI issues here can't cancel itself.
    /// </summary>
    private async Task ReportToAiAsync(string note)
    {
        var handler = _bannerHandler;
        if (handler is null) return;

        try { await _ai.RunAgentAsync(this, note, includeContext: true, handler, _disposeCts.Token); }
        catch (OperationCanceledException) { return; }
        catch { return; }

        var active = await _shell.RunOnUiAsync(() => Task.FromResult(Tab?.IsActive ?? false));
        if (!active && Tab is { } tab)
            await _shell.RunOnUiAsync(() =>
                _shell.ShowNotification(Str.Get("Terminal.Console.NeedsAttention"), tab));
    }

    // ── Capture helpers ────────────────────────────────────────────────────

    private void ArmQuietTimer()
    {
        _quietTimer ??= new Timer(_ => _captureTcs?.TrySetResult(false));
        _quietTimer.Change(StabilizeQuietMs, Timeout.Infinite);
    }

    private void ResetQuietTimer() => _quietTimer?.Change(StabilizeQuietMs, Timeout.Infinite);
    private void StopQuietTimer()  => _quietTimer?.Change(Timeout.Infinite, Timeout.Infinite);

    private static string Fence(string body) => "```\n" + body + "\n```";

    private static string FormatPlain(string command, string output)
        => string.IsNullOrWhiteSpace(output)
            ? $"`{command}` produced no output."
            : $"Output of `{command}`:\n\n{Fence(output)}";

    private static string FormatScreen(string command, string screen)
        => $"`{command}` is a full-screen / interactive program. Current terminal screen:\n\n{Fence(screen)}";

    private static string Cap(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length <= OutputCapLines) return text;
        var head = string.Join("\n", lines.Take(OutputCapLines / 2));
        var tail = string.Join("\n", lines.Skip(lines.Length - OutputCapLines / 2));
        return $"{head}\n… ({lines.Length - OutputCapLines} lines elided) …\n{tail}";
    }

    private static string DescribeElapsed(TimeSpan t)
        => t.TotalSeconds < 60 ? $"{(int)t.TotalSeconds}s"
         : t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes}m {t.Seconds}s"
         : $"{(int)t.TotalHours}h {t.Minutes}m";

    // ── IDisposable ───────────────────────────────────────────────────────

    private bool _disposed;

    public void Dispose()
    {
        ITerminalTransport? transport;
        lock (_transportGate)
        {
            if (_disposed) return;
            _disposed = true;
            transport = _transport;
            _transport = null;
        }
        StopWatcher();
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        _quietTimer?.Dispose();
        if (transport is null) return;
        transport.Output -= OnTransportOutput;
        transport.Failed -= OnTerminalError;
        transport.Exited -= OnProcessExited;
        transport.Dispose();
    }
}
