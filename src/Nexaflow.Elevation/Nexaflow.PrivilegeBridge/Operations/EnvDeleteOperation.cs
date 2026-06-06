using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

internal sealed class EnvDeleteOperation : EnvOperationBase
{
    public override string Id => ElevatedOps.EnvDelete;

    protected override ElevatedOperationResult Apply(string name, IReadOnlyDictionary<string, string> args)
    {
        // Passing null removes the variable from the machine (registry) environment.
        Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Machine);
        return ElevatedOperationResult.Ok(Id, $"Deleted machine variable '{name}'.");
    }
}
