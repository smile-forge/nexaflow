using AngouriMath;

namespace Nexaflow.Features.Solver.Solving;

/// <summary>
/// The calculator: works out what a definition with no unknowns comes to.
/// <para>
/// Offered only for a constant expression. <c>2 + 2 * 3</c> has an answer; <c>4x + 3x</c> has a
/// simplification, which is a different chip, and offering <c>=</c> for it would promise a number
/// that does not exist.
/// </para>
/// </summary>
public sealed class EqualsSolver : ISolver
{
    /// <inheritdoc/>
    public string Id => "equals";

    /// <inheritdoc/>
    public string DisplayName => "Evaluate";

    /// <inheritdoc/>
    public int Order => 0;

    /// <inheritdoc/>
    public IReadOnlyList<SolverChip> CanSolve(SolverInput input)
    {
        if (!ExpressionParser.TryParse(input, input.AngleUnit, out var parsed)) return [];
        if (!parsed.IsConstant) return [];

        // Parsing is not enough — the evaluator can still refuse. The chip only appears once a
        // value has actually been produced, so pressing it can never come back empty-handed.
        if (!TryEvaluate(parsed.Entity, out var value)) return [];

        return
        [
            new SolverChip(Id, "eval", "=", "=", "Work out the value", new Payload(parsed, value)),
        ];
    }

    /// <inheritdoc/>
    public Task<SolverResult> SolveAsync(SolverChip chip, SolverInput input, CancellationToken ct)
    {
        if (chip.Payload is not Payload p)
            return Task.FromResult(SolverResult.Error("The definition changed — press the chip again."));

        var exact = ExactForm(p.Parsed.Entity, p.Value);
        var exactLatex = ExpressionParser.Latex(exact);
        var decimals = ExpressionParser.DecimalLatex(p.Value, input.Decimals);

        // Just the answer. The cell already prints the definition it was asked, so repeating it
        // here only makes "8" harder to find — pressing = on 5+3 should read 8, not 5+3 = 8.
        //
        // Three shapes, in order of how much there is to say:
        //   2 + 2 * 3   ->  8                       exact, nothing to approximate
        //   sin(pi/4)   ->  sqrt(2)/2 ≈ 0.707107    an exact form worth keeping, plus a value
        //   1/3         ->  0.333333                no compact exact form to show
        var answer =
            decimals is null || decimals == exactLatex ? exactLatex
            : IsPlainNumber(exact) ? decimals
            : $"{exactLatex} \\approx {decimals}";

        return Task.FromResult(new SolverResult($"$$\n{answer}\n$$"));
    }

    /// <summary>
    /// The tidiest exact form of the answer. <c>Simplify</c> turns <c>sin(pi/4)</c> into
    /// <c>√2/2</c>, which is the answer a person wanted; the evaluated value is a hundred digits of
    /// decimal and is only ever shown rounded.
    /// </summary>
    private static Entity ExactForm(Entity entity, Entity evaluated)
    {
        try
        {
            var simplified = entity.Simplify();
            return simplified.Nodes.Any(n => n is Entity.Variable) ? evaluated : simplified;
        }
        catch (Exception)
        {
            return evaluated;
        }
    }

    /// <summary>
    /// True when the exact form is already just a number, so printing it and then approximating it
    /// would say the same thing twice.
    /// </summary>
    private static bool IsPlainNumber(Entity entity)
    {
        try { return entity is Entity.Number; }
        catch (Exception) { return false; }
    }

    private static bool TryEvaluate(Entity entity, out Entity value)
    {
        try
        {
            value = entity.EvalNumerical();
            return true;
        }
        catch (Exception)
        {
            value = entity;
            return false;
        }
    }

    private sealed record Payload(ParsedExpression Parsed, Entity Value);
}
