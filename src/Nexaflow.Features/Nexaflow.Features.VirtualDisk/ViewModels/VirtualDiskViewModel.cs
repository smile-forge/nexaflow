using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.VirtualDisk.ViewModels;

/// <summary>
/// Stub view-model for the not-yet-implemented VirtualDisk feature. <see cref="IShellServices"/> is
/// injected for parity with real feature view-models (and future use); the stub does not consume it yet.
/// </summary>
public sealed partial class VirtualDiskViewModel : ObservableObject
{
    [ObservableProperty]
    private string _message = "Virtual Disk support is coming soon.";

    public VirtualDiskViewModel(IShellServices shellServices) { }
}
