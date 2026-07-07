using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.Dotnet.ViewModels;

namespace Nexaflow.Features.Dotnet.Viewlets;

public partial class DotnetViewletView : UserControl, IViewletAiSurface, IViewletQuiescible
{
    private readonly DotnetViewletViewModel _vm;
    private readonly DispatcherTimer _ellipsisTimer;
    private int _phase;

    public DotnetViewletView(IShellServices shell, string folderPath)
    {
        InitializeComponent();
        _vm = new DotnetViewletViewModel(shell, folderPath);
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

    // ── IViewletAiSurface — delegate to the VM, which owns the target + verb runner ──────────────
    string? IViewletAiSurface.GetContext() => _vm.GetContext();
    IReadOnlyList<IClientTool> IViewletAiSurface.GetClientTools() => _vm.GetClientTools();

    // ── IViewletQuiescible — kill the NuGet-scan process (it locks the folder) before it's mutated ──
    Task IViewletQuiescible.QuiesceAsync(CancellationToken ct) => _vm.QuiesceAsync(ct);
}
