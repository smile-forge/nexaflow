using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Pdf.Document;
using Nexaflow.Features.Pdf.Editing;
using Nexaflow.Features.Pdf.Rendering;
using Nexaflow.Visuals.Common.Behaviors;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Common.Previews;

namespace Nexaflow.Features.Pdf.ViewModels;

/// <summary>
/// The Pages tab: a thumbnail of every page in the order the document now has, the page being read marked, and the
/// page edits — pages dragged or moved into a new place, deleted, split into the pages printed on them or merged several
/// to a sheet, set on paper of another size, a picture or another PDF inserted between two — each staged on the
/// document until it is saved.
/// </summary>
public sealed partial class PdfPagesPanel : ObservableObject, IDisposable
{
    private readonly PdfDocumentSession _session;
    private readonly IShellServices _shell;
    private readonly Func<int, bool> _goToPage;
    private readonly PdfThumbnails _thumbnails;
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>The parts a page may be split into, and the pages a sheet may be merged from.</summary>
    private static readonly int[] SheetParts = [2, 3, 4];

    /// <summary>Every line a split may cut a page along — what a page's picture is read for, once, whatever is chosen.</summary>
    private static readonly IReadOnlyList<PdfPageCut> EveryCut =
        SheetParts.SelectMany(count => PdfPagePart.Cuts(count, across: true).Concat(PdfPagePart.Cuts(count, across: false))).Distinct().ToList();

    /// <summary>The longer side of the picture a page's cuts are read from, in pixels.</summary>
    private const int InkLongest = 480;

    private const double MillimetresPerPoint = 25.4 / 72;
    private const double InchesPerPoint = 1.0 / 72;

    private readonly Func<PdfPageSource?> _source;

    /// <summary>Which of <see cref="EveryCut"/> each page's artwork crosses, by the page it is — read while a split is set up.</summary>
    private readonly Dictionary<PdfPageId, IReadOnlyDictionary<PdfPageCut, bool>> _ink = new();

    private CancellationTokenSource? _inspecting;

    internal PdfPagesPanel(PdfDocumentSession session, IShellServices shell, Func<int, bool> goToPage, Func<PdfPageSource?> source)
    {
        _session = session;
        _shell = shell;
        _goToPage = goToPage;
        _thumbnails = new PdfThumbnails(source);
        _source = source;

        SheetEdits =
        [
            new(PdfSheetEdit.Split, Str.Get("Pdf.Sheet.Split")),
            new(PdfSheetEdit.Merge, Str.Get("Pdf.Sheet.Merge")),
            new(PdfSheetEdit.Size, Str.Get("Pdf.Sheet.Size")),
        ];
        SplitCounts = SheetParts.Select(count => new PdfSplitChoice<int>(count, Str.Format("Pdf.Split.PartsFormat", count))).ToList();
        SplitDirections =
        [
            new(PdfSplitDirection.Auto, Str.Get("Pdf.Split.Direction.Auto")),
            new(PdfSplitDirection.Across, Str.Get("Pdf.Split.Direction.Across")),
            new(PdfSplitDirection.Down, Str.Get("Pdf.Split.Direction.Down")),
        ];
        MergeCounts = SheetParts.Select(count => new PdfSplitChoice<int>(count, Str.Format("Pdf.Merge.PartsFormat", count))).ToList();
        MergeDirections =
        [
            new(PdfSplitDirection.Auto, Str.Get("Pdf.Merge.Direction.Auto")),
            new(PdfSplitDirection.Across, Str.Get("Pdf.Split.Direction.Across")),
            new(PdfSplitDirection.Down, Str.Get("Pdf.Split.Direction.Down")),
        ];
        MergePapers =
        [
            new(PdfMergePaper.None, Str.Get("Pdf.Merge.Paper.None")),
            new(PdfMergePaper.ThePagesOwn, Str.Get("Pdf.Merge.Paper.ThePagesOwn")),
            .. PdfPaper.Named.Select(paper => new PdfSplitChoice<PdfMergePaper>(PdfMergePaper.Of(paper.Size),
                Str.Format("Pdf.Merge.Paper.OnFormat", Str.Get($"Pdf.Paper.{paper.Key}")))),
        ];
        SizePapers =
        [
            .. PdfPaper.Named.Select(paper => new PdfSplitChoice<PdfPaper?>(paper, Str.Get($"Pdf.Paper.{paper.Key}"))),
            new(null, Str.Get("Pdf.Size.Custom")),
        ];
        SizeUnits =
        [
            new(MillimetresPerPoint, Str.Get("Pdf.Size.Millimetres")),
            new(InchesPerPoint, Str.Get("Pdf.Size.Inches")),
        ];
        SizeOrientations =
        [
            new(PdfOrientation.Auto, Str.Get("Pdf.Size.Orientation.Auto")),
            new(PdfOrientation.Portrait, Str.Get("Pdf.Size.Orientation.Portrait")),
            new(PdfOrientation.Landscape, Str.Get("Pdf.Size.Orientation.Landscape")),
        ];
        SizeFits =
        [
            new(PdfContentFit.Scale, Str.Get("Pdf.Size.Fit.Scale")),
            new(PdfContentFit.Keep, Str.Get("Pdf.Size.Fit.Keep")),
        ];

        _sheetEdit = SheetEdits[0];
        _splitCount = SplitCounts[0];
        _splitDirection = SplitDirections[0];
        _mergeCount = MergeCounts[0];
        _mergeDirection = MergeDirections[0];
        _mergePaper = MergePapers[0];
        _sizePaper = SizePapers[1];
        _sizeUnit = SizeUnits[0];
        _sizeOrientation = SizeOrientations[0];
        _sizeFit = SizeFits[0];
        _customWidth = Measure(PdfPaper.A4.Size.Width);
        _customHeight = Measure(PdfPaper.A4.Size.Height);
    }

