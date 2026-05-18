using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json;

namespace Nexaflow.Features.Json.Models;

public sealed partial class JsonValueNodeModel : JsonNodeModel
{
    [ObservableProperty] private object?       _value;
    [ObservableProperty] private JsonValueKind _valueKind;

    public string DisplayValue => ValueKind switch
    {
        JsonValueKind.String => $"\"{Value}\"",
        JsonValueKind.Null   => "null",
        JsonValueKind.True   => "true",
        JsonValueKind.False  => "false",
        _                    => Value?.ToString() ?? string.Empty,
    };

    partial void OnValueChanged(object? value)     => OnPropertyChanged(nameof(DisplayValue));
    partial void OnValueKindChanged(JsonValueKind v) => OnPropertyChanged(nameof(DisplayValue));
}
