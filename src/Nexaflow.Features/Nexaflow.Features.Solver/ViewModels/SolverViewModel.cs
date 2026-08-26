using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Solver.Palette;
using Nexaflow.Features.Solver.Solving;

namespace Nexaflow.Features.Solver.ViewModels;

/// <summary>
/// The Solver page: a definition, the chips that recognise it, and the answers they produce.
/// </summary>
public sealed partial class SolverViewModel : ObservableObject, IPageViewModel, IDisposable
{
    /// <summary>
    /// How long to wait after a keystroke before asking the solvers what they make of it. Long
    /// enough that typing a formula does not run the parser on every prefix, short enough that the
    /// chips feel like they are keeping up.
    /// </summary>
    private const int ChipDebounceMs = 200;

    /// <summary>Definition beyond this length is not offered to the solvers — it is a document, not a sum.</summary>
    private const int MaxDefinitionLength = 20_000;

    private readonly SolverConfig _config;
    private readonly IShellServices _shell;
    private readonly SolverRegistry _registry;

    /// <summary>
    /// One text buffer per tab, so switching editors never destroys what is in the other two.
    /// <para>
    /// Each buffer is bound to its own editor and to nothing else, which is what makes the tabs
    /// independent. Swapping one editor's text on the way past used to be how this worked, and it
    /// cannot be made to work: an editor that holds the keyboard refuses a document push (rebuilding
    /// mid-word would destroy what is being typed), so a switch made while the caret was in the
    /// editor left it showing the tab you had just left.
    /// </para>
    /// </summary>
    private readonly Dictionary<DefinitionMode, string> _buffers = new()
    {
        [DefinitionMode.Calc] = string.Empty,
        [DefinitionMode.Latex] = string.Empty,
        [DefinitionMode.Text] = string.Empty,
    };

    /// <summary>This page's AI scope boundary — see <see cref="GetSecurityContext"/>.</summary>
    private readonly string _scopeId = "solver:" + Guid.NewGuid().ToString("n")[..8];

    private CancellationTokenSource? _debounce;
    private bool _disposed;

    /// <summary>Builds the page.</summary>
    public SolverViewModel(SolverConfig config, IShellServices shell, IAIService ai)
    {
        _config = config;
        _shell = shell;
        _registry = SolverRegistry.CreateDefault(ai);

        _mode = config.GetStartMode();
        _angleUnit = config.GetAngleUnit();
        _isPaletteOpen = config.ShowPalette;
        _calcPage = CalcPalette.Main;
        RestoreRecent();
        RaiseNavigatorChanged();

        // The engine's first parse loads a generated grammar. Doing it here, off the UI thread,
        // means the cost lands while the tab is still being painted rather than under the first
        // keystroke.
        _ = Task.Run(ExpressionParser.Warmup);
    }

    /// <summary>Raised when a palette key should be applied — the view owns the caret and selection.</summary>
    public event Action<PaletteKey>? InsertRequested;

    /// <summary>Raised when the definition should be replaced wholesale (clear, or "use as definition").</summary>
    public event Action<string>? DefinitionReplaced;

    // ── Definition ──────────────────────────────────────────────────────────

    /// <summary>Which editor is showing.</summary>
    [ObservableProperty]
    private DefinitionMode _mode;

    /// <summary>
    /// The active editor's text - what gets solved, offered to the chips and shown to the AI.
    /// <para>
    /// A view onto whichever tab's buffer is showing rather than a store of its own, so setting it
    /// (clear, backspace, "use as definition", the AI) writes to the tab it belongs to and every
    /// downstream reader stays written against one property. The editors themselves bind to their
    /// own buffer, never to this.
    /// </para>
    /// </summary>
    public string DefinitionText
    {
        get => _buffers[Mode];
        set => Write(Mode, value);
    }

    /// <summary>The Calc tab's buffer, bound to its field.</summary>
    public string CalcText
    {
        get => _buffers[DefinitionMode.Calc];
        set => Write(DefinitionMode.Calc, value);
    }

