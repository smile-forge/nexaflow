using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.UsbProbe.ViewModels;
using Nexaflow.IO.Usb.Naming;

namespace Nexaflow.Features.UsbProbe.Views;

/// <summary>Options → USB: keeping device names current.</summary>
/// <remarks>
/// The Options panel creates this with no DI and sets the <i>live</i> config as its DataContext, so the editing
/// happens on a <see cref="UsbProbeOptionsModel"/> of its own and reaches the config only on Save.
/// </remarks>
public partial class UsbProbeOptionsView : UserControl, ICustomConfigApply, IConfigChangeTracker
{
    UsbProbeConfig? _config;
    UsbProbeOptionsModel? _model;

    public UsbProbeOptionsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Load();
        Loaded += (_, _) => Load();
    }

    public bool HasChanges => _model?.HasChanges ?? false;

    public event EventHandler? HasChangesChanged;

    public void Apply() => _model?.Apply();

    void Load()
    {
        if (DataContext is not UsbProbeConfig config || ReferenceEquals(config, _config)) return;
        _config = config;
        if (_model is not null) _model.Changed -= Moved;
        _model = new UsbProbeOptionsModel(config, UsbIdsStore.ForUser);
        _model.Changed += Moved;
        Root.DataContext = _model;
    }

    void Moved(object? sender, EventArgs e) => HasChangesChanged?.Invoke(this, EventArgs.Empty);
}
