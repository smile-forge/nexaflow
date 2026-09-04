using Nexaflow.Features.WindowsFileSystem.ViewModels;
using System.Windows.Controls;

namespace Nexaflow.Features.WindowsFileSystem.Views;

/// <summary>
/// The operations panel that grows above the folder tree while a copy, move or delete is running.
/// Purely a surface: the debounce, the rows and the commands are all
/// <see cref="FileOperationsPanelViewModel"/>'s. It attaches on load and detaches on unload because
/// the queue outlives the tab and would otherwise retain the view-model.
/// </summary>
public partial class FileOperationsPanel : UserControl
{
    public FileOperationsPanel() => InitializeComponent();
}
