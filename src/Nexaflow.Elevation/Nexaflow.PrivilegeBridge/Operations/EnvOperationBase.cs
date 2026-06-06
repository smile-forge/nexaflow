using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>Shared validation + change-broadcast for machine-scope environment-variable handlers.</summary>
internal abstract class EnvOperationBase : IElevatedOperation
{
    public abstract string Id { get; }

    public ElevatedOperationResult Execute(IReadOnlyDictionary<string, string> args)
    {
        var name = args.GetValueOrDefault(ElevatedArgs.EnvName);
        if (string.IsNullOrWhiteSpace(name))
            return ElevatedOperationResult.Fail(Id, ElevatedErrorKind.OperationFailed, "Missing variable name.");

        var result = Apply(name, args);
        if (result.Success)
            EnvironmentBroadcast.Notify();
        return result;
    }

    protected abstract ElevatedOperationResult Apply(string name, IReadOnlyDictionary<string, string> args);
}
