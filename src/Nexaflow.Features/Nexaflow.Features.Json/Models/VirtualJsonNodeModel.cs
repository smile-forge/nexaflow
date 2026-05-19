using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.Json.Models;

public sealed partial class VirtualJsonNodeModel : JsonNodeModel
{
    public long ByteOffset { get; set; }
    public long EndOffset  { get; set; }

    [ObservableProperty] private bool _isLoading;
}