    /// <summary>Every page, in order.</summary>
    public ObservableCollection<PdfPageThumbnail> Pages { get; } = [];

    /// <summary>
    /// Whether the pages can be edited — not in a document whose XFA form lays the whole document out itself.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotEditableMessage))]
    [NotifyCanExecuteChangedFor(nameof(ReorderCommand), nameof(DeleteSelectedCommand), nameof(InsertImagesAtCommand),
        nameof(InsertPdfsAtCommand), nameof(MoveSelectionCommand), nameof(BeginSplitCommand), nameof(BeginMergeCommand),
        nameof(BeginResizeCommand), nameof(BeginSheetEditCommand), nameof(ApplySheetEditCommand))]
    private bool _canEdit;

    /// <summary>Why the pages cannot be edited, where they cannot.</summary>
    public string NotEditableMessage => CanEdit ? string.Empty : Str.Get("Pdf.Pages.NotEditableXfa");

    /// <summary>The pages selected, in order.</summary>
    public int SelectedCount => Pages.Count(page => page.IsSelected);

    // ── Sheets: split, merged, set on paper ───────────────────────────────

    /// <summary>
    /// Whether a split, a merge or a new page size is being set up, in the bar above the pages — a split's cut lines drawn
    /// on the pages it would cut.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSplitShown), nameof(IsMergeShown), nameof(IsSizeShown), nameof(MergeSummary))]
    [NotifyCanExecuteChangedFor(nameof(BeginSplitCommand), nameof(BeginMergeCommand), nameof(BeginResizeCommand),
        nameof(BeginSheetEditCommand), nameof(ApplySheetEditCommand))]
    private bool _isEditingSheets;

    /// <summary>
    /// Whether what is set up is for every page — the bar opened from the toolbar, the edit chosen in it — rather than for
    /// the pages selected.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SheetHeading), nameof(MergeSummary))]
    [NotifyCanExecuteChangedFor(nameof(ApplySheetEditCommand))]
    private bool _appliesToAll;

    /// <summary>The edits the bar sets up.</summary>
    public IReadOnlyList<PdfSplitChoice<PdfSheetEdit>> SheetEdits { get; }

    /// <summary>Which edit the bar sets up.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSplitShown), nameof(IsMergeShown), nameof(IsSizeShown), nameof(SheetHeading), nameof(ApplyLabel), nameof(MergeSummary))]
    [NotifyCanExecuteChangedFor(nameof(ApplySheetEditCommand))]
    private PdfSplitChoice<PdfSheetEdit> _sheetEdit;

