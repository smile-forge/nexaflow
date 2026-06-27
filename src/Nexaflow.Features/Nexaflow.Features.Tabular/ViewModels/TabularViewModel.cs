using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Tabular.Detection;
using Nexaflow.Features.Tabular.Streaming;
using Nexaflow.Features.Tabular.Templates;

namespace Nexaflow.Features.Tabular.ViewModels;

public sealed partial class TabularViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private const long  SmallFileThresholdBytes = 100_000;
    private const int   VisibleWindowSize       = 150;

    private readonly IShellServices _shell;
    private readonly IAIService     _ai;
    private CancellationTokenSource _windowCts = new();
    private CancellationTokenSource? _countCts;

    private IRowSource?       _source;
    private DataShape?        _data;
    private FileSample?       _sample;
    private readonly string?  _initialTransformsJson;

    private readonly TabularTemplatesConfig _templates;
    private List<TabularTemplate> _pendingChoices = new();

    /// <summary>The current data shape (raw shape + transform chain). Used by the ribbon
    /// pin handler to capture the user's morphs into the pinned button's params.</summary>
    public DataShape? DataShape => _data;

    /// <summary>Last absolute row index the user anchored selection on — used for Shift-click
    /// range extension in the Excel-style row-selection model.</summary>
    private int               _selectionAnchorRow = -1;
    public ObservableCollection<int> SelectedRowIndices { get; } = new();

    [ObservableProperty] private string  _filePath    = string.Empty;
    [ObservableProperty] private string  _fileName    = string.Empty;
    [ObservableProperty] private string  _encodingName = "?";
    [ObservableProperty] private string  _detectedShapeLabel = "Detecting…";
    [ObservableProperty] private long    _fileSizeBytes;
    [ObservableProperty] private int?    _knownRowCount;
    [ObservableProperty] private int     _estimatedRowCount;
    [ObservableProperty] private bool    _isCountingRows;

    /// <summary>What the grid binds for its scrollbar extent: the actual file row count
    /// once <see cref="RowCounter"/> has finished, otherwise the estimate from
    /// <c>FileLength / AverageLineByteSize</c>. Falls back to a single-window minimum.</summary>
    public int TotalRowCount => KnownRowCount ?? System.Math.Max(EstimatedRowCount, VisibleWindowSize);

    partial void OnKnownRowCountChanged(int? value)     => OnPropertyChanged(nameof(TotalRowCount));
    partial void OnEstimatedRowCountChanged(int value)  => OnPropertyChanged(nameof(TotalRowCount));
    [ObservableProperty] private bool    _isSmallMode;
    [ObservableProperty] private bool    _isLoading = true;
    [ObservableProperty] private int     _focalRow;           // 0-based
    [ObservableProperty] private string  _statusMessage = string.Empty;

    /// <summary>Raw text of leading "#" comment lines (if any) — surfaced as an info-icon
    /// tooltip above the column headers so the user can see which lines were stripped.</summary>
    [ObservableProperty] private string  _commentTooltip = string.Empty;
    [ObservableProperty] private bool    _hasComments;

    /// <summary>True iff at least one column is currently selected — drives the right-side
    /// filter slide-out's visibility (no separate toolbar toggle).</summary>
    public bool IsFilterPanelOpen => Columns.Any(c => c.IsSelected);

    public ObservableCollection<TabularColumnViewModel> Columns { get; } = new();
    public ObservableCollection<TabularRowViewModel>    Window  { get; } = new();
    public ObservableCollection<BreadcrumbStep>          PathBreadcrumb { get; } = new();

    /// <summary>Fired when the row layout changes (initial load, morph, sort) — the view
    /// should rebuild header layout to match.</summary>
    public event Action? LayoutChanged;

    private Task _ready = Task.CompletedTask;
    /// <summary>Completes when the initial sample + shape detection + first window have all
    /// finished. Tests await this; the View doesn't need to.</summary>
    public Task Ready => _ready;

    // ── Templates: Template-This popup ────────────────────────────────────
    [ObservableProperty] private bool _isTemplatePopupOpen;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SaveTemplateCommand))] private string _templateName = string.Empty;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SaveTemplateCommand))] private TemplateScope _selectedScope = TemplateScope.Folder;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SaveTemplateCommand))] private string _globPattern = string.Empty;
    [ObservableProperty] private string _templateFolderPath = string.Empty;

    // ── Templates: Apply-Template panel ───────────────────────────────────
    [ObservableProperty] private bool _isTemplatePanelOpen;
    [ObservableProperty] private bool _showOnlyCompatible = true;
    /// <summary>True while the panel is forcing a choice between several auto-apply matches.</summary>
    [ObservableProperty] private bool _isChooseMode;
    public ObservableCollection<TemplateItemViewModel> PanelTemplates { get; } = new();

    partial void OnShowOnlyCompatibleChanged(bool value) => RebuildPanelTemplates();

    public TabularViewModel(string filePath, IShellServices shell, IAIService ai,
                            TabularTemplatesConfig templates, string? initialTransformsJson = null)
    {
        _shell                  = shell;
        _ai                     = ai;
        _templates              = templates;
        _initialTransformsJson  = initialTransformsJson;
        FilePath                = filePath;
        FileName                = string.IsNullOrEmpty(filePath) ? "Tabular" : Path.GetFileName(filePath);
        if (!string.IsNullOrEmpty(filePath))
        {
            _ready = LoadAsync();
        }
    }

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"Tabular file '{FileName}' — {EncodingName}, " +
                  $"{KnownRowCount?.ToString() ?? "still counting"} rows, " +
                  $"{Columns.Count} columns. Detected shape: {DetectedShapeLabel}. " +
                  $"Mode: {(IsSmallMode ? "small (full-file, sortable)" : "large (windowed)")}.");

        if (Columns.Count > 0)
        {
            sb.Append("\nColumns:");
            for (int i = 0; i < Columns.Count; i++)
                sb.Append($"\n  [{i + 1}] {Columns[i].Header} ({Columns[i].DisplayType})");
        }

        // Prefer selected rows for the sample; fall back to the currently visible window.
        var sampleRows = SelectedRowIndices.Count > 0
            ? Window.Where(r => SelectedRowIndices.Contains(r.AbsoluteIndex)).Take(20).ToList()
            : Window.Take(10).ToList();
        if (sampleRows.Count > 0)
        {
            sb.Append($"\n{(SelectedRowIndices.Count > 0 ? "Selected" : "Visible")} rows:");
            foreach (var r in sampleRows)
            {
                sb.Append($"\n  row {r.AbsoluteIndex + 1}: ");
                sb.Append(string.Join(" | ", r.Cells.Take(8)));
                if (r.Cells.Count > 8) sb.Append(" | …");
            }
        }
        return sb.ToString();
    }

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new DelegateClientTool(
            "open_filter",
            "Open the filter side panel",
            [],
            ToolSafety.ReadOnly,
            (arguments, ct) =>
            {
                if (Columns.Count > 0) Columns[0].IsSelected = true;
                return Task.FromResult(ToolResult.Ok("opened filter", "Opened the filter side panel."));
            }),
        new DelegateClientTool(
            "scroll_to_top",
            "Scroll to the first row",
            [],
            ToolSafety.ReadOnly,
            (arguments, ct) =>
            {
                FocalRow = 0;
                _ = RefreshWindowAsync();
                return Task.FromResult(ToolResult.Ok("scrolled to top", "Scrolled to the first row."));
            }),
        new DelegateClientTool(
            "scroll_to_end",
            "Scroll to the last row",
            [],
            ToolSafety.ReadOnly,
            (arguments, ct) =>
            {
                FocalRow = (KnownRowCount ?? 0) - 1;
                _ = RefreshWindowAsync();
                return Task.FromResult(ToolResult.Ok("scrolled to end", "Scrolled to the last row."));
            }),
    ];

    // ── Loading + shape detection ─────────────────────────────────────────

    private async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Reading sample…";
            BuildBreadcrumb();

            _sample = await Task.Run(() => LineSamplingReader.Read(FilePath));
            FileSizeBytes = _sample.FileLength;
            EncodingName  = _sample.Encoding.WebName.ToUpperInvariant();
            // Estimate row count up-front so the synthetic scrollbar can scroll past the
            // first window even before the background RowCounter finishes.
            EstimatedRowCount = (int)System.Math.Max(
                1,
                _sample.FileLength / System.Math.Max(_sample.AverageLineByteSize, 1));

            StatusMessage = "Detecting shape…";
            var shapes = ShapeDetector.DetectAll(_sample);
            var raw    = await PickShapeAsync(shapes);
            DetectedShapeLabel = raw.Label;

            // Build the DataShape: raw shape + raw header tokens (untrimmed; transforms re-tokenize).
            // First, see if any of the leading "#" comment lines was actually a header in
            // disguise — tokenize each one with the detected separator and look for one that
            // splits into exactly FieldCount fields. Per-file convention is usually the LAST
            // comment before the data block, so we walk from the end backward.
            int commentsToSkip = _sample.LeadingCommentLines.Count;
            string[]? rawHeaderCells = null;
            if (raw.HasHeader && _sample.HeadLines.Count > 0)
                rawHeaderCells = SmallFileLoader.TokenizeRow(_sample.HeadLines[0], raw);
            else if (!raw.HasHeader && _sample.LeadingCommentLines.Count > 0)
            {
                for (int i = _sample.LeadingCommentLines.Count - 1; i >= 0; i--)
                {
                    var tokens = SmallFileLoader.TokenizeRow(_sample.LeadingCommentLines[i], raw);
                    if (tokens.Length == raw.FieldCount && raw.FieldCount > 1)
                    {
                        rawHeaderCells = tokens;
                        // Promote this comment to header. Only skip the comments STRICTLY
                        // above it; the promoted comment itself is consumed by the readers'
                        // HasHeader skip — counting it in both leadingCommentCount AND
                        // HasHeader eats data row 0 (the very first real row).
                        commentsToSkip      = i;
                        raw = raw with { HasHeader = true };
                        DetectedShapeLabel  = raw.Label + " (header from comment row)";
                        break;
                    }
                }
            }
            _data = new DataShape(raw, rawHeaderCells, commentsToSkip);

            // Tooltip lists everything we treated as a comment (not including the promoted
            // comment, which is now the header).
            if (commentsToSkip > 0)
            {
                HasComments     = true;
                CommentTooltip  = "File begins with comment line(s) — skipped:\n\n" +
                                  string.Join("\n", _sample.LeadingCommentLines.Take(commentsToSkip));
            }

            // Replay any transforms supplied at construction (ribbon-pin replay path) BEFORE
            // building the initial column VMs so the view sees the morphed shape from frame one.
            // A ribbon pin's explicit chain wins; only when there is none do we consult templates
            // for an auto-apply match against this file's path + original shape.
            if (!string.IsNullOrEmpty(_initialTransformsJson))
            {
                foreach (var t in TransformSerializer.Deserialize(_initialTransformsJson))
                    _data.AddTransform(t);
            }
            else
            {
                var matches = TemplateMatcher.FindAutoApply(
                    _templates.Templates, FilePath, _data.RawShape, _data.RawHeaderCells);
                if (matches.Count == 1)
                {
                    foreach (var t in TransformSerializer.Deserialize(matches[0].TransformsJson))
                        _data.AddTransform(t);
                }
                else if (matches.Count > 1)
                {
                    // Don't guess — surface the candidates and let the user pick (handled post-load).
                    _pendingChoices = matches.ToList();
                }
            }

            RebuildColumns();

            IsSmallMode = FileSizeBytes < SmallFileThresholdBytes;
            _source     = IsSmallMode
                ? new SmallFileLoader(FilePath, _sample.Encoding, _sample.BomByteCount, _data)
                : (IRowSource)new RowWindowReader(FilePath, _sample.Encoding, _sample.BomByteCount, _data);

            // Known count: immediate for small mode; deferred for large mode.
            if (_source is SmallFileLoader sm) KnownRowCount = sm.KnownRowCount;
            else
            {
                IsCountingRows = true;
                _countCts = new CancellationTokenSource();
                _ = RowCounter.CountAsync(
                    FilePath, _sample.BomByteCount, _data.RawShape,
                    rows => _shell.RunOnUiAsync(() => GrowKnownRowCount(rows)),
                    _countCts.Token,
                    leadingCommentLines: _data.LeadingCommentCount,
                    encoding: _sample.Encoding)
                  .ContinueWith(t =>
                  {
                      _ = _shell.RunOnUiAsync(() =>
                      {
                          IsCountingRows = false;
                          if (_source is RowWindowReader rw && t.Status == TaskStatus.RanToCompletion)
                              rw.SetKnownRowCount(t.Result);
                      });
                  }, TaskScheduler.Default);
            }

            await RefreshWindowAsync();
            LayoutChanged?.Invoke();
            StatusMessage = string.Empty;

            // A single auto-apply match is applied silently (the reshaped grid is self-evident).
            // Only the ambiguous case needs the user: surface the candidates and let them pick.
            if (_pendingChoices.Count > 1)
            {
                IsChooseMode = true;
                RebuildPanelTemplates();
                IsTemplatePanelOpen = true;
                _shell.ShowNotification("Multiple templates match this file — choose one to apply.");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            _shell.ShowError($"Failed to open tabular file: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<CsvShape> PickShapeAsync(IReadOnlyList<CsvShape> shapes)
    {
        // The detector resolves the vast majority of files on its own; only a genuinely close,
        // structurally-different pair of candidates needs the model's judgement.
        if (ShapeDetector.TryPickConfident(shapes, out var confident)) return confident;

        // Ambiguous — ask the Disambiguation model.
        var sampleText = string.Join("\n", (_sample!.HeadLines).Take(4).Concat(_sample.TailLines.Take(4)));
        var ctx = $"Filename: {FileName}\nEncoding: {EncodingName}\nFirst+last 4 lines:\n{sampleText}";
        var question = "Which interpretation best matches the data?";
        var options = shapes.Take(4)
            .Select(s => (Label: s.Label, Detail: s.Description))
            .ToList();

        try
        {
            var idx = await _ai.DisambiguateOptionAsync(ctx, question, options);
            if (idx is int i && i >= 0 && i < shapes.Count) return shapes[i];
        }
        catch { /* fall through to the top-ranked shape */ }
        return shapes[0];
    }

    /// <summary>
    /// Rebuilds <see cref="Columns"/> from <see cref="_data"/>.EffectiveColumns. Called
    /// once on initial load and again after any morph (merge / split / evaluate-as).
    /// Selection / sort / filter on the old column VMs is intentionally not preserved
    /// across rebuilds — the column identity has changed.
    /// </summary>
    private void RebuildColumns()
    {
        if (_data is null) return;
        foreach (var c in Columns) c.PropertyChanged -= OnColumnChanged;
        Columns.Clear();
        SelectedRowIndices.Clear();
        _selectionAnchorRow = -1;

        var effective = _data.EffectiveColumns;
        for (int i = 0; i < effective.Length; i++)
        {
            var c = new TabularColumnViewModel
            {
                Header         = effective[i].Header,
                DetectedType   = effective[i].Type,
                DisplayType    = effective[i].Type,
                IsTypeExplicit = effective[i].IsTypeExplicit,
                Index          = i,
            };
            c.PropertyChanged += OnColumnChanged;
            Columns.Add(c);
        }
        OnPropertyChanged(nameof(IsFilterPanelOpen));
        LayoutChanged?.Invoke();
    }

    private void OnColumnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabularColumnViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(IsFilterPanelOpen));
            return;
        }
        if (e.PropertyName is nameof(TabularColumnViewModel.Filter)
            or nameof(TabularColumnViewModel.SortDirection))
        {
            if (e.PropertyName == nameof(TabularColumnViewModel.SortDirection) && IsSmallMode)
                ApplySort((TabularColumnViewModel)sender!);
            _ = RefreshWindowAsync();
        }
    }

    /// <summary>Re-loads the current window using filter + focal row. Called after
    /// scroll, filter, or morph changes.</summary>
    public async Task RefreshWindowAsync()
    {
        if (_source is null) return;

        _windowCts.Cancel();
        _windowCts = new CancellationTokenSource();
        var ct = _windowCts.Token;

        // FocalRow now means "the top row currently visible in the viewport". Load 150
        // rows starting AT that row so the in-memory window covers the visible viewport
        // (plus the off-screen buffer below for forward scrolling).
        int fromRow    = System.Math.Max(0, FocalRow);
        var filter     = BuildFilter();

        List<HydratedRow> rows;
        try { rows = await _source.GetVisibleAsync(fromRow, VisibleWindowSize, filter, ct); }
        catch (Exception ex) { _shell.ShowError(ex.Message); return; }
        if (ct.IsCancellationRequested) return;  // a newer scroll superseded us — drop the result

        // Reconcile KnownRowCount with what the reader actually saw:
        //  - If we observed a row index past the current count, GROW (RowCounter may have
        //    undercounted on a file with CR-only or mixed line endings).
        //  - If we got a partial window (rows.Count > 0 && < window size), that's a clear
        //    EOF signal — clamp to the observed last row.
        //  - If we got 0 rows back, we requested past EOF but we have no way to know where
        //    EOF actually is from this signal alone. Leave KnownRowCount alone so the count
        //    doesn't oscillate downward chasing the scroll position the user dragged to.
        bool filterActive = Columns.Any(c => c.Filter.IsActive);
        if (!filterActive && rows.Count > 0)
        {
            int observedTotal = rows[^1].AbsoluteIndex + 1;
            if (KnownRowCount is null || observedTotal > KnownRowCount)
                KnownRowCount = observedTotal;
            if (rows.Count < VisibleWindowSize && KnownRowCount > observedTotal)
                KnownRowCount = observedTotal;
        }

        Window.Clear();
        foreach (var r in rows) Window.Add(new TabularRowViewModel(r.AbsoluteIndex, r.Cells));
        RefreshColumnSamples(rows);
        ReclassifyColumnTypesFromSamples();
        ApplyRowSelectionToWindow();
    }

    /// <summary>
    /// After each window refresh, ratchets the display type of every column whose type
    /// hasn't been explicitly set by the user (via Evaluate As). Only WIDENS — the type
    /// from the initial sample is the starting point, and each subsequent window can
    /// only promote to a broader type that fits both the old type and what was just
    /// observed (Integer→Decimal when a decimal shows up, Date→DateTime when a time
    /// shows up, anything-conflict→String). This prevents the column header from
    /// flip-flopping as the user scrolls through a long file with locally-uniform data.
    /// </summary>
    private void ReclassifyColumnTypesFromSamples()
    {
        foreach (var col in Columns)
        {
            if (col.IsTypeExplicit || col.SampleValues.Count == 0) continue;
            var inferred = ColumnTypeInferrer.AggregateColumn(col.SampleValues);
            var widened  = ColumnTypeInferrer.Promote(col.DisplayType, inferred);
            if (col.DisplayType != widened)
            {
                col.DetectedType = widened;
                col.DisplayType  = widened;
            }
        }
    }

    /// <summary>
    /// Refills each column's <see cref="TabularColumnViewModel.SampleValues"/> from the
    /// rows currently in the window. The header context menu uses these to decide which
    /// split separators and target types to surface.
    /// </summary>
    private void RefreshColumnSamples(IReadOnlyList<HydratedRow> rows)
    {
        const int Cap = 100;
        foreach (var c in Columns) c.SampleValues.Clear();
        foreach (var row in rows)
        {
            for (int i = 0; i < Columns.Count && i < row.Cells.Length; i++)
            {
                var s = row.Cells[i];
                if (string.IsNullOrWhiteSpace(s)) continue;
                if (Columns[i].SampleValues.Count >= Cap) continue;
                Columns[i].SampleValues.Add(s);
            }
        }
    }

    private RowFilter BuildFilter()
    {
        var active = Columns
            .Select((c, i) => (Filter: c.Filter, Index: i))
            .Where(x => x.Filter.IsActive)
            .ToArray();
        if (active.Length == 0) return _ => true;
        return cells =>
        {
            foreach (var (f, i) in active)
                if (i < cells.Length && !f.Matches(cells[i])) return false;
            return true;
        };
    }

    private void ApplySort(TabularColumnViewModel sortedCol)
    {
        if (_source is not SmallFileLoader sm) return;
        // Single-column sort: clear other columns' direction.
        foreach (var c in Columns) if (c != sortedCol && c.SortDirection != SortDirection.None) c.SortDirection = SortDirection.None;
        if (sortedCol.SortDirection == SortDirection.None) return;

        int idx = sortedCol.Index;
        int dir = sortedCol.SortDirection == SortDirection.Asc ? 1 : -1;
        var type = sortedCol.DisplayType;
        // Sort by HYDRATED column value so that splits/merges/etc are honored.
        sm.SortByHydratedColumn(idx, (a, b) => dir * CompareTyped(a, b, type));
    }

    private static int CompareTyped(string? a, string? b, CsvDataType type)
    {
        a = (a ?? string.Empty).Trim();
        b = (b ?? string.Empty).Trim();
        switch (type)
        {
            case CsvDataType.Integer:
                // Use Invariant + AllowThousands so values like "1,200" still sort numerically.
                // Fall through to decimal if integer parse fails — protects users who classified
                // a decimal-bearing column as Integer.
                if (long.TryParse(a, NumberStyles.Integer | NumberStyles.AllowThousands,
                                  CultureInfo.InvariantCulture, out var ai) &&
                    long.TryParse(b, NumberStyles.Integer | NumberStyles.AllowThousands,
                                  CultureInfo.InvariantCulture, out var bi))
                    return ai.CompareTo(bi);
                goto case CsvDataType.Decimal;
            case CsvDataType.Decimal:
            case CsvDataType.Currency:
                if (decimal.TryParse(a.TrimStart('$', '€', '£', '¥', '₹', '¤').Trim(),
                                     NumberStyles.Number | NumberStyles.AllowExponent,
                                     CultureInfo.InvariantCulture, out var ad) &&
                    decimal.TryParse(b.TrimStart('$', '€', '£', '¥', '₹', '¤').Trim(),
                                     NumberStyles.Number | NumberStyles.AllowExponent,
                                     CultureInfo.InvariantCulture, out var bd))
                    return ad.CompareTo(bd);
                break;
            case CsvDataType.Date:
            case CsvDataType.DateTime:
            case CsvDataType.Time:
                if (DateTime.TryParse(a, CultureInfo.InvariantCulture, DateTimeStyles.None, out var adt) &&
                    DateTime.TryParse(b, CultureInfo.InvariantCulture, DateTimeStyles.None, out var bdt))
                    return adt.CompareTo(bdt);
                break;
        }
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    // ── Header context menu commands ──────────────────────────────────────

    /// <summary>
    /// Re-interprets the boundary between <paramref name="index"/> and <paramref name="index"/>-1
    /// as data rather than a separator — the original character that produced the split
    /// (the detected CSV separator) is rejoined into the previous cell's value.
    /// </summary>
    public async Task MergeWithPreviousAsync(int index)
    {
        if (index <= 0 || _data is null) return;
        // Use the separator that originally created the boundary between this column and
        // the one to its left. For columns produced by a SplitByTransform this is the
        // split character (so split-then-merge round-trips); for raw file columns it's
        // the file's detected separator.
        var effective = _data.EffectiveColumns;
        string sep = (index < effective.Length ? effective[index].LeftSeparator : null)
                     ?? _data.Separator?.ToString()
                     ?? " ";
        _data.AddTransform(new MergeWithPreviousTransform(index, sep));
        RebuildColumns();
        await RefreshWindowAsync();
    }

    /// <summary>Splits one column by a literal character into N cells, where N is the
    /// maximum number of split parts seen across the column's current sample values
    /// (≥ 2). Subsequent rows split into the same N cells — under-arity rows pad with
    /// "", over-arity rows keep the overflow inside the last cell.</summary>
    public async Task SplitColumnByAsync(int index, char separator)
    {
        if (_data is null || index < 0 || index >= Columns.Count) return;
        int arity = 2;
        foreach (var s in Columns[index].SampleValues)
        {
            // Count separator occurrences cheaply (+1 for arity) without allocating the array.
            int count = 1;
            foreach (var ch in s) if (ch == separator) count++;
            if (count > arity) arity = count;
        }
        _data.AddTransform(new SplitByTransform(index, separator, arity));
        RebuildColumns();
        await RefreshWindowAsync();
    }

    /// <summary>Renames the column at <paramref name="index"/>. The new name is stored
    /// as a <see cref="RenameColumnTransform"/> so it survives subsequent re-derivations
    /// from the raw header tokens and round-trips through ribbon pinning.</summary>
    public async Task RenameColumnAsync(int index, string newHeader)
    {
        if (_data is null || index < 0 || index >= Columns.Count) return;
        if (string.IsNullOrWhiteSpace(newHeader)) return;
        _data.AddTransform(new RenameColumnTransform(index, newHeader.Trim()));
        RebuildColumns();
        await RefreshWindowAsync();
    }

    /// <summary>Opens the shell's text-input overlay pre-filled with the column's
    /// current header; on confirm, applies <see cref="RenameColumnAsync"/>.</summary>
    public void RequestRenameColumn(int index)
    {
        if (index < 0 || index >= Columns.Count) return;
        var current = Columns[index].Header;
        _shell.ShowPrompt(
            title:        "Rename column",
            label:        "New name:",
            initialValue: current,
            onConfirm:    async name => await RenameColumnAsync(index, name),
            onCancel:     () => { });
    }

    /// <summary>Re-classifies a column's display type — surfaced only for types whose
    /// parser succeeds on every non-empty sample value.</summary>
    public async Task EvaluateColumnAsAsync(int index, CsvDataType type)
    {
        if (_data is null || index < 0 || index >= Columns.Count) return;
        Columns[index].DisplayType = type;
        _data.AddTransform(new EvaluateAsTransform(index, type));
        await RefreshWindowAsync();
    }

    // ── Template commands ─────────────────────────────────────────────────

    [RelayCommand]
    private void OpenTemplateThis()
    {
        if (_data is null) return;
        TemplateName        = Path.GetFileNameWithoutExtension(FileName);
        TemplateFolderPath  = Path.GetDirectoryName(FilePath) ?? string.Empty;
        GlobPattern         = "*" + Path.GetExtension(FilePath);
        SelectedScope       = TemplateScope.Folder;
        IsTemplatePopupOpen = true;
    }

    [RelayCommand]
    private void CancelTemplateThis() => IsTemplatePopupOpen = false;

    private bool CanSaveTemplate() =>
        !string.IsNullOrWhiteSpace(TemplateName) &&
        (SelectedScope != TemplateScope.Glob || !string.IsNullOrWhiteSpace(GlobPattern));

    [RelayCommand(CanExecute = nameof(CanSaveTemplate))]
    private void SaveTemplate()
    {
        if (_data is null) return;
        var folder = Path.GetDirectoryName(FilePath) ?? string.Empty;
        var tmpl   = TemplateMatcher.Capture(TemplateName, SelectedScope, folder, GlobPattern.Trim(), _data);
        _templates.Templates.Add(tmpl);
        _shell.SaveFeatureConfig(_templates);
        IsTemplatePopupOpen = false;
        _shell.ShowNotification($"Saved template '{tmpl.Name}'.");
    }

    /// <summary>Toolbar toggle for the Apply-Template side panel.</summary>
    [RelayCommand]
    private void OpenTemplatePanel()
    {
        if (IsTemplatePanelOpen) { IsTemplatePanelOpen = false; return; }
        IsChooseMode = false;
        RebuildPanelTemplates();
        IsTemplatePanelOpen = true;
    }

    [RelayCommand]
    private void CloseTemplatePanel() => IsTemplatePanelOpen = false;

    [RelayCommand]
    private async Task ApplyTemplate(TemplateItemViewModel? item)
    {
        if (item is null) return;
        if (!item.IsCompatible)
        {
            bool ok = await _shell.ConfirmAsync(
                "Apply template?",
                "This template was built from a differently-shaped file and may not line up. Apply anyway?");
            if (!ok) return;
        }
        await ApplyTemplateChainAsync(item.Template);
        _pendingChoices.Clear();
        IsChooseMode        = false;
        IsTemplatePanelOpen = false;
    }

    [RelayCommand]
    private void DeleteTemplate(TemplateItemViewModel? item)
    {
        if (item is null) return;
        _templates.Templates.RemoveAll(t => t.Id == item.Template.Id);
        _pendingChoices.RemoveAll(t => t.Id == item.Template.Id);
        _shell.SaveFeatureConfig(_templates);
        RebuildPanelTemplates();
    }

    /// <summary>Replaces the live transform chain with the template's — i.e. loads the file with a
    /// predefined shape — then rebuilds columns and the visible window.</summary>
    private async Task ApplyTemplateChainAsync(TabularTemplate t)
    {
        if (_data is null) return;
        _data.ClearTransforms();
        foreach (var tr in TransformSerializer.Deserialize(t.TransformsJson))
            _data.AddTransform(tr);
        RebuildColumns();
        await RefreshWindowAsync();
    }

    private void RebuildPanelTemplates()
    {
        PanelTemplates.Clear();
        var src = IsChooseMode ? (IEnumerable<TabularTemplate>)_pendingChoices : _templates.Templates;
        foreach (var t in src)
        {
            bool compat = _data is not null &&
                          TemplateMatcher.IsCompatible(t, _data.RawShape, _data.RawHeaderCells);
            if (!IsChooseMode && ShowOnlyCompatible && !compat) continue;
            PanelTemplates.Add(new TemplateItemViewModel(t, compat));
        }
    }

    // ── Row selection (Excel-style) ───────────────────────────────────────

    public void HandleRowClick(int absoluteIndex, bool ctrl, bool shift)
    {
        if (shift && _selectionAnchorRow >= 0)
        {
            int lo = System.Math.Min(_selectionAnchorRow, absoluteIndex);
            int hi = System.Math.Max(_selectionAnchorRow, absoluteIndex);
            SelectedRowIndices.Clear();
            for (int r = lo; r <= hi; r++) SelectedRowIndices.Add(r);
        }
        else if (ctrl)
        {
            if (!SelectedRowIndices.Remove(absoluteIndex)) SelectedRowIndices.Add(absoluteIndex);
            _selectionAnchorRow = absoluteIndex;
        }
        else
        {
            SelectedRowIndices.Clear();
            SelectedRowIndices.Add(absoluteIndex);
            _selectionAnchorRow = absoluteIndex;
        }
        ApplyRowSelectionToWindow();
    }

    private void ApplyRowSelectionToWindow()
    {
        var set = new HashSet<int>(SelectedRowIndices);
        foreach (var row in Window) row.IsSelected = set.Contains(row.AbsoluteIndex);
    }

    /// <summary>Background row counter periodic callback. Only updates KnownRowCount if
    /// the new value isn't smaller than the currently observed one — so a slow file scan
    /// that's still in progress doesn't undo growth we already learned from the reader.</summary>
    private void GrowKnownRowCount(int value)
    {
        if (KnownRowCount is null || value > KnownRowCount.Value)
            KnownRowCount = value;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void BuildBreadcrumb()
    {
        PathBreadcrumb.Clear();
        if (string.IsNullOrEmpty(FilePath)) return;
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (string.IsNullOrEmpty(dir)) return;
            var parts = dir.TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar);
            string acc = string.Empty;
            for (int i = 0; i < parts.Length; i++)
            {
                acc = i == 0 ? parts[i] + Path.DirectorySeparatorChar : Path.Combine(acc, parts[i]);
                PathBreadcrumb.Add(new BreadcrumbStep(parts[i], acc));
            }
            PathBreadcrumb.Add(new BreadcrumbStep(Path.GetFileName(FilePath), FilePath, IsFile: true));
        }
        catch { /* best effort */ }
    }

    public void OpenBreadcrumb(BreadcrumbStep step)
    {
        if (step.IsFile) return;
        _shell.OpenTab("FileSystem", new Dictionary<string, string>
        {
            ["mode"]  = "path",
            ["path"]  = step.FullPath,
            ["label"] = step.Label,
        });
    }

    public void Dispose()
    {
        _windowCts.Cancel();
        _countCts?.Cancel();
        _source?.Dispose();
    }
}

public sealed record BreadcrumbStep(string Label, string FullPath, bool IsFile = false);