    /// <summary>The Latex tab's buffer, bound to its editor.</summary>
    public string LatexText
    {
        get => _buffers[DefinitionMode.Latex];
        set => Write(DefinitionMode.Latex, value);
    }

    /// <summary>The Text tab's buffer, bound to its editor.</summary>
    public string TextText
    {
        get => _buffers[DefinitionMode.Text];
        set => Write(DefinitionMode.Text, value);
    }

    /// <summary>Stores one tab's text, and tells the world only what actually changed - a tab that is
    /// not showing has no bearing on what is emptied, offered or solved.</summary>
    private void Write(DefinitionMode mode, string value)
    {
        value ??= string.Empty;
        if (_buffers[mode] == value) return;

        _buffers[mode] = value;
        OnPropertyChanged(mode switch
        {
            DefinitionMode.Calc => nameof(CalcText),
            DefinitionMode.Latex => nameof(LatexText),
            _ => nameof(TextText),
        });

        if (mode != Mode) return;
        OnPropertyChanged(nameof(DefinitionText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasText));
        ScheduleChipRefresh();
    }

    /// <summary>How angles are read. Toggled from the palette.</summary>
    [ObservableProperty]
    private AngleUnit _angleUnit;

    /// <summary>
    /// The active mode as a string, because the tab rail selects by <c>Tag</c>.
    /// </summary>
    public string ModeName
    {
        get => Mode.ToString();
        set
        {
            if (Enum.TryParse<DefinitionMode>(value, ignoreCase: true, out var mode)) Mode = mode;
        }
    }

    /// <summary>
    /// Whether the definition area is a single formula rather than a document.
    /// <para>
    /// This is the whole of the Latex tab: the same in-place markdown editor, told that what it holds
    /// is one formula. It then owns the <c>$$</c> itself — puts it on to typeset, takes it off on the
    /// way out — so the definition here is always the formula and never a maths block, and the user
    /// never types or sees a fence. The Text tab leaves it alone and stays a full markdown editor,
    /// blocks, headings, diagrams and maths of its own included.
    /// </para>
    /// </summary>
    public bool IsSingleFormula => Mode == DefinitionMode.Latex;

    /// <summary>True when the definition area is empty, so the view can show its placeholder.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(DefinitionText);

    /// <summary>The inverse — there is something worth clearing.</summary>
    public bool HasText => !IsEmpty;

    /// <summary>Label for the DEG/RAD toggle.</summary>
    public string AngleLabel => AngleUnit == AngleUnit.Degrees ? "DEG" : "RAD";

    /// <summary>True while the Calc editor is showing.</summary>
    public bool IsCalcMode => Mode == DefinitionMode.Calc;

    /// <summary>True while the prose editor is showing.</summary>
    public bool IsTextMode => Mode == DefinitionMode.Text;

    /// <summary>True while the in-place editor is showing — Latex or Text.</summary>
    public bool IsMarkdownMode => Mode is DefinitionMode.Latex or DefinitionMode.Text;

    /// <summary>True while the LaTeX editor is showing, which is when the symbol palette applies.</summary>
    public bool IsLatexMode => Mode == DefinitionMode.Latex;

    /// <summary>
    /// Whether the editor shows the characters that were typed instead of typesetting them.
    /// <para>
    /// An escape hatch, not a second editor. Typing straight into the rendered maths is the whole
    /// point of the Latex tab and is what nearly everyone will do — this is for the times the
    /// rendering is itself the problem (a formula that will not typeset, and you want to see exactly
    /// what you wrote), and for people who would rather write LaTeX than click a palette. Same
    /// surface either way: the editor changes what it shows, nothing swaps in beside it.
    /// </para>
    /// <para>
    /// Deliberately not persisted. It gets you out of trouble with one formula, so a tab opened
    /// tomorrow should still open rendered — but it lives on the ViewModel rather than the view, so
    /// leaving the Solver for another shell tab and coming back keeps it where you left it.
    /// </para>
    /// </summary>
    [ObservableProperty]
    private bool _showsSource;