    public bool IsSplitShown => IsEditingSheets && SheetEdit.Value == PdfSheetEdit.Split;

    public bool IsMergeShown => IsEditingSheets && SheetEdit.Value == PdfSheetEdit.Merge;

    public bool IsSizeShown => IsEditingSheets && SheetEdit.Value == PdfSheetEdit.Size;

    /// <summary>What the bar is headed: the edit, and the pages it is for.</summary>
    public string SheetHeading => Str.Get($"Pdf.Sheet.Heading.{SheetEdit.Value}{(AppliesToAll ? "All" : "Selected")}");

    /// <summary>What the bar's button that does the edit says.</summary>
    public string ApplyLabel => Str.Get($"Pdf.Sheet.Apply.{SheetEdit.Value}");

    /// <summary>How many parts a page may be split into.</summary>
    public IReadOnlyList<PdfSplitChoice<int>> SplitCounts { get; }

    /// <summary>Which ways a page may be cut.</summary>
    public IReadOnlyList<PdfSplitChoice<PdfSplitDirection>> SplitDirections { get; }

    /// <summary>How many parts each page is split into.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUnfoldBooklet))]
    private PdfSplitChoice<int> _splitCount;

    /// <summary>Which way each page is cut.</summary>
    [ObservableProperty] private PdfSplitChoice<PdfSplitDirection> _splitDirection;

    /// <summary>
    /// Whether the pages are a folded booklet's sheets, their halves put into reading order rather than left in place —
    /// for halves only.
    /// </summary>
    [ObservableProperty] private bool _splitBooklet;

    /// <summary>Whether a booklet can be unfolded from the parts chosen: only from halves.</summary>
    public bool CanUnfoldBooklet => SplitCount.Value == 2;

    /// <summary>Which pages' artwork a cut would cross, said where there are any.</summary>
    [ObservableProperty] private string _splitWarning = string.Empty;

    /// <summary>Whether some of the pages to be split are still being checked for artwork across their cuts.</summary>
    [ObservableProperty] private bool _isCheckingCuts;

    /// <summary>The checking of the cuts last set going: complete once every page with cuts drawn has been checked.</summary>
    internal Task CutsInspected { get; private set; } = Task.CompletedTask;

    /// <summary>How many pages may be merged to a sheet.</summary>
    public IReadOnlyList<PdfSplitChoice<int>> MergeCounts { get; }

    /// <summary>Which ways pages may be put together on a sheet.</summary>
    public IReadOnlyList<PdfSplitChoice<PdfSplitDirection>> MergeDirections { get; }

    /// <summary>What a sheet of pages merged may be made as large as.</summary>
    public IReadOnlyList<PdfSplitChoice<PdfMergePaper>> MergePapers { get; }

    /// <summary>How many pages are merged to each sheet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFoldBooklet), nameof(MergeSummary))]
    [NotifyCanExecuteChangedFor(nameof(ApplySheetEditCommand))]
    private PdfSplitChoice<int> _mergeCount;

    /// <summary>Which way the pages are put together on each sheet.</summary>
    [ObservableProperty] private PdfSplitChoice<PdfSplitDirection> _mergeDirection;

    /// <summary>What each sheet is made as large as.</summary>
    [ObservableProperty] private PdfSplitChoice<PdfMergePaper> _mergePaper;

    /// <summary>
    /// Whether the pages, in reading order, are folded into a booklet's sheets for printing — for two to a sheet only.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MergeSummary))]
    private bool _mergeBooklet;

    /// <summary>Whether a booklet can be folded from the pages to a sheet chosen: only from two.</summary>
    public bool CanFoldBooklet => MergeCount.Value == 2;

    /// <summary>How many pages go onto how many sheets.</summary>
    public string MergeSummary
    {
        get
        {
            if (!IsMergeShown) return string.Empty;
            var pages = Targets().Count;
            var sheets = MergeBooklet && CanFoldBooklet ? (pages + 3) / 4 * 2 : (pages + MergeCount.Value - 1) / MergeCount.Value;
            return Str.Format("Pdf.Merge.SummaryFormat", pages, sheets);
        }
    }

