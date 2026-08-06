using System;
using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Email.ViewModels;

namespace Nexaflow.Features.Email.Views;

internal partial class EmailView : UserControl, IPageView
{
    private readonly EmailViewModel _vm;

    public EmailView(EmailViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        // Set the inline-image base directory before the Markdown binding evaluates (which happens when
        // DataContext is assigned), so cid: images resolve on the first render.
        MarkdownBody.BaseDirectory = vm.MarkdownBaseDirectory;
        DataContext = vm;

        // Search collaboration: the rendered body highlights its own text; a text-source body has its match
        // selected. The VM decides which pane is active and drives the header/attachment highlights itself.
        vm.FindInRenderedBody = MarkdownBody.FindInRendered;
        vm.StepRenderedBody   = MarkdownBody.StepSearch;
        vm.ClearRenderedBody  = MarkdownBody.ClearSearch;
        vm.BodySelectionRequested += SelectInBody;
        Unloaded += (_, _) => vm.BodySelectionRequested -= SelectInBody;
    }

    // Selects a span of whichever text-source body pane is on screen, scrolling it into view.
    private void SelectInBody(int offset, int length)
    {
        var box = _vm.IsPlainTextView ? PlainTextBody
                : _vm.IsHtmlSourceView ? HtmlSourceBody
                : null;
        if (box is null) return;

        Dispatcher.BeginInvoke(() =>
        {
            var max = box.Text.Length;
            if (offset < 0 || offset > max) return;
            box.Focus();
            box.Select(offset, Math.Min(length, max - offset));
            box.ScrollToLine(box.GetLineIndexFromCharacterIndex(offset));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    IPageViewModel? IPageView.ViewModel => _vm;
}
