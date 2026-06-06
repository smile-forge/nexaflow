using System.Reflection;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>
/// Maps operation ids to handlers, discovered by reflection over this assembly. An unknown id is
/// rejected (never guessed); a handler that throws is contained as a failed result.
/// </summary>
internal sealed class OperationRegistry
{
    private readonly Dictionary<string, IElevatedOperation> _ops =
        Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IElevatedOperation).IsAssignableFrom(t))
            .Select(t => (IElevatedOperation)Activator.CreateInstance(t)!)
            .ToDictionary(o => o.Id, StringComparer.OrdinalIgnoreCase);

    public ElevatedOperationResult Run(ElevatedOperation op)
    {
        if (!_ops.TryGetValue(op.Op, out var handler))
            return ElevatedOperationResult.Fail(op.Op, ElevatedErrorKind.UnknownOperation,
                $"Unknown elevated operation '{op.Op}'.");

        try
        {
            return handler.Execute(op.Args);
        }
        catch (Exception ex)
        {
            return ElevatedOperationResult.Fail(op.Op, ElevatedErrorKind.OperationFailed, ex.Message);
        }
    }
}