    // ── Chips and results ───────────────────────────────────────────────────

    /// <summary>What the solvers are offering for the current definition.</summary>
    public ObservableCollection<SolverChipViewModel> Chips { get; } = [];

    /// <summary>The answers so far, oldest first.</summary>
    public ObservableCollection<SolverResultViewModel> Results { get; } = [];

    /// <summary>True when no chip is on offer, so the view can explain why the strip is empty.</summary>
    public bool HasNoChips => Chips.Count == 0;

    /// <summary>True once something has been answered.</summary>
    public bool HasResults => Results.Count > 0;

    // ── Palette ─────────────────────────────────────────────────────────────

    /// <summary>Whether the palette is showing.</summary>
    [ObservableProperty]
    private bool _isPaletteOpen;

    /// <summary>What the navigator's centre reads at the top of the tree.</summary>
    private const string TopLabel = "Symbols";

    /// <summary>
    /// How many symbols the recently-used strip remembers. Two columns of five: the first fills, and
    /// the second only appears once there is a sixth.
    /// </summary>
    public const int RecentCapacity = 10;

    /// <summary>Which page of the calculator keypad is showing.</summary>
    [ObservableProperty]
    private PaletteGroup _calcPage;

    /// <summary>
    /// Where the navigator currently is — "Symbols › Greek › α – θ" — one step per entry, each a way
    /// back to that level. A trail you can only read is half a trail: the centre steps up one at a
    /// time, so without this getting back to the top of a three-deep tree is three clicks.
    /// </summary>
    public IReadOnlyList<PaletteCrumb> LatexCrumbs { get; private set; } = [];

    /// <summary>
    /// The path down from the categories to whatever the navigator is showing. Holding the path
    /// rather than a parent pointer is what makes the centre-to-go-up trivial and exact. Empty means
    /// the top ring — the categories themselves.
    /// </summary>
    private readonly List<PaletteGroup> _latexPath = [];

    /// <summary>
    /// The symbols used most recently, in the order they were first reached.
    /// <para>
    /// Deliberately not most-recent-first. A strip beside the navigator is a click target, and a
    /// target that reorders itself every time you hit it cannot be learned; positions are held and
    /// only eviction moves anything. Recency decides <i>what</i> is dropped, never <i>where</i>
    /// something sits.
    /// </para>
    /// </summary>
    public ObservableCollection<PaletteKey> RecentKeys { get; } = [];

    /// <summary>When each remembered symbol was last used — the eviction order, not the display order.</summary>
    private readonly Dictionary<PaletteKey, DateTimeOffset> _lastUsed = [];

    /// <summary>Whether the recently-used strip has anything to show.</summary>
    public bool HasRecentKeys => RecentKeys.Count > 0;

    /// <summary>
    /// The eight positions the navigator draws: the categories at the top, a category's groups one
    /// level in, and the symbols themselves at the bottom.
    /// </summary>
    public IReadOnlyList<PaletteTile> LatexTiles
    {
        get
        {
            if (_latexPath.Count == 0) return Fill([.. LatexPalette.Categories.Select(Tile)]);

            var here = _latexPath[^1];
            return here.Children.Count > 0
                ? Fill([.. here.Children.Select(Tile)])
                : Fill([.. here.Keys.Select((key, i) => Tile(here.Id, key, i))]);
        }
    }

    /// <summary>What the navigator's centre reads.</summary>
    public string LatexCentreLabel => _latexPath.Count > 0 ? _latexPath[^1].Label : TopLabel;

    /// <summary>Whether the centre can step back up.</summary>
    public bool CanGoUpLatex => _latexPath.Count > 0;

    /// <summary>The keys the calculator keypad should draw right now. LaTeX has no grid — see the navigator.</summary>
    public IReadOnlyList<PaletteKey> PaletteKeys => CalcPage.Keys;

    // ── Commands ────────────────────────────────────────────────────────────

