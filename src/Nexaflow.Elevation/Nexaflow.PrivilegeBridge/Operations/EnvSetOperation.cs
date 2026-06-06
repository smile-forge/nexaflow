using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

internal sealed class EnvSetOperation : EnvOperationBase
{
    public override string Id => ElevatedOps.EnvSet;

    protected override ElevatedOperationResult Apply(string name, IReadOnlyDictionary<string, string> args)
    {
        var value = args.GetValueOrDefault(ElevatedArgs.EnvValue) ?? "";
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Machine);
        return ElevatedOperationResult.Ok(Id, $"Set machine variable '{name}'.");
    }
}
