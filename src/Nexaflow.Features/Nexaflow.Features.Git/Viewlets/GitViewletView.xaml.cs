using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.Git.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nexaflow.Features.Git.Viewlets;

/// <summary>
/// The Git viewlet's surface. Everything observable or triggerable lives on <see cref="GitViewletViewModel"/>;
/// this owns only what is genuinely view-level — the hand-built branch menu and the result line's fade.
/// </summary>
public partial class GitViewletView : UserControl, IViewletAiSurface
{
    private readonly GitViewletViewModel _vm;

    public GitViewletView(GitOptions options, IShellServices shell, string folderPath, IViewletController controller)
    {
        InitializeComponent();
        _vm = new GitViewletViewModel(options, shell, folderPath, controller);
        DataContext = _vm;

        _vm.PropertyChanged += OnVmPropertyChanged;
        Loaded += (_, _) => _ = _vm.RefreshAsync();
    }

    // ── Branch menu ───────────────────────────────────────────────────────
    // ContextMenu and MenuItem carry keyless styles in Core's Styles.xaml, so a hand-built menu is themed
    // for free — same idiom as the .NET viewlet's target picker.

    private void BranchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.LocalBranches.Count == 0) return;

        var menu = new ContextMenu { PlacementTarget = BranchButton, Placement = PlacementMode.Bottom };
        foreach (var name in _vm.LocalBranches)
        {
            var item = new MenuItem { Header = name, IsChecked = name == _vm.BranchName };
            var captured = name;
            item.Click += (_, _) => _vm.SwitchBranchCommand.Execute(captured);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    // ── Result line ───────────────────────────────────────────────────────
    // The text and its success/failure are VM state; the timed fade-out is not — it's an animation, so it
    // stays here and is re-triggered whenever the VM publishes a new result.

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GitViewletViewModel.ActionResult)) ShowActionResult();
    }

    private void ShowActionResult()
    {
        if (string.IsNullOrEmpty(_vm.ActionResult)) { PullResultText.Visibility = Visibility.Collapsed; return; }

        PullResultText.BeginAnimation(OpacityProperty, null);   // cancel any in-progress fade
        PullResultText.Foreground = (Brush)FindResource(_vm.ActionResultIsError ? "DangerBrush" : "TextMutedBrush");
        PullResultText.Opacity    = 1.0;
        PullResultText.Visibility = Visibility.Visible;

        var fade = new DoubleAnimation
        {
            From         = 1.0,
            To           = 0.0,
            BeginTime    = TimeSpan.FromSeconds(3.5),
            Duration     = new Duration(TimeSpan.FromSeconds(1.5)),
            FillBehavior = FillBehavior.Stop
        };
        fade.Completed += (_, _) =>
        {
            PullResultText.Visibility = Visibility.Collapsed;
            PullResultText.Opacity    = 1.0;
        };
        PullResultText.BeginAnimation(OpacityProperty, fade);
    }

    // ── IViewletAiSurface — delegate to the VM, which owns the repo services ──────────────────────

    string? IViewletAiSurface.GetContext() => _vm.GetContext();
    IReadOnlyList<IClientTool> IViewletAiSurface.GetClientTools() => _vm.GetClientTools();
}
