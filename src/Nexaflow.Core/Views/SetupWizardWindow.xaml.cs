using Nexaflow.Core.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace Nexaflow.Core.Views;

/// <summary>
/// First-run / post-update setup wizard. Shown modally before the main window when there's anything
/// to configure (see <see cref="SetupWizardViewModel.Build"/>). Closing it — Finish, Skip, or the
/// window X — always lets App proceed to launch (no dead-end); committed steps persist either way.
/// </summary>
public partial class SetupWizardWindow : Window
{
    private readonly SetupWizardViewModel _vm;

    public SetupWizardWindow(SetupWizardViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        _vm.Finished += (_, _) => Close();
        Closed       += (_, _) => _vm.ReleaseResources();   // safety: also covers the window X
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
