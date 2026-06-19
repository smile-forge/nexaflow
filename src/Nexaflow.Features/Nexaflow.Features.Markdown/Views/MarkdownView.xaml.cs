using Nexaflow.Features.Common;
using Nexaflow.Features.Markdown.ViewModels;
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

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => ViewModel;
}
