using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Controls;
using System.Collections.Generic;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

[CustomControl(typeof(ExternalAppsEditorControl))]
public sealed class ExternalAppsConfig : IFeatureConfig
{
    public string ConfigName   => "externalapps";
    public string FriendlyName => "External Apps";

    /// <summary>
    /// When true, a background HKCR scan populates registry-derived file type
    /// mappings on startup, and <see cref="ShellVerbAction"/>s are surfaced
    /// alongside built-in actions. When false, both are suppressed.
    /// </summary>
    public bool UseRegistryMapping { get; set; } = true;

    public List<ExternalAppDefinition> Apps { get; set; } = new();
}

public sealed class ExternalAppDefinition
{
    /// <summary>
    /// Stable identity used to reference this app from a Default Action override
    /// (<c>DefaultActionKind.ExternalApp</c>). Assigned on first save/import and backfilled for
    /// pre-existing apps at startup (<see cref="Services.ExternalAppRegistry.BackfillIds"/>), so a
    /// reference survives display-name/path edits. Empty only for a definition never yet persisted.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Legacy single-extension match (e.g. ".jpg"; "*"/empty = any). Superseded by
    /// <see cref="Criteria"/>: the Options editor migrates a non-empty value into an
    /// <see cref="CriteriaType.Extension"/> criterion on load and writes this blank thereafter.
    /// Kept so configs written before scoping still deserialize and match (see
    /// <see cref="Services.ExternalAppRegistry"/>'s fallback).
    /// </summary>
    public string Extension        { get; set; } = string.Empty;
    public string DisplayName      { get; set; } = string.Empty;
    public string ApplicationPath  { get; set; } = string.Empty;
    public string Arguments        { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string IconPath         { get; set; } = string.Empty;
    public MultiFileMode MultiFile { get; set; } = MultiFileMode.SingleFileOnly;

    /// <summary>
    /// Optional file-selection scope. When non-empty, a file matches this app iff it
    /// satisfies any of these criteria (full-path globs / extensions), and the legacy
    /// <see cref="Extension"/> field is ignored. Empty (the default for configs written
    /// before scoping existed) falls back to matching on <see cref="Extension"/>.
    /// Only <see cref="CriteriaType.Extension"/> and <see cref="CriteriaType.PathPattern"/>
    /// are honored by the external-app resolver.
    /// </summary>
    public List<FileSelectionCriteria> Criteria { get; set; } = [];
}

public enum MultiFileMode
{
    SingleFileOnly,
    SingleLaunchAllFiles,
    OneLaunchPerFile,
}
