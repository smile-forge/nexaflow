namespace Nexaflow.Elevation.Contracts;

/// <summary>
/// Why an elevated request did not succeed. <see cref="None"/> means success.
/// <see cref="UserDeclinedElevation"/> is deliberately distinct so callers can stay quiet on a UAC cancel
/// rather than surfacing it as an error.
/// </summary>
public enum ElevatedErrorKind
{
    None = 0,
    UserDeclinedElevation,
    BridgeNotFound,
    BridgeLaunchFailed,
    Timeout,
    HandshakeFailed,
    UnknownOperation,
    OperationFailed,
    Cancelled,
    Unexpected,
}
