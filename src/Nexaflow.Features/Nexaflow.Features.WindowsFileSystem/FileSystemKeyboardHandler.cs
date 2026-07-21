using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using System.Windows.Input;

namespace Nexaflow.Features.WindowsFileSystem;

/// <summary>
/// Handles keyboard shortcuts for <see cref="FileSystemView"/>.
/// All shortcuts delegate to the existing <see cref="IFileAction"/> strip so
/// behavior — flash animation, path resolution, force flag — is identical to
/// clicking the corresponding button.
/// </summary>
public sealed class FileSystemKeyboardHandler : IKeyboardHandler
{
    private readonly FileSystemViewModel _viewModel;

    public FileSystemKeyboardHandler(FileSystemViewModel viewModel)
        => _viewModel = viewModel;

    private bool HasSelection => !_viewModel.IsThisPcMode && _viewModel.CurrentSelection.Count > 0;

    /// <summary>Open acts on exactly one entry, mirroring a double-click on a single row.</summary>
    private bool IsSingleSelection => !_viewModel.IsThisPcMode && _viewModel.CurrentSelection.Count == 1;

    public bool CanProcessKey(Key key, ModifierKeys modifiers)
    {
        return (key, modifiers) switch
        {
            (Key.C, ModifierKeys.Control) => HasSelection,
            (Key.X, ModifierKeys.Control) => HasSelection,
            (Key.V, ModifierKeys.Control) => NativeMethods.ClipboardHasFiles(),
            (Key.Delete, ModifierKeys.None)  => HasSelection,
            (Key.Delete, ModifierKeys.Shift) => HasSelection,
            // Shift+Enter opens the selected entry — the keyboard equivalent of double-clicking its row.
            // Shift, not plain Enter: the AI input normally holds focus and Enter submits the prompt there,
            // so an unmodified binding would read as a clash even though the shell's TextBox guard hides it.
            (Key.Enter, ModifierKeys.Shift)  => IsSingleSelection,
            // Refresh is always available — it re-reads the current path (list + tree),
            // and in This-PC mode re-enumerates the drives.
            (Key.F5, ModifierKeys.None)      => true,
            _ => false,
        };
    }

    public bool ProcessKey(Key key, ModifierKeys modifiers)
    {
        if (!CanProcessKey(key, modifiers)) return false;

        // All shortcuts route through TryExecuteAction so they go through the same
        // code path as clicking the button: path resolution, force flag, flash animation.
        return (key, modifiers) switch
        {
            (Key.C, ModifierKeys.Control)    => _viewModel.TryExecuteAction<CopyFiles>(),
            (Key.X, ModifierKeys.Control)    => _viewModel.TryExecuteAction<CutFiles>(),
            (Key.V, ModifierKeys.Control)    => _viewModel.TryExecuteAction<PasteFiles>(),
            // Shift+Del: FileActionViewModel.Execute() reads Keyboard.IsKeyDown(Shift) live,
            // so force=true is set automatically — no special handling needed here.
            (Key.Delete, _)                  => _viewModel.TryExecuteAction<DeleteFile>(),
            // F5: re-read the current path — refreshes both the file list and the tree.
            (Key.F5, ModifierKeys.None)      => Refresh(),
            (Key.Enter, ModifierKeys.Shift)  => OpenSelection(),
            _                                => false,
        };
    }

    /// <summary>Opens the selected entry through the same <c>OpenEntryCommand</c> the row's double-click
    /// binding uses, so folders navigate and files go through the default-open resolver identically.</summary>
    private bool OpenSelection()
    {
        if (_viewModel.CurrentSelection is not [var entry]) return false;
        _viewModel.OpenEntryCommand.Execute(entry);
        return true;
    }

    private bool Refresh()
    {
        _viewModel.Refresh();
        return true;
    }
}
