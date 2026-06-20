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
    // correct line text is read back from that grid after each chunk.
    private readonly TerminalScreen _screen;
    private readonly int _cols;
    private readonly int _rows;

    // The entry currently receiving output (null = shell startup / idle)
    private ConsoleEntry? _activeEntry;

    // The echo of the command we just sent — suppress it from output
    private string? _pendingEcho;

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

    /// <summary>All entries (commands + their output) shown in the centre panel.</summary>
    public ObservableCollection<ConsoleEntry> Entries { get; } = [];

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

    /// <summary>
    /// Live echo of the command being typed in the AI bar (console mode), shown as a faux prompt row so
    /// you see what's about to run. Null = nothing being typed. Set via <see cref="SetInputPreview"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private string? _previewLine;

    /// <summary>True while a typed command is being previewed (drives the faux-prompt row's visibility).</summary>
    public bool HasPreview => !string.IsNullOrEmpty(PreviewLine);

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
        _historyCursor = -1;   // a fresh command resets Up/Down navigation

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

    // ── Chat-bar participation (key handling + live preview) ──────────────

    /// <summary>Mirrors the in-progress console command (without the <c>&gt;</c> prefix) at a faux prompt.</summary>
    public void SetInputPreview(string? draft)
        => PreviewLine = string.IsNullOrWhiteSpace(draft) ? null : draft;

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

        // Drain all fully-committed lines from the screen buffer.
        foreach (var line in _screen.TakeLines())
            ProcessLine(line);

        // Check whether the cursor row now holds a shell prompt.
        var prompt = _screen.PeekPromptLine();
        if (prompt is not null)
        {
            // ConPTY positions command output above the next prompt with cursor moves, not line feeds,
            // so short output never LF-commits. Flush those stranded rows now that the command is done.
            foreach (var line in _screen.DrainAboveCursor())
                ProcessLine(line);

            var path = ExtractPathFromPrompt(prompt);
            if (path is not null)
            {
                bool pathChanged = !string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase);
                CurrentPath = path;
                RefreshEnvVars();
                // The Files list only needs rebuilding when the directory changes — re-enumerating a large
                // folder after every command would churn the UI thread.
                if (pathChanged) RefreshFiles();
                HandlePromptDetected();
                SyncTabMeta();
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
        _ = _shell.RunOnUiAsync(() =>
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
        _ = _shell.RunOnUiAsync(() =>
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
        var envs = Environments
            .Where(e => GlobMatchPath(CurrentPath, e.LocationFilter))
            .ToList();
        if (envs.Count == 0) return [];

        var envList = string.Join("; ", envs.Select(e =>
            $"'{e.Name}'" + (string.IsNullOrEmpty(e.InitialCommand) ? "" : $" (runs: {e.InitialCommand})")));

        return [new DelegateClientTool(
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
            })];
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
