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
/// page edits — pages dragged or moved into a new place, deleted, split into the pages printed on them, a picture or
/// another PDF inserted between two — each staged on the document until it is saved.
/// </summary>
public sealed partial class PdfPagesPanel : ObservableObject, IDisposable
{
    private readonly PdfDocumentSession _session;
    private readonly IShellServices _shell;
    private readonly Func<int, bool> _goToPage;
    private readonly PdfThumbnails _thumbnails;
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>The parts a page may be split into.</summary>
    private static readonly int[] SplitParts = [2, 3, 4];

    /// <summary>Every line a split may cut a page along — what a page's picture is read for, once, whatever is chosen.</summary>
    private static readonly IReadOnlyList<PdfPageCut> EveryCut =
        SplitParts.SelectMany(count => PdfPagePart.Cuts(count, across: true).Concat(PdfPagePart.Cuts(count, across: false))).Distinct().ToList();

    /// <summary>The longer side of the picture a page's cuts are read from, in pixels.</summary>
    private const int InkLongest = 480;

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
        SplitCounts = SplitParts.Select(count => new PdfSplitChoice<int>(count, Str.Format("Pdf.Split.PartsFormat", count))).ToList();
        SplitDirections =
        [
            new(PdfSplitDirection.Auto, Str.Get("Pdf.Split.Direction.Auto")),
            new(PdfSplitDirection.Across, Str.Get("Pdf.Split.Direction.Across")),
            new(PdfSplitDirection.Down, Str.Get("Pdf.Split.Direction.Down")),
        ];
        _splitCount = SplitCounts[0];
        _splitDirection = SplitDirections[0];
    }

    /// <summary>Every page, in order.</summary>
    public ObservableCollection<PdfPageThumbnail> Pages { get; } = [];

    /// <summary>
    /// Whether the pages can be edited — not in a document whose XFA form lays the whole document out itself.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotEditableMessage))]
    [NotifyCanExecuteChangedFor(nameof(ReorderCommand), nameof(DeleteSelectedCommand), nameof(InsertImagesAtCommand),
        nameof(InsertPdfsAtCommand), nameof(MoveSelectionCommand), nameof(BeginSplitCommand), nameof(ApplySplitCommand))]
    private bool _canEdit;

    /// <summary>Why the pages cannot be edited, where they cannot.</summary>
    public string NotEditableMessage => CanEdit ? string.Empty : Str.Get("Pdf.Pages.NotEditableXfa");

    /// <summary>The pages selected, in order.</summary>
    public int SelectedCount => Pages.Count(page => page.IsSelected);

    /// <summary>Whether a split of the selected pages is being set up, the lines they would be cut along drawn on them.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BeginSplitCommand), nameof(ApplySplitCommand))]
    private bool _isSplitting;

    /// <summary>How many parts a page may be split into.</summary>
    public IReadOnlyList<PdfSplitChoice<int>> SplitCounts { get; }

    /// <summary>Which ways a page may be cut.</summary>
    public IReadOnlyList<PdfSplitChoice<PdfSplitDirection>> SplitDirections { get; }

    /// <summary>How many parts each selected page is split into.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUnfoldBooklet))]
    private PdfSplitChoice<int> _splitCount;

    /// <summary>Which way each selected page is cut.</summary>
    [ObservableProperty] private PdfSplitChoice<PdfSplitDirection> _splitDirection;

    /// <summary>
    /// Whether the selected pages are a folded booklet's sheets, their halves put into reading order rather than left in
    /// place — for halves only.
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
    private void BeginSplit()
    {
        IsSplitting = true;
        ShowCuts();
    }

    private bool CanBeginSplit() => CanEdit && !IsSplitting && SelectedCount > 0;

    /// <summary>The selected pages split as set up, as one edit.</summary>
    [RelayCommand(CanExecute = nameof(CanApplySplit))]
    private void ApplySplit()
    {
        var indices = SelectedIndices();
        var (count, direction, booklet) = (SplitCount.Value, SplitDirection.Value, SplitBooklet && CanUnfoldBooklet);
        EndSplit();
        Edit(() => _session.Split(indices, count, direction, booklet));
    }

    private bool CanApplySplit() => CanEdit && IsSplitting && SelectedCount > 0;

    [RelayCommand]
    private void CancelSplit() => EndSplit();

    partial void OnSplitCountChanged(PdfSplitChoice<int> value)
    {
        if (!CanUnfoldBooklet) SplitBooklet = false;
        ShowCuts();
    }

    partial void OnSplitDirectionChanged(PdfSplitChoice<PdfSplitDirection> value) => ShowCuts();

    private void EndSplit()
    {
        IsSplitting = false;
        ShowCuts();
        _ink.Clear();
    }

    /// <summary>
    /// Draws the lines the selected pages would be cut along while a split is set up — on none once it is not — each marked
    /// where its page's artwork crosses it, and has the pages not yet checked checked.
    /// </summary>
    private void ShowCuts()
    {
        _inspecting?.Cancel();
        _inspecting = null;

        var unread = new List<PdfPageThumbnail>();
        for (var i = 0; i < Pages.Count; i++)
        {
            var page = Pages[i];
            if (!IsSplitting || !page.IsSelected)
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
            if (IsSplitting && page.IsSelected && Pages.IndexOf(page) is var at and >= 0) ShowCuts(page, at);
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
        var cut = IsSplitting ? Pages.Where(page => page.Cuts.Count > 0).ToList() : [];
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
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        BeginSplitCommand.NotifyCanExecuteChanged();
        ApplySplitCommand.NotifyCanExecuteChanged();
        if (IsSplitting) ShowCuts();
    }
}
