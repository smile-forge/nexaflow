using System.Text;
using AngouriMath;
using AngouriMath.Core.Transformations;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Solver.Solving;

/// <summary>
/// Rearranging a formula without changing what it means: simplify, factor, and the working that
/// gets from one to the other.
/// </summary>
public sealed class AlgebraSolver : ISolver
{
    /// <summary>
    /// Above this the factoriser is documented to decline, so the chip is not offered rather than
    /// offered and then refused.
    /// </summary>
    private const int MaxFactorDegree = 32;

    /// <inheritdoc/>
    public string Id => "algebra";

    /// <inheritdoc/>
    public string DisplayName => Str.Get("Solver.Name.Algebra");

    /// <inheritdoc/>
    public int Order => 10;

    /// <inheritdoc/>
    public IReadOnlyList<SolverChip> CanSolve(SolverInput input)
    {
        if (!ExpressionParser.TryParse(input, input.AngleUnit, out var parsed)) return [];

        var chips = new List<SolverChip>(3);

        if (!parsed.IsConstant)
        {
            chips.Add(new SolverChip(Id, "simplify", Str.Get("Solver.Chip.Simplify"), "≡",
                Str.Get("Solver.Chip.SimplifyTip"), parsed));

            chips.Add(new SolverChip(Id, "steps", Str.Get("Solver.Chip.Steps"), "⋯",
                Str.Get("Solver.Chip.StepsTip"), parsed));

            // Factorisation is univariate over the rationals. Offering it for anything else would
            // produce a refusal rather than an answer, so the shape is checked up front.
            if (parsed.Variables.Count == 1)
                chips.Add(new SolverChip(Id, "factor", Str.Get("Solver.Chip.Factor"), "×",
                    Str.Get("Solver.Chip.FactorTip"), parsed));
        }
        else if (TryWholeNumber(parsed.Entity, out var n) && n > 1)
        {
            chips.Add(new SolverChip(Id, "factorint", Str.Get("Solver.Chip.Factor"), "×",
                Str.Get("Solver.Chip.FactorIntegerTip"), parsed));
        }

        return chips;
    }

    /// <inheritdoc/>
    public Task<SolverResult> SolveAsync(SolverChip chip, SolverInput input, CancellationToken ct)
    {
        if (chip.Payload is not ParsedExpression parsed)
            return Task.FromResult(SolverResult.Error(Str.Get("Solver.Result.DefinitionChanged")));

        return Task.FromResult(chip.Id switch
        {
            "simplify" => Simplify(parsed),
            "factor" => Factor(parsed),
            "factorint" => FactorInteger(parsed),
            "steps" => Steps(parsed),
            _ => SolverResult.Error(Str.Format("Solver.Result.UnknownAlgebraFormat", chip.Id)),
        });
    }

    private static SolverResult Simplify(ParsedExpression parsed)
    {
        try
        {
            var simplified = parsed.Entity.Simplify();
            var before = ExpressionParser.Latex(parsed.ForDisplay);
            var after = ExpressionParser.Latex(simplified);

            return before == after
                ? new SolverResult($"$$\n{after}\n$$\n\n{Str.Get("Solver.Result.AlreadySimplest")}")
                : new SolverResult(ExpressionParser.WithProviso($"$$\n{before} = {after}\n$$", simplified));
        }
        catch (Exception e)
        {
            return SolverResult.Error(Str.Format("Solver.Result.SimplifyFailedFormat", e.Message));
        }
    }

    private static SolverResult Factor(ParsedExpression parsed)
    {
        var variable = parsed.Variables[0];

        try
        {
            var factored = MathS.Polynomials.Factor(parsed.Entity, variable);
            if (factored is null)
                return SolverResult.Error(
                    Str.Format("Solver.Result.NotAPolynomialFormat", variable.Name, MaxFactorDegree));

            var before = ExpressionParser.Latex(parsed.ForDisplay);
            var after = ExpressionParser.Latex(factored);

            return before == after
                ? new SolverResult($"$$\n{after}\n$$\n\n{Str.Get("Solver.Result.Irreducible")}")
                : new SolverResult($"$$\n{before} = {after}\n$$");
        }
        catch (Exception e)
        {
            return SolverResult.Error(Str.Format("Solver.Result.FactoriseFailedFormat", e.Message));
        }
    }

    private static SolverResult FactorInteger(ParsedExpression parsed)
    {
        try
        {
            if (!TryWholeNumber(parsed.Entity, out var n))
                return SolverResult.Error(Str.Get("Solver.Result.NotWholeNumber"));

            var parts = MathS.NumberTheory.Factorize(n).ToList();
            if (parts.Count == 0) return SolverResult.Error(Str.Get("Solver.Result.NoPrimeFactorisation"));

            var product = string.Join(" \\times ", parts.Select(p =>
                p.power == 1 ? p.prime.ToString() : $"{p.prime}^{{{p.power}}}"));

            var sb = new StringBuilder();
            sb.Append("$$\n").Append(n).Append(" = ").Append(product).Append("\n$$");

            if (parts.Count == 1 && parts[0].power == 1)
                sb.Append("\n\n").Append(Str.Get("Solver.Result.Prime"));

            return new SolverResult(sb.ToString());
        }
        catch (Exception e)
        {
            return SolverResult.Error(Str.Format("Solver.Result.FactoriseFailedFormat", e.Message));
        }
    }

    /// <summary>
    /// The working, taken from the engine rather than narrated: each stage is an edge the simplifier
    /// actually traversed, named by the identity that did it.
    /// </summary>
    private static SolverResult Steps(ParsedExpression parsed)
    {
        try
        {
            var path = DerivationPath.OfSimplifying(parsed.Entity);
            if (path is null)
                return SolverResult.Error(Str.Get("Solver.Result.NoDerivation"));

            if (path.Steps.Count == 0)
                return new SolverResult($"$$\n{ExpressionParser.Latex(path.Result)}\n$$\n\n{Str.Get("Solver.Result.NoStepsToTake")}");

            var sb = new StringBuilder();
            sb.Append("$$\n\\begin{aligned}\n");
            sb.Append("  & ").Append(ExpressionParser.Latex(path.Input)).Append(" \\\\\n");

            foreach (var step in path.Steps)
            {
                sb.Append("= \\; & ").Append(ExpressionParser.Latex(step.After))
                  .Append(" && \\text{").Append(LatexEscape(step.Name)).Append("} \\\\\n");
            }

            sb.Append("\\end{aligned}\n$$");

            if (path.ExpressionsExplored > path.Steps.Count + 1)
                sb.Append("\n\n").Append(Str.Format("Solver.Result.ExploredFormat", path.ExpressionsExplored));

            return new SolverResult(sb.ToString());
        }
        catch (Exception e)
        {
            return SolverResult.Error(Str.Format("Solver.Result.DerivationFailedFormat", e.Message));
        }
    }

    /// <summary>A rule name is engine text; it must not be able to break out of <c>\text{}</c>.</summary>
    private static string LatexEscape(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        foreach (var c in name)
        {
            if (c is '\\' or '{' or '}' or '$' or '&' or '#' or '_' or '%' or '^' or '~')
            {
                sb.Append(' ');
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool TryWholeNumber(Entity entity, out int value)
    {
        value = 0;
        try
        {
            if (entity.Evaled is not Entity.Number.Integer i) return false;
            if (i > int.MaxValue || i < int.MinValue) return false;
            value = (int)i;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

}
