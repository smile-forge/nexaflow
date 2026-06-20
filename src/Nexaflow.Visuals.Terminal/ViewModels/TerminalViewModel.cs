using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Terminal;
using Nexaflow.Visuals.Terminal.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace Nexaflow.Visuals.Terminal.ViewModels;

/// <summary>
/// Reusable terminal page view-model: owns one <see cref="PseudoConsoleHostService"/> whose shell stays
/// alive for the tab's lifetime, renders its output, and tracks the working directory, environment
/// snapshot and command history. Shell-specific bits (which executable, how to phrase <c>cd</c>/<c>set</c>)
/// and the environment source/persistence are supplied by a feature-specific subclass — so cmd and a
/// future PowerShell feature share everything here without referencing each other.
/// </summary>
public abstract partial class TerminalViewModel : ObservableObject, IDisposable, IPageViewModel, IChatEngagement
{
    // ── Terminal back-end ─────────────────────────────────────────────────
    protected readonly PseudoConsoleHostService _pty;

    // VT screen buffer: raw PTY bytes are rendered into a cols×rows grid;
    // the visible screen + scrollback are read back from it after each chunk.
    private readonly TerminalScreen _screen;
    private readonly int _cols;
    private readonly int _rows;

    // True while the cursor row is a bare shell prompt (vs. a command running / output flowing). Drives
    // the init sequence (fire once per prompt) and gates the Enter command/query routing.
    private bool _atPrompt;

    // ── Environment / tab ─────────────────────────────────────────────────
    protected readonly IShellServices _shell;
    protected readonly IAIService     _ai;
    protected TerminalEnvironment?    _activeEnv;

    /// <summary>Set by the page registration so the VM can update its own page title/breadcrumbs.</summary>
    public Page? Tab { get; set; }

    // First-prompt init state machine: 0 = waiting for first prompt,
    // 1 = cd sent, waiting for next prompt, 2 = normal operation.
    private int     _initPhase;
    private string? _pendingInitPath;
    private string? _pendingInitCmd;

    // ── Observable state ──────────────────────────────────────────────────

    /// <summary>Lines that have scrolled off the top of the screen — the terminal scrollback.</summary>
    public ObservableCollection<string> Scrollback { get; } = [];

    /// <summary>The live terminal screen (the visible VT grid), rendered as monospace text and rebuilt
    /// on each chunk. Together with <see cref="Scrollback"/> this is the full terminal display.</summary>
    [ObservableProperty] private string _screenText = string.Empty;

    /// <summary>Current path shown in the top bar — updated by parsing shell prompts.</summary>
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

    /// <summary>How this shell changes directory (cmd: <c>cd /d "…"</c>).</summary>
    protected virtual string FormatCdCommand(string path) => $"cd /d \"{path}\"";

    /// <summary>How this shell sets a variable for the live session (cmd: <c>set "NAME=value"</c>).</summary>
    protected virtual string FormatSetCommand(string name, string value) => $"set \"{name}={value}\"";

    /// <summary>Human description of the shell, so the AI's run_command tool uses the right syntax.</summary>
    protected virtual string ShellDescription =>
        "the Windows cmd.exe command prompt (use cmd / batch syntax such as `dir`, not PowerShell cmdlets)";

    // ── Construction / startup ────────────────────────────────────────────

    protected TerminalViewModel(PseudoConsoleHostService pty, IShellServices shell, IAIService ai,
                                int cols = 220, int rows = 50)
    {
        _shell  = shell;
        _ai     = ai;
        _cols   = cols;
        _rows   = rows;
        _screen = new TerminalScreen(cols, rows);
        _pty    = pty;
        _pty.OutputReceived += OnPtyOutput;
        _pty.TerminalError  += OnTerminalError;
        _pty.ProcessExited  += OnProcessExited;

        _envView = CollectionViewSource.GetDefaultView(EnvVars);
        _envView.Filter = o => MatchesEnvFilter(o as EnvVar);

        // The PTY is started in SetupInitialState — after the active environment's start directory and
        // variable overrides are known, so they can be applied at process creation.
    }

