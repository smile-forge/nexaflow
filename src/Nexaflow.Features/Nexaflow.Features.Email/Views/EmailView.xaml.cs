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
    }

    IPageViewModel? IPageView.ViewModel => _vm;
}
