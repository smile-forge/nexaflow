using AngouriMath;

namespace Nexaflow.Features.Solver.Solving;

/// <summary>
/// Differentiation and integration, one chip per free variable — so <c>x y + y</c> offers
/// <c>d/dx</c> and <c>d/dy</c> rather than guessing which was meant.
/// </summary>
public sealed class CalculusSolver : ISolver
{
    /// <summary>
    /// Past this the chip strip stops being a set of choices and becomes a wall. An expression in
    /// more variables than this is better served by the AI chips.
    /// </summary>
    private const int MaxVariableChips = 3;

    /// <inheritdoc/>
    public string Id => "calculus";

    /// <inheritdoc/>
    public string DisplayName => "Calculus";

    /// <inheritdoc/>
    public int Order => 20;

    /// <inheritdoc/>
    public IReadOnlyList<SolverChip> CanSolve(SolverInput input)
    {
        if (!ExpressionParser.TryParse(input, input.AngleUnit, out var parsed)) return [];
        if (parsed.IsConstant) return [];

        var chips = new List<SolverChip>(MaxVariableChips * 2);

        foreach (var v in parsed.Variables.Take(MaxVariableChips))
        {
            chips.Add(new SolverChip(Id, $"d/{v.Name}", $"d/d{v.Name}", "∂",
                $"Differentiate with respect to {v.Name}", new Payload(parsed, v)));
        }

        foreach (var v in parsed.Variables.Take(MaxVariableChips))
        {
            chips.Add(new SolverChip(Id, $"int/{v.Name}", $"∫ d{v.Name}", "∫",
                $"Integrate with respect to {v.Name}", new Payload(parsed, v)));
        }

        return chips;
    }

    /// <inheritdoc/>
    public Task<SolverResult> SolveAsync(SolverChip chip, SolverInput input, CancellationToken ct)
    {
        if (chip.Payload is not Payload p)
            return Task.FromResult(SolverResult.Error("The definition changed — press the chip again."));

        return Task.FromResult(chip.Id.StartsWith("d/", StringComparison.Ordinal)
            ? Differentiate(p)
            : Integrate(p));
    }

    private static SolverResult Differentiate(Payload p)
    {
        try
        {
            var derivative = Tidy(p.Parsed.Entity.Differentiate(p.Variable));
            var body =
                $"$$\n\\frac{{d}}{{d{p.Variable.Name}}}\\left({ExpressionParser.Latex(p.Parsed.ForDisplay)}\\right) " +
                $"= {ExpressionParser.Latex(derivative)}\n$$";

            return new SolverResult(ExpressionParser.WithProviso(body, derivative));
        }
        catch (Exception e)
        {
            return SolverResult.Error($"Could not differentiate that: {e.Message}");
        }
    }

    /// <summary>
    /// Full simplification rather than <c>InnerSimplified</c>. The cheap pass leaves results like
    /// <c>(2x · 2)/4 + 3</c> where the answer is <c>x + 3</c> — correct, but nobody would write it
    /// down that way, and a derivative is read as much as it is used.
    /// </summary>
    private static Entity Tidy(Entity entity)
    {
        try { return entity.Simplify(); }
        catch (Exception) { return entity.InnerSimplified; }
    }

    private static SolverResult Integrate(Payload p)
    {
        try
        {
            var integral = Tidy(p.Parsed.Entity.Integrate(p.Variable));

            // An integral the engine cannot do comes back as an unevaluated Integralf rather than
            // as a failure. Reporting that honestly matters more than rendering it: the symbol
            // typesets perfectly well and would read as an answer.
            if (ContainsUnevaluatedIntegral(integral))
                return SolverResult.Error(
                    $"No closed form found for $\\int {ExpressionParser.Latex(p.Parsed.ForDisplay)} \\; d{p.Variable.Name}$. " +
                    "Not every integral has one — **Solve by steps** will try.");

            var body =
                $"$$\n\\int {ExpressionParser.Latex(p.Parsed.ForDisplay)} \\; d{p.Variable.Name} " +
                $"= {ExpressionParser.Latex(integral)}\n$$";

            return new SolverResult(ExpressionParser.WithProviso(body, integral));
        }
        catch (Exception e)
        {
            return SolverResult.Error($"Could not integrate that: {e.Message}");
        }
    }

    /// <summary>True when any part of the result is still an integral the engine declined to do.</summary>
    private static bool ContainsUnevaluatedIntegral(Entity entity)
    {
        try
        {
            return entity.Nodes.Any(n => n is Entity.Integralf);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private sealed record Payload(ParsedExpression Parsed, Entity.Variable Variable);
}