    /// <summary>Switches editor.</summary>
    [RelayCommand]
    private void SetMode(DefinitionMode mode) => Mode = mode;

    /// <summary>Shows or hides the palette.</summary>
    [RelayCommand]
    private void TogglePalette() => IsPaletteOpen = !IsPaletteOpen;

    /// <summary>Swaps between degrees and radians, and re-offers the chips under the new reading.</summary>
    [RelayCommand]
    private void ToggleAngleUnit()
        => AngleUnit = AngleUnit == AngleUnit.Degrees ? AngleUnit.Radians : AngleUnit.Degrees;

    /// <summary>Empties the definition area.</summary>
    [RelayCommand]
    private void ClearDefinition()
    {
        DefinitionText = string.Empty;
        DefinitionReplaced?.Invoke(string.Empty);
    }

    /// <summary>Removes every answer.</summary>
    [RelayCommand]
    private void ClearResults()
    {
        foreach (var result in Results) result.Dispose();
        Results.Clear();
        OnPropertyChanged(nameof(HasResults));
    }

    /// <summary>Handles a palette key: either a command, or text to insert.</summary>
    [RelayCommand]
    private void PressKey(PaletteKey? key)
    {
        if (key is null || key.IsBlank) return;

        if (key.IsCommand)
        {
            RunPaletteCommand(key.CommandId);
            return;
        }

        if (IsLatexMode) Remember(key);
        InsertRequested?.Invoke(key);
    }

    /// <summary>
    /// Takes a navigator position: a category or group opens a level, a symbol types itself.
    /// <para>
    /// One command for both because the ring makes no distinction — the symbols <i>are</i> the last
    /// level, so drilling and typing are the same gesture and splitting them into two commands would
    /// only create two things that have to agree about where you are.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void SelectLatexTile(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;

        var mark = id.IndexOf('#');
        if (mark < 0)
        {
            if (LatexPalette.Find(id) is not { } group) return;
            _latexPath.Add(group);
            RaiseNavigatorChanged();
            return;
        }

        if (LatexPalette.Find(id[..mark]) is not { } owner) return;
        if (!int.TryParse(id[(mark + 1)..], out var slot)) return;
        if (slot < 0 || slot >= owner.Keys.Count) return;

