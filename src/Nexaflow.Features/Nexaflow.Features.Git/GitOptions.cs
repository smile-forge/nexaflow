using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Git;

public sealed class GitOptions : IFeatureConfig
{
    public string ConfigName   => "git";
    public string FriendlyName => Str.Get("Git.Config.Git");

    [ConfigDisplayName("Git.Config.Git.GitManagerPath")]
    [FilePath(".exe")]
    public string GitManagerPath { get; set; } = string.Empty;
}
