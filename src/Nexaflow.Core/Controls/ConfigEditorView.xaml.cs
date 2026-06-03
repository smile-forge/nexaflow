using System.Windows.Controls;

namespace Nexaflow.Core.Controls;

/// <summary>
/// Renders one <see cref="ViewModels.ConfigEditViewModel"/> (its custom control or a reflected
/// property grid). Reused by the setup wizard; its DataContext is the ConfigEditViewModel.
/// </summary>
public partial class ConfigEditorView : UserControl
{
    public ConfigEditorView() => InitializeComponent();
}
