using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Controls;
using System.Collections.Generic;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

/// <summary>
/// Per-extension overrides for the double-click ("default") action. When an override exists for a file's
/// extension it wins over <see cref="Services.DefaultFileOpener"/>'s automatic internal-vs-shell resolution.
/// Global config (one instance per process), edited from the Default Actions Options tab.
/// </summary>
[CustomControl(typeof(DefaultActionsEditorControl))]
public sealed class DefaultActionsConfig : IFeatureConfig
{
    public string ConfigName   => "defaultactions";
    public string FriendlyName => "Default Actions";

    public List<DefaultActionOverride> Overrides { get; set; } = [];
}

/// <summary>The kind of target a <see cref="DefaultActionOverride"/> points at.</summary>
public enum DefaultActionKind
{
    /// <summary>A built-in viewer, identified by its <c>ExperienceId</c> (e.g. <c>/text/code</c>).</summary>
    InternalViewer,
    /// <summary>A user external app, identified by its <see cref="ExternalAppDefinition.Id"/>.</summary>
    ExternalApp,
    /// <summary>A Windows shell verb (e.g. <c>open</c>, <c>edit</c>) run via <c>UseShellExecute</c>.</summary>
    WindowsVerb,
}

public sealed class DefaultActionOverride
{
    /// <summary>Bare extension, no dot, lower-case (e.g. <c>pdf</c>, <c>tar.gz</c>).</summary>
    public string            Extension { get; set; } = string.Empty;

    public DefaultActionKind Kind      { get; set; }

    /// <summary>ExperienceId (InternalViewer) · external-app Id (ExternalApp) · verb name (WindowsVerb).</summary>
    public string            TargetId  { get; set; } = string.Empty;

    /// <summary>Human label shown in the Options list; advisory only (recomputed when candidates load).</summary>
    public string            Label     { get; set; } = string.Empty;
}
