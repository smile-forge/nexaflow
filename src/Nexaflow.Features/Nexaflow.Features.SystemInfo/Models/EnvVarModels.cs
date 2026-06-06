using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.SystemInfo.Models;

/// <summary>Which environment scope a variable belongs to (the two persistent, editable scopes —
/// mirrors the native Windows "Environment Variables" dialog).</summary>
public enum EnvScope
{
    /// <summary>Per-user (HKCU) — editable without elevation.</summary>
    User,
    /// <summary>System-wide (HKLM) — editing requires the elevated bridge.</summary>
    Machine,
}

/// <summary>
/// One environment variable. <see cref="Value"/> is observable so the detail editor and PATH-entry list
/// stay in sync. Values are kept raw/unexpanded (so <c>%VAR%</c> indirection survives a round-trip).
/// </summary>
public sealed partial class EnvVarRow : ObservableObject
{
    public string Name { get; }
    public EnvScope Scope { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPathLike))]
    [NotifyPropertyChangedFor(nameof(Entries))]
    private string _value;

    public EnvVarRow(string name, string value, EnvScope scope)
    {
        Name   = name;
        Scope  = scope;
        _value = value;
    }

    /// <summary>True for ';'-delimited list values (PATH, PATHEXT, PSModulePath…) — shown as an entry list.</summary>
    public bool IsPathLike => Value.Contains(';');

    /// <summary>The value split on ';' for list-style editing. Empty entries are preserved (order matters).</summary>
    public IReadOnlyList<string> Entries =>
        string.IsNullOrEmpty(Value) ? [] : Value.Split(';');

    /// <summary>Machine-scope rows can only be changed via the elevated bridge.</summary>
    public bool RequiresElevation => Scope == EnvScope.Machine;
}

/// <summary>Both scopes captured in one background pass.</summary>
public sealed record EnvVarsBundle(
    IReadOnlyList<EnvVarRow> User,
    IReadOnlyList<EnvVarRow> Machine);
