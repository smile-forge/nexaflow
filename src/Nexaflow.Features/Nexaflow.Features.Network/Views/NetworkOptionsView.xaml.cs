using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Network.ViewModels;
using Nexaflow.IO.Network.Adapters;

namespace Nexaflow.Features.Network.Views;

/// <summary>
/// Options → Network: the guard's kill switch, which adapters are used, which networks an address sweep may
/// run on, and one run's ceilings.
/// </summary>
/// <remarks>
/// The Options panel creates this with no DI and sets the <i>live</i> config as its DataContext, so the
/// editing happens on a <see cref="NetworkOptionsModel"/> of its own and reaches the config only on Save.
/// </remarks>
public partial class NetworkOptionsView : UserControl, ICustomConfigApply, IConfigChangeTracker, IConfigValidation
{
    private NetworkConfig? _config;
    private NetworkOptionsModel? _model;

    public NetworkOptionsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Load();
        Loaded += (_, _) => Load();
    }

    public bool HasChanges => _model?.HasChanges ?? false;
    public bool IsValid => _model?.IsValid ?? true;

    public event EventHandler? HasChangesChanged;
    public event EventHandler? IsValidChanged;

    public void Apply() => _model?.Apply();

    private void Load()
    {
        if (DataContext is not NetworkConfig config || ReferenceEquals(config, _config)) return;
        _config = config;

        if (_model is not null) _model.Changed -= Moved;

        // Every adapter the OS reports, down ones included — excluding one before it connects is the point.
        _model = new NetworkOptionsModel(config, NetworkAdapters.Read());
        _model.Changed += Moved;

        Root.DataContext = _model;
        Moved(this, EventArgs.Empty);
    }

    private void Moved(object? sender, EventArgs e)
    {
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
        IsValidChanged?.Invoke(this, EventArgs.Empty);
    }
}