    /// <summary>The papers a page may be set on, and one of its own size.</summary>
    public IReadOnlyList<PdfSplitChoice<PdfPaper?>> SizePapers { get; }

    /// <summary>What a size of its own is given in: points to the unit, each.</summary>
    public IReadOnlyList<PdfSplitChoice<double>> SizeUnits { get; }

    /// <summary>Which ways up a page may be set.</summary>
    public IReadOnlyList<PdfSplitChoice<PdfOrientation>> SizeOrientations { get; }

    /// <summary>What may happen to a page's content on its new paper.</summary>
    public IReadOnlyList<PdfSplitChoice<PdfContentFit>> SizeFits { get; }

    /// <summary>The paper each page is set on: a named one, or one of its own size (null).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomSize))]
    [NotifyCanExecuteChangedFor(nameof(ApplySheetEditCommand))]
    private PdfSplitChoice<PdfPaper?> _sizePaper;

    /// <summary>Whether the paper is one of its own size, given below.</summary>
    public bool IsCustomSize => SizePaper.Value is null;

    /// <summary>The unit a size of its own is given in.</summary>
    [ObservableProperty] private PdfSplitChoice<double> _sizeUnit;

    /// <summary>A size of its own: how wide, in <see cref="SizeUnit"/>.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplySheetEditCommand))]
    private string _customWidth;

    /// <summary>A size of its own: how tall, in <see cref="SizeUnit"/>.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplySheetEditCommand))]
    private string _customHeight;

    /// <summary>Which way up each page is set.</summary>
    [ObservableProperty] private PdfSplitChoice<PdfOrientation> _sizeOrientation;

    /// <summary>What happens to each page's content on its new paper.</summary>
    [ObservableProperty] private PdfSplitChoice<PdfContentFit> _sizeFit;

    /// <summary>
    /// Brings the thumbnails into line with the pages: those gone removed, the rest moved, the new added — each kept
    /// thumbnail the same object, so its picture and selection go where its page goes.
    /// </summary>
    internal void Show(PdfPageArrangement arrangement)
    {
        var wanted = arrangement.Pages.Select(page => page.Id).ToHashSet();
        for (var i = Pages.Count - 1; i >= 0; i--)
        {
            if (wanted.Contains(Pages[i].Id)) continue;
            _thumbnails.Cancel(Pages[i]);
            Pages[i].PropertyChanged -= OnThumbnailChanged;
            Pages.RemoveAt(i);
        }

        var known = Pages.ToDictionary(page => page.Id);
        for (var i = 0; i < arrangement.Count; i++)
        {
            var id = arrangement[i].Id;
            if (i < Pages.Count && Pages[i].Id == id) continue;

            if (known.TryGetValue(id, out var existing))
            {
                Pages.Move(Pages.IndexOf(existing), i);
                continue;
            }

            var added = new PdfPageThumbnail(id);
            added.PropertyChanged += OnThumbnailChanged;
            Pages.Insert(i, added);
        }

        for (var i = 0; i < Pages.Count; i++)
        {
            Pages[i].Number = i + 1;
            Pages[i].Fit(_session.PageSize(i));
        }

        Selected();
    }

    /// <summary>Marks page <paramref name="number"/> (from 1) as the one being read.</summary>
    internal void MarkCurrent(int number)
    {
        for (var i = 0; i < Pages.Count; i++)
            Pages[i].IsCurrent = i == number - 1;
    }

    /// <summary>A thumbnail's row came into view, its picture wanted <paramref name="width"/> pixels wide.</summary>
    public void Realized(PdfPageThumbnail page, int width) => _thumbnails.Request(page, width);

