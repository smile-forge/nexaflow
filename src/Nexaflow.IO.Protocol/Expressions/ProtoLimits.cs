namespace Nexaflow.IO.Protocol.Expressions;

/// <summary>Hard ceilings the engine imposes on any document, including one it did not write.</summary>
public static class ProtoLimits
{
    /// <summary>
    /// Evaluation-step budget for one expression.
    ///
    /// <para>
    /// Every iteration bound in the language is a literal — lists come from inputs, and the only generator
    /// is <c>range(n, max)</c> with a literal maximum — so termination is a property of the syntax and this
    /// budget should be unreachable. It exists as a backstop against a bug in that reasoning, not as the
    /// mechanism enforcing it. The distinction matters: if this is what stops a runaway transform, the
    /// totality argument was wrong and the fix belongs in the analysis, not in a bigger number here.
    /// </para>
    /// </summary>
    public const long DefaultFuel = 2_000_000;

    /// <summary>Largest literal a <c>range</c> maximum may declare, so a document cannot buy itself an
    /// arbitrarily long loop simply by writing a very large bound.</summary>
    public const long MaxRangeBound = 65_536;

    /// <summary>
    /// How many structures may be chained from one declaration.
    ///
    /// <para>
    /// Unlike the fuel budget this one is load-bearing, because on decode the answer to "is there another"
    /// comes from the data: a corrupt or hostile bound is otherwise an allocation request, and it arrives
    /// before anything has had a chance to judge the message.
    /// </para>
    /// </summary>
    public const long MaxChainedInstances = 65_536;
}
