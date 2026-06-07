using Microsoft.Win32;
using Nexaflow.Features.WindowsRegistry.Services;

namespace Nexaflow.Features.WindowsRegistry.ViewModels;

/// <summary>One row in the right-hand value list.</summary>
public sealed class RegistryValue
{
    /// <summary>The real value name; empty string for the key's default value.</summary>
    public string RawName { get; }

    /// <summary>Display name — "(Default)" for the unnamed default value.</summary>
    public string Name => RawName.Length == 0 ? "(Default)" : RawName;

    public RegistryValueKind Kind      { get; }
    public string            TypeLabel => RegistryValueCodec.TypeLabel(Kind);
    public string            Data      { get; }
    public bool              IsDefault => RawName.Length == 0;

    public RegistryValue(string rawName, RegistryValueKind kind, object? data)
    {
        RawName = rawName;
        Kind    = kind;
        Data    = RegistryValueCodec.FormatForDisplay(data, kind);
    }
}