    /// <summary>
    /// Resolves the active environment, applies its start directory + variable overrides to the PTY, and
    /// starts the shell. Call once from the subclass constructor after its environment source is set.
    /// </summary>
    protected void SetupInitialState(TerminalEnvironment? env, string? initialPath, bool pickerPending = false)
    {
        // When a launch picker is pending, start with no active environment (and no initial command) —
        // the chosen environment is applied to the running shell once the user picks.
        _activeEnv = pickerPending ? null : (env ?? DefaultEnv());

        // Start the shell directly in the target folder (lpCurrentDirectory) and overlay the
        // environment's variable overrides at process creation — no visible `cd` entry on open.
        _pty.StartDirectory = initialPath ?? _activeEnv?.StartDirectory;
        if (_activeEnv?.EnvOverrides is { Count: > 0 } overrides)
            _pty.EnvironmentOverrides = overrides;

        // Only the initial command is deferred to the first prompt; the start dir is already applied.
        _pendingInitPath = null;
        _pendingInitCmd  = _activeEnv?.InitialCommand;
        _initPhase       = _pendingInitCmd is not null ? 0 : 2;

        _pty.Start((short)_cols, (short)_rows);
        RefreshEnvVars();
        RefreshFiles();
    }

    private TerminalEnvironment? DefaultEnv()
        => Environments.FirstOrDefault(e => e.IsDefault) ?? Environments.FirstOrDefault();