    /// <summary>A thumbnail's row left view.</summary>
    public void Unrealized(PdfPageThumbnail page) => _thumbnails.Cancel(page);

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Reorder(ReorderRequest request)
    {
        var indices = request.Items.OfType<PdfPageThumbnail>().Select(page => Pages.IndexOf(page)).Where(index => index >= 0).ToList();
        if (indices.Count > 0) Edit(() => _session.Move(indices, request.ToIndex));
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private void DeleteSelected()
    {
        var indices = SelectedIndices();
        if (indices.Count > 0) Edit(() => _session.Delete(indices));
    }

    private bool CanDeleteSelected() => CanEdit && SelectedCount > 0 && SelectedCount < Pages.Count;

    /// <summary>Pictures picked, each inserted into gap <paramref name="gap"/> as a page.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task InsertImagesAt(int gap) => InsertAsync(gap, pictures: true);

    /// <summary>PDFs picked, every page of each inserted into gap <paramref name="gap"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task InsertPdfsAt(int gap) => InsertAsync(gap, pictures: false);

    /// <summary>The selected pages moved one place up (<paramref name="step"/> -1) or down (1), together.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void MoveSelection(int step)
    {
        var indices = SelectedIndices();
        if (indices.Count == 0) return;

        var gap = step < 0 ? indices[0] - 1 : indices[^1] + 2;
        if (gap < 0 || gap > Pages.Count) return;
        Edit(() => _session.Move(indices, gap));
    }

    /// <summary>Sets up a split of the selected pages: their cut lines drawn, and checked against their artwork.</summary>
    [RelayCommand(CanExecute = nameof(CanBeginSplit))]
    private void BeginSplit() => OpenSheetEdit(PdfSheetEdit.Split, all: false);

    private bool CanBeginSplit() => CanEdit && !IsEditingSheets && SelectedCount > 0;

    /// <summary>Sets up a merge of the selected pages onto sheets.</summary>
    [RelayCommand(CanExecute = nameof(CanBeginMerge))]
    private void BeginMerge() => OpenSheetEdit(PdfSheetEdit.Merge, all: false);

    private bool CanBeginMerge() => CanEdit && !IsEditingSheets && SelectedCount >= 2;

    /// <summary>Sets up setting the selected pages on paper of another size.</summary>
    [RelayCommand(CanExecute = nameof(CanBeginSplit))]
    private void BeginResize() => OpenSheetEdit(PdfSheetEdit.Size, all: false);

    /// <summary>Sets up a split, a merge or a new size for every page, the edit chosen in the bar.</summary>
    [RelayCommand(CanExecute = nameof(CanBeginSheetEdit))]
    private void BeginSheetEdit() => OpenSheetEdit(SheetEdit.Value, all: true);

    private bool CanBeginSheetEdit() => CanEdit && !IsEditingSheets && Pages.Count > 0;

    /// <summary>What is set up in the bar done, as one edit.</summary>
    [RelayCommand(CanExecute = nameof(CanApplySheetEdit))]
    private void ApplySheetEdit()
    {
        var indices = Targets();
        switch (SheetEdit.Value)
        {
            case PdfSheetEdit.Split:
                var (parts, cut, unfold) = (SplitCount.Value, SplitDirection.Value, SplitBooklet && CanUnfoldBooklet);
                EndSheetEdit();
                Edit(() => _session.Split(indices, parts, cut, unfold));
                break;
            case PdfSheetEdit.Merge:
                var (pages, put, fold, paper) = (MergeCount.Value, MergeDirection.Value, MergeBooklet && CanFoldBooklet, MergePaper.Value);
                EndSheetEdit();
                Edit(() => _session.Merge(indices, pages, put, fold, paper));
                break;
            default:
                if (Sizing() is not { } sizing) return;
                EndSheetEdit();
                Edit(() => _session.Resize(indices, sizing));
                break;
        }
    }

    private bool CanApplySheetEdit() => CanEdit && IsEditingSheets && SheetEdit.Value switch
    {
        PdfSheetEdit.Merge => Targets().Count >= 2,
        PdfSheetEdit.Size  => Targets().Count > 0 && Sizing() is not null,
        _                  => Targets().Count > 0,
    };

    [RelayCommand]
    private void CancelSheetEdit() => EndSheetEdit();

    partial void OnSheetEditChanged(PdfSplitChoice<PdfSheetEdit> value) => ShowCuts();

    partial void OnSplitCountChanged(PdfSplitChoice<int> value)
    {
        if (!CanUnfoldBooklet) SplitBooklet = false;
        ShowCuts();
    }

    partial void OnSplitDirectionChanged(PdfSplitChoice<PdfSplitDirection> value) => ShowCuts();

    partial void OnMergeCountChanged(PdfSplitChoice<int> value)
    {
        if (!CanFoldBooklet) MergeBooklet = false;
    }

    /// <summary>A size of its own keeps its size when its unit changes: the figures given are said again in the new unit.</summary>
    partial void OnSizeUnitChanged(PdfSplitChoice<double>? oldValue, PdfSplitChoice<double> newValue)
    {
        if (oldValue is null) return;
        if (Points(CustomWidth, oldValue.Value) is { } width) CustomWidth = Measure(width);
        if (Points(CustomHeight, oldValue.Value) is { } height) CustomHeight = Measure(height);
    }

    private void OpenSheetEdit(PdfSheetEdit edit, bool all)
    {
        AppliesToAll = all;
        SheetEdit = SheetEdits.First(choice => choice.Value == edit);
        IsEditingSheets = true;
        ShowCuts();
    }

    private void EndSheetEdit()
    {
        IsEditingSheets = false;
        ShowCuts();
        _ink.Clear();
    }

    /// <summary>The paper each page is to be set on, as chosen; null while a size of its own is not a size.</summary>
    private PdfPageSizing? Sizing()
    {
        var paper = SizePaper.Value?.Size
                    ?? (Points(CustomWidth, SizeUnit.Value) is { } width && Points(CustomHeight, SizeUnit.Value) is { } height
                        ? new System.Windows.Size(width, height)
                        : (System.Windows.Size?)null);
        return paper is { } size ? new PdfPageSizing(size, SizeOrientation.Value, SizeFit.Value) : null;
    }

    /// <summary><paramref name="text"/>, a length in units <paramref name="perPoint"/> to the point, in points; null when it is no length.</summary>
    private static double? Points(string text, double perPoint) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var value)
        && value > 0 && double.IsFinite(value)
            ? value / perPoint
            : null;

