using Nexaflow.Features.Common;

namespace Nexaflow.Features.Projects;

[WorkspaceScopedConfig]
[MandatorySetup]
public sealed class ProjectsConfig : IFeatureConfig
{
    public string ConfigName   => "projects";
    public string FriendlyName => "Projects";

    /// <summary>Whether the Projects feature is enabled for this workspace. The project directory is
    /// only required (and editable) when this is on.</summary>
    [ConfigDisplayName("Enable projects")]
    public bool EnableProjects { get; set; }

    [ConfigDisplayName("Project Directory")]
    [FolderPath]
    [DisabledIfNotSet(nameof(EnableProjects))]
    public string ProjectDirectory { get; set; } = @"C:\Projects";
}
