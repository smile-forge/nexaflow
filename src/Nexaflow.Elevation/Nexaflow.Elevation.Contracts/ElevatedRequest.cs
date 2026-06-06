using System.Collections.Generic;

namespace Nexaflow.Elevation.Contracts;

/// <summary>One admin-only action: an <see cref="ElevatedOps"/> id plus its string-keyed arguments.</summary>
public sealed class ElevatedOperation
{
    public string Op { get; set; } = "";
    public Dictionary<string, string> Args { get; set; } = new();
}

/// <summary>
/// A batch of operations run under a single elevation (one UAC prompt). The common case is one
/// operation — use <see cref="Single"/>. Batching lets a multi-step action avoid re-prompting.
/// </summary>
public sealed class ElevatedRequest
{
    public List<ElevatedOperation> Operations { get; set; } = new();

    public static ElevatedRequest Single(string op, IReadOnlyDictionary<string, string> args) =>
        new() { Operations = { new ElevatedOperation { Op = op, Args = new(args) } } };

    public static ElevatedRequest Single(string op, params (string Key, string Value)[] args)
    {
        var d = new Dictionary<string, string>(args.Length);
        foreach (var (k, v) in args) d[k] = v;
        return new() { Operations = { new ElevatedOperation { Op = op, Args = d } } };
    }
}
