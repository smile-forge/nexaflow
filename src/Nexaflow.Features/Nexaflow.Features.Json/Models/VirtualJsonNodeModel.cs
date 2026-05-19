using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.Json.Models;

public sealed partial class VirtualJsonNodeModel : JsonNodeModel
{
    public long ByteOffset       { get; set; }
    public long EndOffset        { get; set; }   // = file size
    public int  EstimatedItemCount { get; set; } // > 0 when this sentinel represents N unloaded items

    [ObservableProperty] private bool _isLoading;

    /// <summary>Set true after a load at this sentinel's offset fails so we don't retry.</summary>
    public bool IsLoadFailed { get; set; }
}
