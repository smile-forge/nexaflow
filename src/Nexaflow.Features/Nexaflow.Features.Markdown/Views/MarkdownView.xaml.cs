using Nexaflow.Features.Common;
using Nexaflow.Features.Markdown.ViewModels;
using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nexaflow.Features.Markdown.Views;

public partial class MarkdownView : UserControl, IPageView
{
    public MarkdownViewModel ViewModel { get; }

    // ── Construction ──────────────────────────────────────────────────────

    public MarkdownView(MarkdownViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;

        // Resolve relative ![](img.png) images against the file's own folder.
        Editor.BaseDirectory = Path.GetDirectoryName(viewModel.FilePath);

        // Move focus to whichever surface the toggle just revealed, so typing works immediately.
        viewModel.PropertyChanged += OnViewModelChanged;

        // Search collaboration: the rendered surface (the inline editor) owns its own highlighting; the
        // source box's match is shown by selecting it. The VM decides which is active.
        viewModel.FindInRendered        = Editor.FindInRendered;
        viewModel.StepRendered          = Editor.StepSearch;
        viewModel.ClearRendered         = Editor.ClearSearch;
        viewModel.RenderedMarkPositions = Editor.SearchMarkPositions;
        viewModel.SourceSelectionRequested += SelectInSource;
        viewModel.PropertyChanged += OnSearchPropertyChanged;
        Unloaded += (_, _) =>
        {
            viewModel.SourceSelectionRequested -= SelectInSource;
            viewModel.PropertyChanged -= OnSearchPropertyChanged;
        };

        // Ctrl+S → save (fires in either mode — it's on the UserControl).
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (viewModel.SaveCommand.CanExecute(null))
                    viewModel.SaveCommand.Execute(null);
                e.Handled = true;
            }
        };

        Focusable = true;

        // Opened from a snaplink with a heading → scroll there once the document has rendered + laid out.
        if (viewModel.InitialHeading is { Count: > 0 } heading)
            Loaded += (_, _) => Dispatcher.BeginInvoke(
                () => Editor.ScrollToHeading(heading),
                System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MarkdownViewModel.SourceOnly)) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (ViewModel.SourceOnly) SourceBox.Focus();
            else                      Editor.Focus();
        });
    }

    // The rendered surface reports its match positions only after it has laid out, so read them on a queued
    // pass rather than the instant the count changes.
    private void OnSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MarkdownViewModel.MiniMapMarks)) return;
        MiniMapCanvas.Marks = ViewModel.MiniMapMarks;
    }

    /// <summary>Selects a span of the raw source and scrolls it into view. Queued at Loaded priority because
    /// the search switches to the source surface in the same beat — the box has no layout until that lands.</summary>
    private void SelectInSource(int offset, int length)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var max = SourceBox.Text.Length;
            if (offset < 0 || offset > max) return;

            SourceBox.Focus();
            SourceBox.Select(offset, Math.Min(length, max - offset));
            SourceBox.ScrollToLine(SourceBox.GetLineIndexFromCharacterIndex(offset));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => ViewModel;
}
