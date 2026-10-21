using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Pdf.Editing;
using Nexaflow.Features.Pdf.Rendering;
using Nexaflow.Visuals.Common.Behaviors;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Common.Previews;

namespace Nexaflow.Features.Pdf.ViewModels;

/// <summary>
/// The Pages tab: a thumbnail of every page in the order the document now has, the page being read marked, and the
/// page edits — pages dragged or moved into a new place, deleted, a picture or another PDF inserted between two — each
/// staged on the document until it is saved.
/// </summary>
public sealed partial class PdfPagesPanel : ObservableObject, IDisposable
{
    private readonly PdfDocumentSession _session;
    private readonly IShellServices _shell;
    private readonly Func<int, bool> _goToPage;
    private readonly PdfThumbnails _thumbnails;
    private readonly CancellationTokenSource _lifetime = new();

    internal PdfPagesPanel(PdfDocumentSession session, IShellServices shell, Func<int, bool> goToPage, Func<PdfPageSource?> source)
    {
        _session = session;
        _shell = shell;
        _goToPage = goToPage;
        _thumbnails = new PdfThumbnails(source);
    }

    /// <summary>Every page, in order.</summary>
    public ObservableCollection<PdfPageThumbnail> Pages { get; } = [];

    /// <summary>
    /// Whether the pages can be edited — not in a document whose XFA form lays the whole document out itself.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotEditableMessage))]
    [NotifyCanExecuteChangedFor(nameof(ReorderCommand), nameof(DeleteSelectedCommand), nameof(InsertImagesAtCommand),
        nameof(InsertPdfsAtCommand), nameof(MoveSelectionCommand))]
    private bool _canEdit;

    /// <summary>Why the pages cannot be edited, where they cannot.</summary>
    public string NotEditableMessage => CanEdit ? string.Empty : Str.Get("Pdf.Pages.NotEditableXfa");

    /// <summary>The pages selected, in order.</summary>
    public int SelectedCount => Pages.Count(page => page.IsSelected);

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
    }
}
