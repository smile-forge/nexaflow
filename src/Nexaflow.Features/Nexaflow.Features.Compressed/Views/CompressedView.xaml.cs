using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Compressed.ViewModels;

namespace Nexaflow.Features.Compressed.Views;

public partial class CompressedView : UserControl, IPageView
{
    private readonly CompressedViewModel _vm;

    public CompressedView(CompressedViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Drop += OnDrop;
        DragOver += OnDragOver;
    }

    public IPageViewModel? ViewModel => _vm;

    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        // Single-archive tab — params don't change after creation; nothing to re-init.
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

    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasFiles(e) && _vm.CanModify ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!HasFiles(e)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        await _vm.AddSourcesAsync(paths.ToList());
    }
}
