using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Features.Compressed.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Compressed.Views;

[ViewFor(typeof(CompressedViewModel))]
public partial class CompressedView : UserControl
{
    private readonly CompressedViewModel _vm;

    public CompressedView(CompressedViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void EntryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EntryList.SelectedItem is ArchiveNode node)
            _vm.ActivateRowCommand.Execute(node);
    }

    // ── Password overlay (PasswordBox can't be data-bound) ─────────────────────

    private void PasswordOk_Click(object sender, RoutedEventArgs e)
    {
        var pwd = PasswordEntry.Password;
        PasswordEntry.Clear();
        _ = _vm.SubmitPasswordAsync(pwd);
    }

    private void PasswordEntry_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) PasswordOk_Click(sender, e);
        else if (e.Key == Key.Escape) _vm.CancelPasswordCommand.Execute(null);
    }
}
