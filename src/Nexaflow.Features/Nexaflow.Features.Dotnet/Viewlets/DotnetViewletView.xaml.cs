using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.Dotnet.Models;
using Nexaflow.Features.Dotnet.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Dotnet.Viewlets;

[ViewFor(typeof(DotnetViewletViewModel))]
public partial class DotnetViewletView : UserControl
{
    private readonly DotnetViewletViewModel _vm;
    private readonly DispatcherTimer _ellipsisTimer;
    private int _phase;

    public DotnetViewletView(DotnetViewletViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;

        _ellipsisTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _ellipsisTimer.Tick += (_, _) => Tick();
        _vm.PropertyChanged += OnVmPropertyChanged;

        Unloaded += (_, _) =>
        {
            _ellipsisTimer.Stop();
            _vm.CancelPending();
        };
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DotnetViewletViewModel.IsBusy))
        {
            if (_vm.IsBusy) { _phase = 0; _ellipsisTimer.Start(); Tick(); }
            else _ellipsisTimer.Stop();
        }
    }

    // Animate the dots only when there's no live output line to show; otherwise keep them static.
    private void Tick()
    {
        if (!_vm.IsBusy) return;
        if (!string.IsNullOrEmpty(_vm.ProgressDetail))
        {
            EllipsisText.Text = "...";
            return;
        }
        _phase = (_phase + 1) % 3;
        EllipsisText.Text = new string('.', _phase + 1);
    }

    // ── Dropdown menus ────────────────────────────────────────────────────────────────────────────
    // ContextMenu and MenuItem carry keyless styles in Core's Styles.xaml, so a hand-built menu is themed
    // for free — same idiom as the Git viewlet's branch picker.

    private void TargetButton_Click(object sender, RoutedEventArgs e)
        => ShowMenu(TargetButton, _vm.Targets, _vm.SelectedTarget, t => _vm.SelectedTarget = t);

    private void StartupButton_Click(object sender, RoutedEventArgs e)
        => ShowMenu(StartupButton, _vm.RunnableProjects, _vm.StartupProject, _vm.ChooseStartupProject);

    private static void ShowMenu(
        UIElement anchor, IEnumerable<DotnetTarget> items, DotnetTarget? current, Action<DotnetTarget> onPick)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };

        foreach (var target in items)
        {
            var item = new MenuItem
            {
                Header    = target.DisplayName,
                ToolTip   = target.Path,
                IsChecked = target == current,
            };
            var captured = target;
            item.Click += (_, _) => onPick(captured);
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0)
            menu.IsOpen = true;
    }
}
