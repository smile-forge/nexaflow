using Nexaflow.Features.Tabular.Templates;

namespace Nexaflow.Features.Tabular.ViewModels;

/// <summary>Row in the Apply-Template panel: a saved template plus whether it's compatible with the
/// file currently open. Apply/Delete are relayed to the parent <see cref="TabularViewModel"/>
/// commands via <c>CommandParameter</c>, so this is a thin, immutable display wrapper.</summary>
public sealed class TemplateItemViewModel
{
    public TemplateItemViewModel(TabularTemplate template, bool isCompatible)
    {
        Template     = template;
        IsCompatible = isCompatible;
    }

    public TabularTemplate Template     { get; }
    public bool            IsCompatible { get; }

    public string Name         => Template.Name;
    public string ScopeSummary => Template.ScopeSummary;

    /// <summary>Non-empty when the template doesn't fit the current file (drives a warning hint).</summary>
    public string CompatibilityNote => IsCompatible ? string.Empty : "≠ different shape";
}