    /// <summary><paramref name="points"/> said in the unit chosen, to a tenth.</summary>
    private string Measure(double points) =>
        Math.Round(points * SizeUnit.Value, 1).ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>The pages the bar is for, in order: every one, or those selected.</summary>
    private List<int> Targets() => AppliesToAll ? Enumerable.Range(0, Pages.Count).ToList() : SelectedIndices();

    private bool IsTarget(PdfPageThumbnail page) => AppliesToAll || page.IsSelected;

    /// <summary>
    /// Draws the lines the pages would be cut along while a split is set up — on none once it is not — each marked where
    /// its page's artwork crosses it, and has the pages not yet checked checked.
    /// </summary>
    private void ShowCuts()
    {
        _inspecting?.Cancel();
        _inspecting = null;

        var unread = new List<PdfPageThumbnail>();
        for (var i = 0; i < Pages.Count; i++)
        {
            var page = Pages[i];
            if (!IsSplitShown || !IsTarget(page))
            {
                if (page.Cuts.Count > 0) page.Cuts = [];
                continue;
            }

            ShowCuts(page, i);
            if (!_ink.ContainsKey(page.Id)) unread.Add(page);
        }

        if (unread.Count > 0)
        {
            _inspecting = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            CutsInspected = InspectAsync(unread, _inspecting.Token);
        }

        NoteCuts();
    }

    private void ShowCuts(PdfPageThumbnail page, int index) =>
        page.ShowCuts(PdfPagePart.Cuts(SplitCount.Value, _session.SplitsAcross(index, SplitDirection.Value)),
            cut => _ink.TryGetValue(page.Id, out var crossed) && crossed.TryGetValue(cut, out var crosses) && crosses);

