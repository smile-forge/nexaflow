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
    /// <summary>Extension to match — e.g. ".jpg". "*" or empty matches any file.</summary>
    public string Extension        { get; set; } = string.Empty;
    public string DisplayName      { get; set; } = string.Empty;
    public string ApplicationPath  { get; set; } = string.Empty;
    public string Arguments        { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string IconPath         { get; set; } = string.Empty;
    public MultiFileMode MultiFile { get; set; } = MultiFileMode.SingleFileOnly;
}

public enum MultiFileMode
{
    SingleFileOnly,
    SingleLaunchAllFiles,
    OneLaunchPerFile,
}
