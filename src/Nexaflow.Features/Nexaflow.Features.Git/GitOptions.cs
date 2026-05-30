using Nexaflow.Features.Common;

namespace Nexaflow.Features.Git;

public sealed class GitOptions : IFeatureConfig
{
    public string ConfigName   => "git";
    public string FriendlyName => "Git";

    [ConfigDisplayName("Git Manager Application")]
    [FilePath]
    public string GitManagerPath { get; set; } = string.Empty;
}
