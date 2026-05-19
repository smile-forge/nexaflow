using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.Json.Models;

public enum NodeViewMode { Tree, Text, Table }

public abstract partial class JsonNodeModel : ObservableObject
{
    [ObservableProperty] private string? _key;
    [ObservableProperty] private int?    _index;

    public JsonNodeModel? Parent { get; internal set; }

    public string DisplayKey => Key is not null ? $"\"{Key}\"" : Index is not null ? $"[{Index}]" : "$";

    public string GetJsonPath()
    {
        if (Parent is null) return "$";
        var parentPath = Parent.GetJsonPath();
        if (Key is not null)   return $"{parentPath}.{Key}";
        if (Index is not null) return $"{parentPath}[{Index}]";
        return parentPath;
    }
}