    /// <summary>
    /// Reads which cuts each of <paramref name="pages"/> has artwork across, from a picture of it, one page at a time.
    /// </summary>
    private async Task InspectAsync(IReadOnlyList<PdfPageThumbnail> pages, CancellationToken ct)
    {
        foreach (var page in pages)
        {
            if (_source() is not { } source) return;
            var index = source.Arrangement.IndexOf(page.Id);
            if (index < 0) continue;

            IReadOnlyDictionary<PdfPageCut, bool> crossed;
            try
            {
                var size = _session.PageSize(index);
                var width = Math.Max(1, (int)Math.Round(InkLongest * size.Width / Math.Max(size.Width, size.Height)));
                crossed = await source.PaintedAsync(index, width, ct) is { } picture
                    ? await Task.Run(() => Crossed(picture), ct)
                    : new Dictionary<PdfPageCut, bool>();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                crossed = new Dictionary<PdfPageCut, bool>();   // the page's document can no longer be read
            }

            _ink[page.Id] = crossed;
            if (IsSplitShown && IsTarget(page) && Pages.IndexOf(page) is var at and >= 0) ShowCuts(page, at);
            NoteCuts();
        }
    }

    private static IReadOnlyDictionary<PdfPageCut, bool> Crossed(System.Windows.Media.Imaging.BitmapSource picture)
    {
        var ink = PdfInk.Of(picture);
        return EveryCut.ToDictionary(cut => cut, ink.Crosses);
    }

    /// <summary>Says which pages' artwork a cut crosses, and whether any are still being checked.</summary>
    private void NoteCuts()
    {
        var cut = IsSplitShown ? Pages.Where(page => page.Cuts.Count > 0).ToList() : [];
        var crossed = cut.Where(page => page.Cuts.Any(line => line.CrossesInk)).Select(page => page.Number).ToList();
        SplitWarning = crossed.Count > 0 ? Str.Format("Pdf.Split.CrossesFormat", string.Join(", ", crossed)) : string.Empty;
        IsCheckingCuts = cut.Any(page => !_ink.ContainsKey(page.Id));
    }

    [RelayCommand]
    private void GoTo(PdfPageThumbnail? page)
    {
        if (page is not null && Pages.IndexOf(page) is var index and >= 0) _goToPage(index + 1);
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _thumbnails.Dispose();
    }

    private async Task InsertAsync(int gap, bool pictures)
    {
        var folder = Path.GetDirectoryName(_session.Base.Path);
        var paths = await _shell.PickOpenFilesAsync(pictures ? DecodablePictures.Extensions : [".pdf"], folder);
        if (paths.Count == 0) return;

        try
        {
            if (pictures)
                await _session.InsertImagesAsync(gap, paths, _lifetime.Token);
            else
                await _session.InsertPdfsAsync(gap, paths, _lifetime.Token);
        }
        catch (PdfEditRefusedException refused)
        {
            _shell.ShowError(Refusal(refused));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Edit(Action edit)
    {
        try
        {
            edit();
        }
        catch (PdfEditRefusedException refused)
        {
            _shell.ShowError(Refusal(refused));
        }
    }

    /// <summary>Why an edit was refused, as the reader is told.</summary>
    internal static string Refusal(PdfEditRefusedException refused)
    {
        var name = Path.GetFileName(refused.Path);
        return refused.Reason switch
        {
            PdfEditRefusal.XfaForm     => Str.Format("Pdf.Edit.XfaFormat", name),
            PdfEditRefusal.NotAPicture => Str.Format("Pdf.Edit.NotAPictureFormat", name),
            PdfEditRefusal.InUse       => Str.Format("Pdf.Edit.InUseFormat", name),
            _                          => Str.Format("Pdf.Edit.UnreadableFormat", name),
        };
    }

    private List<int> SelectedIndices() =>
        Pages.Select((page, index) => (page, index)).Where(entry => entry.page.IsSelected).Select(entry => entry.index).ToList();

    private void OnThumbnailChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PdfPageThumbnail.IsSelected)) Selected();
    }

    private void Selected()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(MergeSummary));
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        BeginSplitCommand.NotifyCanExecuteChanged();
        BeginMergeCommand.NotifyCanExecuteChanged();
        BeginResizeCommand.NotifyCanExecuteChanged();
        BeginSheetEditCommand.NotifyCanExecuteChanged();
        ApplySheetEditCommand.NotifyCanExecuteChanged();
        if (IsSplitShown) ShowCuts();
    }
}
