using Nexaflow.Features.Common;
using Nexaflow.Features.Console.Controls;
using Nexaflow.Features.Console.Models;

namespace Nexaflow.Features.Console;

[CustomControl(typeof(ConsoleEnvironmentsEditorControl))]
public sealed class ConsoleConfig : IFeatureConfig
{
    public string ConfigName   => "console";
    public string FriendlyName => "Console";

    public List<ConsoleEnvironment> Environments { get; set; } = [];

    public ConsoleEnvironment? GetDefaultEnv()
        => Environments.FirstOrDefault(e => e.IsDefault)
        ?? Environments.FirstOrDefault();

    public ConsoleEnvironment? FindEnvByName(string name)
        => Environments.FirstOrDefault(e =>
               string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
}
