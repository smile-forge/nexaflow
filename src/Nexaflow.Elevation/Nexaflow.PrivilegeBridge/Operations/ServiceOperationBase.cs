using System.ServiceProcess;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>
/// Shared plumbing for service-control handlers: validates the service name, opens a
/// <see cref="ServiceController"/>, and turns thrown control errors into a clean failed result.
/// </summary>
internal abstract class ServiceOperationBase : IElevatedOperation
{
    protected static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    public abstract string Id { get; }

    public ElevatedOperationResult Execute(IReadOnlyDictionary<string, string> args)
    {
        var name = args.GetValueOrDefault(ElevatedArgs.ServiceName);
        if (string.IsNullOrWhiteSpace(name))
            return ElevatedOperationResult.Fail(Id, ElevatedErrorKind.OperationFailed, "Missing service name.");

        try
        {
            using var sc = new ServiceController(name);
            var result = Run(sc, name, args);
            Stamp(result, sc);
            return result;
        }
        catch (Exception ex)
        {
            // ServiceController surfaces "not found" / access / control failures as InvalidOperationException
            // (often wrapping a Win32Exception). Report the most specific message we have.
            return ElevatedOperationResult.Fail(Id, ElevatedErrorKind.OperationFailed,
                $"'{name}': {(ex.InnerException ?? ex).Message}");
        }
    }

    protected abstract ElevatedOperationResult Run(
        ServiceController sc, string name, IReadOnlyDictionary<string, string> args);

    /// <summary>Stamps the service's current status/start-type onto the result for the caller to refresh its row.</summary>
    private static void Stamp(ElevatedOperationResult result, ServiceController sc)
    {
        try { sc.Refresh(); result.Data["status"] = sc.Status.ToString(); } catch { /* may have vanished */ }
        try { result.Data["startType"] = sc.StartType.ToString(); } catch { /* not always readable */ }
    }

    protected ElevatedOperationResult Ok(string message) => ElevatedOperationResult.Ok(Id, message);

    protected ElevatedOperationResult Fail(string message) =>
        ElevatedOperationResult.Fail(Id, ElevatedErrorKind.OperationFailed, message);
}
