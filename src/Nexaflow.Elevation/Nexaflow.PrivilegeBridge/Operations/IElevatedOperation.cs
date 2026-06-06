using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>
/// One admin-only action the bridge can perform. Add a new elevated capability by implementing this
/// interface — <see cref="OperationRegistry"/> discovers it by reflection, no wiring required.
/// </summary>
public interface IElevatedOperation
{
    /// <summary>The <see cref="ElevatedOps"/> id this handler answers to.</summary>
    string Id { get; }

    /// <summary>Performs the action. Must not throw for an expected failure — return a failed result.</summary>
    ElevatedOperationResult Execute(IReadOnlyDictionary<string, string> args);
}
