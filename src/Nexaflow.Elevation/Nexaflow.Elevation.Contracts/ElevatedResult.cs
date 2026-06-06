using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Elevation.Contracts;

/// <summary>Outcome of a single operation within a request.</summary>
public sealed class ElevatedOperationResult
{
    public string Op { get; set; } = "";
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public ElevatedErrorKind ErrorKind { get; set; } = ElevatedErrorKind.None;
    public Dictionary<string, string> Data { get; set; } = new();

    public static ElevatedOperationResult Ok(string op, string message) =>
        new() { Op = op, Success = true, Message = message };

    public static ElevatedOperationResult Fail(string op, ElevatedErrorKind kind, string message) =>
        new() { Op = op, Success = false, ErrorKind = kind, Message = message };
}

/// <summary>
/// Overall outcome returned by the privilege bridge (or synthesised by the launcher when the bridge
/// never ran — e.g. a declined UAC). <see cref="Operations"/> holds the per-operation detail.
/// </summary>
public sealed class ElevatedResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public ElevatedErrorKind ErrorKind { get; set; } = ElevatedErrorKind.None;
    public Dictionary<string, string> Data { get; set; } = new();
    public List<ElevatedOperationResult> Operations { get; set; } = new();

    public bool WasDeclined => ErrorKind == ElevatedErrorKind.UserDeclinedElevation;

    public static ElevatedResult Fail(ElevatedErrorKind kind, string message) =>
        new() { Success = false, ErrorKind = kind, Message = message };

    public static ElevatedResult Declined() =>
        Fail(ElevatedErrorKind.UserDeclinedElevation,
             "The action needs administrator approval and the request was declined.");

    /// <summary>Rolls a completed operation set up into an overall result (success = all succeeded).</summary>
    public static ElevatedResult FromOperations(List<ElevatedOperationResult> ops)
    {
        var failed = ops.FirstOrDefault(o => !o.Success);
        return new ElevatedResult
        {
            Operations = ops,
            Success    = failed is null,
            ErrorKind  = failed?.ErrorKind ?? ElevatedErrorKind.None,
            Message    = failed?.Message
                         ?? (ops.Count == 1 ? ops[0].Message : $"All {ops.Count} operations succeeded."),
        };
    }
}
