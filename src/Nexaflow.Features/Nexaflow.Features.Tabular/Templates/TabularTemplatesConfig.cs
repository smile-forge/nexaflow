using System.Collections.Generic;
using Nexaflow.Features.Common;
using Nexaflow.Features.Tabular.Controls;

namespace Nexaflow.Features.Tabular.Templates;

/// <summary>
/// Global feature config holding the user's saved tabular templates. Discovered by reflection and
/// shared across workspaces (templates describe file <em>shapes</em>, not a workspace). Managed primarily
/// from the Tabular viewer (Template This / Apply Template); the Options section
/// (<see cref="TabularTemplatesEditorControl"/>) is a central rename/delete surface. The
/// <c>[CustomControl]</c> is required because a bare <see cref="Templates"/> list would render as a
/// broken fallback editor in the default Options property grid.
/// </summary>
[CustomControl(typeof(TabularTemplatesEditorControl))]
public sealed class TabularTemplatesConfig : IFeatureConfig
{
    public string ConfigName   => "tabulartemplates";
    public string FriendlyName => "Tabular Templates";

    public List<TabularTemplate> Templates { get; set; } = new();
}