    protected TerminalEnvironment? FindEnvByName(string name)
        => Environments.FirstOrDefault(e =>
               string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    // ── Public API called by the shell ────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="command"/> in the shell as if it had been typed: sends the text plus a carriage
    /// return and records it in history. Used by the init sequence, the AI bar, history re-run, env switches
    /// and <c>set</c> writes — the user types directly into the terminal instead (see <see cref="SendText"/>).
    /// </summary>
    public void SendCommand(string command)
    {
        var trimmed = command.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        RecordHistory(trimmed);
        IsBusy   = true;
        _atPrompt = false;
        _pty.WriteRaw(trimmed + "\r");
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
        _pty.SendCtrlC();
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
        _pty.WriteRaw(text);
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
        _pty.WriteRaw(seq);
        return true;
    }

    /// <summary>
    /// Handles Enter. At a shell prompt the typed line is classified: a recognised command runs (and is
    /// recorded in history); natural language is wiped off the shell line and handed to the LLM. While a
    /// program is running (the cursor row isn't a prompt) Enter is simply forwarded so interactive input works.
    /// </summary>
    public void HandleEnter()
    {
        // Pressing Enter is a user action — dissolve any lingering AI banner. (A lone Enter does NOT stop
        // a background-command watch; the watcher only yields to real typing or Ctrl-C.)
        EngagementInvalidated?.Invoke();

        var input = _atPrompt ? CurrentInputLine() : null;
        if (input is null)
        {
            _pty.WriteRaw("\r");                 // interactive program reading input
            return;
        }

        if (CommandClassifier.IsCommand(input, ShellBuiltins))
        {
            var trimmed = input.Trim();
            if (trimmed.Length > 0) RecordHistory(trimmed);
            IsBusy   = true;
            _atPrompt = false;
            _pty.WriteRaw("\r");
            return;
        }

        // Natural language → clear what was typed off the shell line, then ask the model.
        if (input.Length > 0) _pty.WriteRaw("\x1b");   // Esc clears cmd's current input line
        _shell.SubmitAiQuery(input.Trim());
    }

    /// <summary>The text typed after the prompt on the cursor row, or null if that row isn't a prompt.</summary>
    private string? CurrentInputLine()
    {
        var snap = _screen.Snapshot();
        var row  = (snap.ElementAtOrDefault(_screen.CursorRow) ?? string.Empty).TrimEnd();
        var m    = PromptPathRegex.Match(row);
        return m.Success && m.Index == 0 ? row[m.Length..] : row;
    }

    /// <summary>The shell's built-in command names, used to classify Enter'd lines (cmd by default).</summary>
    protected virtual IReadOnlySet<string> ShellBuiltins => CommandClassifier.CmdBuiltins;

    /// <summary>
    /// True if <paramref name="input"/> is a recognised shell command (vs. natural language) for this
    /// terminal. The chat-bar query handler uses it to route a typed line to the shell while letting
    /// natural language reach the AI; it wraps the same <see cref="CommandClassifier"/> heuristic Enter
    /// uses, keeping <see cref="ShellBuiltins"/> encapsulated. Blank input is not a command here.
    /// </summary>
    public bool IsConsoleCommand(string input)
        => !string.IsNullOrWhiteSpace(input) && CommandClassifier.IsCommand(input, ShellBuiltins);

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

    private void OnPtyOutput(string rawChunk)
        => _ = _shell.RunOnUiAsync(() => ProcessScreenOutput(rawChunk));

    private void ProcessScreenOutput(string rawChunk)
    {
        _screen.Feed(rawChunk);

        // Lines that scrolled off the top become scrollback; the visible grid is the live screen.
        foreach (var line in _screen.TakeScrollback())
            Scrollback.Add(line.TrimEnd());
        ScreenText = string.Join("\n", _screen.Snapshot()).TrimEnd('\n');

        // While capturing a command's output: note any VT/ANSI escape (→ return the screen, not plain
        // text) and reset the quiet-debounce, so the capture settles once output stops.
        if (_capturing)
        {
            if (rawChunk.IndexOf('\x1b') >= 0) _captureSawAnsi = true;
            ResetQuietTimer();
        }

        // A bare shell prompt on the cursor row means the previous command finished. Handle it once per
        // prompt (not on every chunk, and not while the user is mid-typing — the row then ends with the
        // typed text, so PeekPromptLine returns null and _atPrompt simply stays set).
        if (_screen.PeekPromptLine() is { } prompt)
        {
            if (!_atPrompt)
            {
                _atPrompt = true;
                IsBusy    = false;
                _captureTcs?.TrySetResult(true);   // a capture in flight: the command finished (clean prompt)

                if (ExtractPathFromPrompt(prompt) is { } path)
                {
                    bool pathChanged = !string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase);
                    CurrentPath = path;
                    RefreshEnvVars();
                    if (pathChanged) RefreshFiles();   // re-enumerate only when the directory changes
                    HandlePromptDetected();
                    SyncTabMeta();
                }
            }
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

    // ── Init state machine ────────────────────────────────────────────────
    //
    // Called each time a shell prompt is detected.  Drives the three phases:
    //   0 — awaiting the very first prompt; send cd if a path was requested
    //   1 — awaiting the post-cd prompt; send InitialCommand if defined
    //   2 — normal operation; nothing to do

    private void HandlePromptDetected()
    {
        if (_initPhase == 0)
        {
            if (_pendingInitPath is not null)
            {
                var path = _pendingInitPath;
                _pendingInitPath = null;
                _initPhase = 1;
                SendCommand(FormatCdCommand(path));
            }
            else
            {
                if (_pendingInitCmd is not null)
                {
                    var cmd = _pendingInitCmd;
                    _pendingInitCmd = null;
                    _initPhase = 2;
                    SendCommand(cmd);
                }
                else _initPhase = 2;
            }
        }
        else if (_initPhase == 1)
        {
            if (_pendingInitCmd is not null)
            {
                var cmd = _pendingInitCmd;
                _pendingInitCmd = null;
                _initPhase = 2;
                SendCommand(cmd);
            }
            else _initPhase = 2;
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

    protected static bool GlobMatchPath(string path, string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || pattern is "*" or "**") return true;
        if (pattern.EndsWith("\\*") || pattern.EndsWith("/*"))
            return path.StartsWith(pattern[..^2], StringComparison.OrdinalIgnoreCase);
        return string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase);
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
            && !string.Equals(newPath, CurrentPath, StringComparison.OrdinalIgnoreCase);

        if (pathChanged)
        {
            _initPhase = _pendingInitCmd is not null ? 1 : 2;
            SendCommand(FormatCdCommand(newPath!));
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
        // No choice made — fall back to the default environment.
        if (DefaultEnv() is { } def) ApplyPickedEnvironment(def);
    }

    private void ApplyPickedEnvironment(TerminalEnvironment env)
    {
        _activeEnv = env;
        SyncTabMeta();

        // The shell is already running, so overrides go in via live `set` rather than the process block.
        foreach (var kv in env.EnvOverrides)
            SendCommand(FormatSetCommand(kv.Key, kv.Value));

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
                SendCommand(FormatSetCommand(name, value));
                return true;

            case EnvScope.User:
                try
                {
                    Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
                    return true;
                }
                catch (Exception ex)
                {
                    _shell.ShowError($"Could not set '{name}' for the user: {ex.Message}");
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
        if (string.IsNullOrEmpty(CurrentPath) || !Directory.Exists(CurrentPath)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(CurrentPath)
                                          .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                Files.Add(new TerminalFsEntry(file, isDirectory: false));
            foreach (var dir in Directory.EnumerateDirectories(CurrentPath)
                                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                Files.Add(new TerminalFsEntry(dir, isDirectory: true));
        }
        catch { /* access denied / transient — leave whatever was enumerated */ }
    }

    /// <summary>Navigates the shell into a folder from the Files panel (double-click).</summary>
    public void NavigateInto(TerminalFsEntry entry)
    {
        if (entry.IsDirectory) SendCommand(FormatCdCommand(entry.FullPath));
    }

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext()
    {
        var ctx = $"Terminal: '{CurrentPath}'.";

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

    public string? GetAiSystemPromptGuidance() =>
        $"You are attached to a live terminal at '{CurrentPath}' running {ShellDescription}. You can run " +
        "commands directly with the approval-gated `run_command` tool and you receive the actual output " +
        "back, so inspect results and keep going. To suggest a command for the user to run themselves " +
        "instead of running it, emit a `client_prefill` block whose text is the command prefixed with `>` " +
        "(e.g. `>git status`) — it lands in the input bar, not the terminal, so they can edit it and press " +
        "Enter to run it. Keep replies short; they appear in a small banner under the console.";

    public virtual IReadOnlyList<IClientTool> GetClientTools()
    {
        var tools = new List<IClientTool>
        {
            // Lets the model carry out a natural-language request (routed here when the user's typed line
            // isn't a recognised command) by running real commands in the live shell. Approval-gated.
            new DelegateClientTool(
                "run_command",
                $"Runs a command line in this terminal's running shell: {ShellDescription}. It is the user's " +
                $"live session, currently at '{CurrentPath}'. The command runs there and you receive its " +
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

        var envs = Environments
            .Where(e => GlobMatchPath(CurrentPath, e.LocationFilter))
            .ToList();
        if (envs.Count == 0) return tools;

        var envList = string.Join("; ", envs.Select(e =>
            $"'{e.Name}'" + (string.IsNullOrEmpty(e.InitialCommand) ? "" : $" (runs: {e.InitialCommand})")));

        tools.Add(new DelegateClientTool(
            "set_environment",
            $"Switches the terminal to a configured environment. Provide 'name' matching one of: {envList}. " +
            "The environment's initial command is sent and the tab title updates.",
            [new ClientToolParameter("name", "Name of the environment to switch to.")],
            ToolSafety.RequiresApproval,
            (arguments, ct) =>
            {
                var name = arguments["name"]?.GetValue<string>();
                if (string.IsNullOrEmpty(name))
                    return Task.FromResult(ToolResult.Error("No environment name provided."));

                var env = FindEnvByName(name);
                if (env is null)
                    return Task.FromResult(ToolResult.Error($"Unknown environment '{name}'."));

                _activeEnv = env;
                SyncTabMeta();

                if (!string.IsNullOrEmpty(env.InitialCommand))
                {
                    if (!IsBusy)
                        SendCommand(env.InitialCommand);
                    else
                        _pendingInitCmd = env.InitialCommand;
                }

                return Task.FromResult(ToolResult.Ok($"switched to {env.Name}", $"Switched the terminal to '{env.Name}'."));
            }));

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
    private bool _captureSawAnsi;
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

    private sealed record CaptureSnapshot(string Plain, string Screen, bool SawAnsi, bool AltScreen, bool AtPrompt);

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
            _captureSawAnsi = false;
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

        // Finished (a clean prompt returned): plain text for a clean command, the screen for VT output.
        if (promptReturned)
            return (!snap.SawAnsi && !snap.AltScreen)
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
            plainLines.AddRange(screenLines);
            // Drop the trailing fresh prompt / blank lines so plain output is just the command's output.
            while (plainLines.Count > 0 &&
                   (plainLines[^1].Trim().Length == 0 || plainLines[^1].TrimEnd().EndsWith('>')))
                plainLines.RemoveAt(plainLines.Count - 1);

            return Task.FromResult(new CaptureSnapshot(
                Cap(string.Join("\n", plainLines).TrimEnd()), screen,
                _captureSawAnsi, _screen.IsAltScreen, _atPrompt));
        });

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
        var output  = (!snap.SawAnsi && !snap.AltScreen) ? snap.Plain : snap.Screen;
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
                _shell.ShowNotification("Console: a background command needs your attention — open the tab to see it.", tab));
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
        if (_disposed) return;
        _disposed = true;
        StopWatcher();
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        _quietTimer?.Dispose();
        _pty.OutputReceived -= OnPtyOutput;
        _pty.TerminalError  -= OnTerminalError;
        _pty.ProcessExited  -= OnProcessExited;
        _pty.Dispose();
    }
}
