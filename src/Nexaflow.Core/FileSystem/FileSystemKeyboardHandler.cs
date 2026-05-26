using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem;
using Nexaflow.Core.ViewModels;
using System.Windows.Input;
using Nexaflow.Features.WindowsFileSystem.FileActions;

namespace Nexaflow.Core.FileSystem;

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

    public bool CanProcessKey(Key key, ModifierKeys modifiers)
    {
        return (key, modifiers) switch
        {
            (Key.C, ModifierKeys.Control) => HasSelection,
            (Key.X, ModifierKeys.Control) => HasSelection,
            (Key.V, ModifierKeys.Control) => NativeMethods.ClipboardHasFiles(),
            (Key.Delete, ModifierKeys.None)  => HasSelection,
            (Key.Delete, ModifierKeys.Shift) => HasSelection,
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
            _                                => false,
        };
    }
}
