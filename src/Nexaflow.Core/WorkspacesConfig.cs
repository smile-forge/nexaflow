using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Features.Common;

namespace Nexaflow.Core;

[CustomControl(typeof(ProfilesConfigControl))]
public sealed class WorkspacesConfig : IFeatureConfig
{
    // Kept as "workcontexts" / "Contexts" so existing on-disk config (workcontexts.json) still loads.
    public string ConfigName   => "workcontexts";
    public string FriendlyName => "Workspaces";

    public List<Profile> Contexts { get; set; } = [new Profile()];
}
