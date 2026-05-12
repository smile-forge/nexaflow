using Nexaflow.Features.Common;

namespace Nexaflow.Features.Projects;

public sealed class ProjectsConfig : IFeatureConfig
{
    public string ConfigName   => "projects";
    public string FriendlyName => "Projects";

    [ConfigDisplayName("Project Directory")]
    [FolderPath]
    public string ProjectDirectory { get; set; } = @"C:\Projects";
}
