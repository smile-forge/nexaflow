using Microsoft.Win32;
using Nexaflow.Features.WindowsRegistry.Services;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.WindowsRegistry.ViewModels;

/// <summary>One row in the right-hand value list.</summary>
public sealed class RegistryValue
{
    /// <summary>The real value name; empty string for the key's default value.</summary>
    public string RawName { get; }

    /// <summary>The name as the AI tools report it — "(Default)" for the unnamed default value.</summary>
    public string Name => RawName.Length == 0 ? "(Default)" : RawName;

    /// <summary>The name the value list shows, in the person's language.</summary>
    public string DisplayName => RawName.Length == 0 ? Str.Get("WindowsRegistry.Value.Default") : RawName;

    public RegistryValueKind Kind      { get; }
    public string            TypeLabel => RegistryValueCodec.TypeLabel(Kind);

    /// <summary>The data as the AI tools report it — "(value not set)" for a default value the key does not hold.</summary>
    public string            Data      { get; }

    /// <summary>The data the value list shows, in the person's language.</summary>
    public string            DisplayData => IsSet ? Data : Str.Get("WindowsRegistry.Value.NotSet");

    public bool              IsDefault => RawName.Length == 0;

    /// <summary>False only for the row standing in for a default value the key does not hold.</summary>
    public bool              IsSet     { get; }

    public RegistryValue(string rawName, RegistryValueKind kind, object? data)
        : this(rawName, kind, data, isSet: true) { }

    private RegistryValue(string rawName, RegistryValueKind kind, object? data, bool isSet)
    {
        RawName = rawName;
        Kind    = kind;
        Data    = RegistryValueCodec.FormatForDisplay(data, kind);
        IsSet   = isSet;
    }

    /// <summary>The row shown for a key's default value when the key holds none.</summary>
    public static RegistryValue UnsetDefault() => new("", RegistryValueKind.String, "(value not set)", isSet: false);
}
