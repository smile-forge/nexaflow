using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Interaction;

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

    public bool CanProcessKey(ConsoleKey key, KeyModifiers modifiers)
    {
        return (key, modifiers) switch
        {
            (ConsoleKey.C, KeyModifiers.Control) => HasSelection,
            (ConsoleKey.X, KeyModifiers.Control) => HasSelection,

            (ConsoleKey.Delete, KeyModifiers.None)  => HasSelection,
            (ConsoleKey.Delete, KeyModifiers.Shift) => HasSelection,
            // Shift+Enter opens the selected entry — the keyboard equivalent of double-clicking its row.
            // Shift, not plain Enter: the AI input normally holds focus and Enter submits the prompt there,
            // so an unmodified binding would read as a clash even though the shell's TextBox guard hides it.
            (ConsoleKey.Enter, KeyModifiers.Shift)  => IsSingleSelection,
            // Refresh is always available — it re-reads the current path (list + tree),
            // and in This-PC mode re-enumerates the drives.
            (ConsoleKey.F5, KeyModifiers.None)      => true,
            _ => false,
        };
    }

    public bool ProcessKey(ConsoleKey key, KeyModifiers modifiers)
    {
        if (!CanProcessKey(key, modifiers)) return false;

        // All shortcuts route through TryExecuteAction so they go through the same
        // code path as clicking the button: path resolution, force flag, flash animation.
        return (key, modifiers) switch
        {
            (ConsoleKey.C, KeyModifiers.Control)    => _viewModel.TryExecuteAction<CopyFiles>(),
            (ConsoleKey.X, KeyModifiers.Control)    => _viewModel.TryExecuteAction<CutFiles>(),

            // Shift+Del: FileActionViewModel.Execute() reads Keyboard.IsKeyDown(Shift) live,
            // so force=true is set automatically — no special handling needed here.
            (ConsoleKey.Delete, _)                  => _viewModel.TryExecuteAction<DeleteFile>(),
            // F5: re-read the current path — refreshes both the file list and the tree.
            (ConsoleKey.F5, KeyModifiers.None)      => Refresh(),
            (ConsoleKey.Enter, KeyModifiers.Shift)  => OpenSelection(),
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
