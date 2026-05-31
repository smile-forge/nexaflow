using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Features.Common;

namespace Nexaflow.Core;

[CustomControl(typeof(WorkContextsConfigControl))]
public sealed class WorkContextsConfig : IFeatureConfig
{
    public string ConfigName   => "workcontexts";
    public string FriendlyName => "Work Contexts";

    public List<WorkContextConfig> Contexts { get; set; } = [new WorkContextConfig()];
}
