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
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
public abstract partial class TerminalViewModel : ObservableObject, IDisposable, IPageViewModel
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

    protected TerminalViewModel(PseudoConsoleHostService pty, IShellServices shell,
                                int cols = 220, int rows = 50)
    {
        _shell  = shell;
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
        if (!string.IsNullOrEmpty(text)) _pty.WriteRaw(text);
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

        // A bare shell prompt on the cursor row means the previous command finished. Handle it once per
        // prompt (not on every chunk, and not while the user is mid-typing — the row then ends with the
        // typed text, so PeekPromptLine returns null and _atPrompt simply stays set).
        if (_screen.PeekPromptLine() is { } prompt)
        {
            if (!_atPrompt)
            {
                _atPrompt = true;
                IsBusy    = false;

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

    public string GetContext() => $"Terminal: '{CurrentPath}'.";

    public virtual IReadOnlyList<IClientTool> GetClientTools()
    {
        var tools = new List<IClientTool>
        {
            // Lets the model carry out a natural-language request (routed here when the user's typed line
            // isn't a recognised command) by running real commands in the live shell. Approval-gated.
            new DelegateClientTool(
                "run_command",
                $"Runs a command line in this terminal's running shell: {ShellDescription}. It is the user's " +
                $"live session, currently at '{CurrentPath}'; the command executes there and its output " +
                $"appears in the terminal.",
                [new ClientToolParameter("command", "The exact command line to run.")],
                ToolSafety.RequiresApproval,
                (arguments, ct) =>
                {
                    var cmd = arguments["command"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(cmd))
                        return Task.FromResult(ToolResult.Error("No command provided."));
                    SendCommand(cmd);
                    return Task.FromResult(ToolResult.Ok($"ran: {cmd}", $"Ran `{cmd}` in the terminal."));
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