        PressKey(owner.Keys[slot]);
    }

    /// <summary>Steps the navigator back up a level.</summary>
    [RelayCommand]
    private void LatexNavigateUp()
    {
        if (_latexPath.Count == 0) return;
        _latexPath.RemoveAt(_latexPath.Count - 1);
        RaiseNavigatorChanged();
    }

    /// <summary>Jumps straight to a level of the breadcrumb, however deep the navigator has gone.</summary>
    [RelayCommand]
    private void NavigateToCrumb(int depth)
    {
        if (depth < 0 || depth >= _latexPath.Count) return;   // nonsense, or already there
        _latexPath.RemoveRange(depth, _latexPath.Count - depth);
        RaiseNavigatorChanged();
    }

    /// <summary>
    /// Records a symbol as recently used.
    /// <para>
    /// A symbol already in the strip keeps its position — see <see cref="RecentKeys"/> — and a new one
    /// <i>takes over</i> the position of the least recently used rather than being appended after it
    /// was removed. Appending would slide every symbol after the evicted one along by a place, which
    /// is the same disruption reordering would cause, just rarer and therefore more surprising.
    /// </para>
    /// </summary>
    private void Remember(PaletteKey key)
    {
        var known = RecentKeys.Contains(key);
        _lastUsed[key] = NextStamp();

        // Re-using a symbol only moves its timestamp, so it is flushed on close rather than written
        // through: the strip is meant to be pressed, and a whole config file per press is a disk
        // write on the UI thread for something nobody would miss if a crash lost it.
        if (known) return;

        if (RecentKeys.Count < RecentCapacity)
        {
            RecentKeys.Add(key);
            OnPropertyChanged(nameof(HasRecentKeys));
        }
        else
        {
            var stalest = StalestSlot();
            _lastUsed.Remove(RecentKeys[stalest]);
            RecentKeys[stalest] = key;
        }

        SaveRecent();
    }

    /// <summary>Which position holds the symbol that has gone longest without being used.</summary>
    private int StalestSlot()
    {
        var stalest = 0;
        for (var i = 1; i < RecentKeys.Count; i++)
            if (When(RecentKeys[i]) < When(RecentKeys[stalest])) stalest = i;
        return stalest;
    }

    private DateTimeOffset When(PaletteKey key) => _lastUsed.GetValueOrDefault(key);

    /// <summary>
    /// Now, but never earlier than the last stamp handed out. A real time is what makes the eviction
    /// order survive a restart; forcing it forward is what stops two presses inside one clock tick
    /// from being indistinguishable.
    /// </summary>
    private DateTimeOffset NextStamp()
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastUsed.Count == 0) return now;

        var latest = _lastUsed.Values.Max();
        return now > latest ? now : latest.AddTicks(1);
    }

    /// <summary>Restores the strip from the last session, keeping both its order and its recency.</summary>
    private void RestoreRecent()
    {
        foreach (var remembered in _config.RecentSymbols.Take(RecentCapacity))
        {
            // Resolved by what it types, so a symbol that has moved in the tree is still found and
            // one that has been dropped from it simply does not come back.
            var key = LatexPalette.LeafGroups()
                .SelectMany(g => g.Keys)
                .FirstOrDefault(k => !k.IsBlank && k.Insert == remembered.Insert);

            if (key is null || RecentKeys.Contains(key)) continue;

            RecentKeys.Add(key);
            _lastUsed[key] = remembered.LastUsed;
        }
    }

    private void SaveRecent()
    {
        _config.RecentSymbols =
            [.. RecentKeys.Select(k => new RecentSymbol { Insert = k.Insert, LastUsed = When(k) })];

        try { _shell.SaveFeatureConfig(_config); } catch { /* best-effort persistence */ }
    }

    /// <summary>Holds the ring's spare positions open. See <see cref="LatexPalette"/>.</summary>
    private static IReadOnlyList<PaletteTile> Fill(List<PaletteTile> tiles)
    {
        while (tiles.Count < LatexPalette.Slots) tiles.Add(PaletteTile.Gap);
        return tiles;
    }

    private static PaletteTile Tile(PaletteGroup group)
        => new(group.Id, group.Label, group.Label, true, null);

    private static PaletteTile Tile(string ownerId, PaletteKey key, int slot)
        => key.IsBlank ? PaletteTile.Gap : new PaletteTile($"{ownerId}#{slot}", key.Label, key.Tooltip, false, key);

    private void RaiseNavigatorChanged()
    {
        var crumbs = new List<PaletteCrumb>(_latexPath.Count + 1)
        {
            new(TopLabel, 0, _latexPath.Count == 0),
        };
        for (var i = 0; i < _latexPath.Count; i++)
            crumbs.Add(new PaletteCrumb(_latexPath[i].Label, i + 1, i == _latexPath.Count - 1));

        LatexCrumbs = crumbs;

        OnPropertyChanged(nameof(LatexTiles));
        OnPropertyChanged(nameof(LatexCentreLabel));
        OnPropertyChanged(nameof(CanGoUpLatex));
        OnPropertyChanged(nameof(LatexCrumbs));
    }

    private void RunPaletteCommand(string id)
    {
        switch (id)
        {
            case CalcPalette.SecondPageId:
                CalcPage = CalcPage.Id == CalcPalette.Second.Id ? CalcPalette.Main : CalcPalette.Second;
                break;

            case CalcPalette.ConstPageId:
                CalcPage = CalcPage.Id == CalcPalette.Constants.Id ? CalcPalette.Main : CalcPalette.Constants;
                break;

            case CalcPalette.ClearId:
                ClearDefinition();
                break;

            case CalcPalette.BackspaceId:
                if (DefinitionText.Length > 0)
                {
                    DefinitionText = DefinitionText[..^1];
                    DefinitionReplaced?.Invoke(DefinitionText);
                }
                break;

            case CalcPalette.AngleToggleId:
                ToggleAngleUnit();
                break;
        }
    }

    // ── Property change plumbing ────────────────────────────────────────────

    partial void OnModeChanged(DefinitionMode oldValue, DefinitionMode newValue)
    {
        // Nothing is copied and nothing is replaced: each tab's editor is bound to its own buffer and
        // has been holding it all along. Switching only changes which one is on show.
        OnPropertyChanged(nameof(DefinitionText));
        OnPropertyChanged(nameof(ModeName));
        OnPropertyChanged(nameof(IsCalcMode));
        OnPropertyChanged(nameof(IsTextMode));
        OnPropertyChanged(nameof(IsMarkdownMode));
        OnPropertyChanged(nameof(IsLatexMode));
        OnPropertyChanged(nameof(IsSingleFormula));
        OnPropertyChanged(nameof(PaletteKeys));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasText));

        ScheduleChipRefresh();
    }

    partial void OnAngleUnitChanged(AngleUnit value)
    {
        OnPropertyChanged(nameof(AngleLabel));
        ScheduleChipRefresh();
    }

    partial void OnCalcPageChanged(PaletteGroup value) => OnPropertyChanged(nameof(PaletteKeys));

    // ── Chips ───────────────────────────────────────────────────────────────

    /// <summary>The definition as the solvers see it.</summary>
    public SolverInput CurrentInput => new(Mode, DefinitionText, AngleUnit, _config.GetDecimalPlaces());

    /// <summary>
    /// Re-asks the solvers what they can offer, after a short pause and off the UI thread.
    /// <para>
    /// Both halves matter. Parsing on every keystroke would run the grammar against every prefix of
    /// what is being typed, and doing it on the dispatcher would make the definition area stutter
    /// while it happened.
    /// </para>
    /// </summary>
    private void ScheduleChipRefresh()
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = new CancellationTokenSource();
        var ct = _debounce.Token;

        var input = CurrentInput;
        if (input.Text.Length > MaxDefinitionLength)
        {
            _ = ApplyChipsAsync([], ct);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ChipDebounceMs, ct).ConfigureAwait(false);
                var chips = _registry.ChipsFor(input);
                await ApplyChipsAsync(chips, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a later keystroke.
            }
        }, ct);
    }

    private async Task ApplyChipsAsync(IReadOnlyList<SolverChip> chips, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || _disposed) return;

        await _shell.RunOnUiAsync(() =>
        {
            if (ct.IsCancellationRequested || _disposed) return;

            Chips.Clear();
            foreach (var chip in chips)
                Chips.Add(new SolverChipViewModel(chip, RunChipAsync));

            OnPropertyChanged(nameof(HasNoChips));
        }).ConfigureAwait(false);
    }

    // ── Running a chip ──────────────────────────────────────────────────────

    /// <summary>
    /// Appends a result cell and fills it in. The cell appears immediately, busy, so a slow AI
    /// answer shows up as something happening rather than as nothing happening.
    /// </summary>
    public async Task RunChipAsync(SolverChip chip)
    {
        var input = CurrentInput;

        var cell = new SolverResultViewModel(
            chip.Label, input.Trimmed, input.Mode, RemoveResult, UseAsDefinition, CopyToClipboard);

        Results.Add(cell);
        OnPropertyChanged(nameof(HasResults));

        try
        {
            var result = await Task.Run(() => _registry.SolveAsync(chip, input, cell.Token), cell.Token)
                                   .ConfigureAwait(false);

            await _shell.RunOnUiAsync(() => cell.Complete(result)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The cell was removed, or the tab closed, while the solver was still working.
        }
        catch (Exception e)
        {
            await _shell.RunOnUiAsync(() => cell.Complete(SolverResult.Error($"Something went wrong: {e.Message}")))
                        .ConfigureAwait(false);
        }
    }

    private void RemoveResult(SolverResultViewModel cell)
    {
        Results.Remove(cell);
        OnPropertyChanged(nameof(HasResults));
    }

    /// <summary>
    /// Puts an answer back in the definition area. The markdown fences come off first — what goes
    /// back has to be something the editor and the solvers can read, not a rendered block.
    /// </summary>
    private void UseAsDefinition(string markdown)
    {
        var text = StripMathFences(markdown);
        if (text.Length == 0) return;

        // Only the right-hand side is worth carrying forward: an answer reads "input = result",
        // and feeding the whole equation back would ask the next chip to solve what was just solved.
        var at = text.LastIndexOf('=');
        if (at >= 0 && at < text.Length - 1) text = text[(at + 1)..].Trim();

        Mode = DefinitionMode.Latex;
        DefinitionText = text;
        DefinitionReplaced?.Invoke(text);
    }

    private void CopyToClipboard(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
            _shell.ShowNotification("Copied to the clipboard.");
        }
        catch (Exception)
        {
            // The clipboard is shared with every other process and can genuinely be locked. Losing
            // a copy is not worth an error dialog.
        }
    }

    /// <summary>Pulls the LaTeX body out of a <c>$$…$$</c> block.</summary>
    private static string StripMathFences(string markdown)
    {
        var text = markdown.Trim();
        if (!text.StartsWith("$$", StringComparison.Ordinal)) return text;

        text = text[2..];
        var end = text.IndexOf("$$", StringComparison.Ordinal);
        if (end >= 0) text = text[..end];
        return text.Trim();
    }

    // ── AI surface ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string GetContext()
    {
        var sb = new StringBuilder();
        sb.Append("Solver page. Definition mode: ").Append(Mode)
          .Append("; angles in ").Append(AngleUnit == AngleUnit.Degrees ? "degrees" : "radians").Append(".\n");

        sb.Append(IsEmpty
            ? "The definition area is empty.\n"
            : $"Definition:\n```\n{DefinitionText}\n```\n");

        if (Chips.Count > 0)
            sb.Append("Chips currently offered: ").Append(string.Join(", ", Chips.Select(c => c.Label))).Append(".\n");

        sb.Append(Results.Count == 0
            ? "No results yet."
            : $"{Results.Count} result(s) so far, most recent from the '{Results[^1].ChipLabel}' chip.");

        return sb.ToString();
    }

    /// <summary>
    /// This page's scope boundary. Distinct per instance, not a constant.
    /// <para>
    /// A Solver page's tools act on its own definition and its own results and nothing else, so two
    /// of them pinned into one conversation are two separate boundaries. Returning the same string
    /// from both would let the agent's tool table collapse them first-wins, and every
    /// <c>solver_run_chip</c> would land on whichever page happened to be registered first — the
    /// kind of failure that looks like the tool working.
    /// </para>
    /// </summary>
    public string? GetSecurityContext() => _scopeId;

    /// <inheritdoc/>
    public string? GetAiSystemPromptGuidance() =>
        "The Solver page is a maths workspace. The user has a definition (a calculator line, LaTeX, " +
        "or prose) and a list of worked answers. It renders LaTeX, so give maths as $...$ or $$...$$. " +
        "You can read the definition and results, replace the definition, and run any chip currently " +
        "on offer — prefer running a chip over working something out yourself, because the chips use " +
        "an exact algebra engine.";

    /// <inheritdoc/>
    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new DelegateClientTool(
            "solver_read_state",
            "Read the Solver page: the current definition, the chips on offer, and every result so far.",
            [],
            ToolSafety.SafeOperation,
            (_, _) => Task.FromResult(ToolResult.Ok("Read the Solver page.", DescribeState())),
            parallelizable: true),

        new DelegateClientTool(
            "solver_set_definition",
            "Replace the Solver's definition area with new text. Use mode 'calc' for a plain " +
            "expression, 'latex' for LaTeX, or 'text' for prose.",
            [
                new ClientToolParameter("definition", "The text to put in the definition area."),
                new ClientToolParameter("mode", "One of: calc, latex, text.", Required: false),
            ],
            // Replaces what the user typed, which is theirs and not recoverable from here.
            ToolSafety.RequiresApproval,
            SetDefinitionToolAsync),

        new DelegateClientTool(
            "solver_run_chip",
            "Run one of the chips currently on offer and append its answer to the results. Pass the " +
            "chip's label exactly as solver_read_state reported it.",
            [new ClientToolParameter("chip", "The chip label, e.g. '=', 'simplify', 'd/dx'.")],
            ToolSafety.SafeOperation,
            RunChipToolAsync),
    ];

    private string DescribeState()
    {
        var sb = new StringBuilder();
        sb.Append("Mode: ").Append(Mode).Append('\n');
        sb.Append("Angles: ").Append(AngleUnit).Append('\n');
        sb.Append("Definition: ").Append(IsEmpty ? "(empty)" : DefinitionText).Append('\n');
        sb.Append("Chips on offer: ")
          .Append(Chips.Count == 0 ? "(none)" : string.Join(", ", Chips.Select(c => c.Label))).Append('\n');

        if (Results.Count == 0)
        {
            sb.Append("Results: (none)");
            return sb.ToString();
        }

        sb.Append("Results:\n");
        for (var i = 0; i < Results.Count; i++)
        {
            var r = Results[i];
            sb.Append(i + 1).Append(". [").Append(r.ChipLabel).Append("] of `").Append(r.Definition).Append("`\n");
            sb.Append(r.IsBusy ? "   (still running)\n" : $"{Indent(r.Markdown)}\n");
        }

        return sb.ToString();

        static string Indent(string text)
            => string.Join('\n', text.Split('\n').Select(l => "   " + l));
    }

    private async Task<ToolResult> SetDefinitionToolAsync(System.Text.Json.Nodes.JsonObject args, CancellationToken ct)
    {
        var definition = ToolArgs.Str(args, "definition", "text", "value") ?? string.Empty;
        var modeText = ToolArgs.Str(args, "mode");

        var mode = modeText is null ? Mode
            : Enum.TryParse<DefinitionMode>(modeText, ignoreCase: true, out var m) ? m
            : Mode;

        await _shell.RunOnUiAsync(() =>
        {
            Mode = mode;
            DefinitionText = definition;
            DefinitionReplaced?.Invoke(definition);
        }).ConfigureAwait(false);

        return ToolResult.Ok($"Set the definition ({mode}).", $"The definition area now holds:\n{definition}");
    }

    private async Task<ToolResult> RunChipToolAsync(System.Text.Json.Nodes.JsonObject args, CancellationToken ct)
    {
        var label = ToolArgs.Str(args, "chip", "label", "name");
        if (string.IsNullOrWhiteSpace(label)) return ToolResult.Error("No chip was named.");

        var match = Chips.FirstOrDefault(c => string.Equals(c.Label, label, StringComparison.OrdinalIgnoreCase))
                 ?? Chips.FirstOrDefault(c => c.Chip.Id.Equals(label, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return ToolResult.Error(
                $"No chip called '{label}'.",
                $"The chips currently on offer are: {(Chips.Count == 0 ? "(none)" : string.Join(", ", Chips.Select(c => c.Label)))}.");

        await RunChipAsync(match.Chip).ConfigureAwait(false);

        var produced = Results.LastOrDefault();
        return ToolResult.Ok($"Ran '{match.Label}'.", produced?.Markdown ?? "(no output)");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _debounce?.Cancel(); } catch (ObjectDisposedException) { }
        _debounce?.Dispose();
        _debounce = null;

        foreach (var result in Results) result.Dispose();
        Results.Clear();

        // Flushes the recency stamps a re-use only moved in memory — see Remember.
        if (RecentKeys.Count > 0) SaveRecent();
    }
}
