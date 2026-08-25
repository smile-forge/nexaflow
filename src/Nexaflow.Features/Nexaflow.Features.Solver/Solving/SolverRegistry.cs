namespace Nexaflow.Features.Solver.Solving;

/// <summary>
/// Holds the solvers and asks each of them what it can offer for the current definition.
/// <para>
/// The isolation here is deliberate. <see cref="ISolver.CanSolve"/> runs on every keystroke against
/// half-typed input, which is exactly the shape that provokes a parser into throwing; one solver
/// doing so must not empty the chip strip for the rest. Same for <see cref="SolveAsync"/> — a
/// solver that throws produces an error cell, never an unhandled exception on a background thread.
/// </para>
/// </summary>
public sealed class SolverRegistry
{
    private readonly IReadOnlyList<ISolver> _solvers;

    /// <summary>Builds a registry over the given solvers, ordered by <see cref="ISolver.Order"/>.</summary>
    public SolverRegistry(IEnumerable<ISolver> solvers)
        => _solvers = solvers.OrderBy(s => s.Order).ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();

    /// <summary>The solvers, in chip-strip order.</summary>
    public IReadOnlyList<ISolver> Solvers => _solvers;

    /// <summary>
    /// Every chip on offer for <paramref name="input"/>, in strip order. Safe to call on a
    /// background thread; never throws.
    /// </summary>
    public IReadOnlyList<SolverChip> ChipsFor(SolverInput input)
    {
        if (input.IsEmpty) return [];

        var chips = new List<SolverChip>();
        foreach (var solver in _solvers)
        {
            try
            {
                chips.AddRange(solver.CanSolve(input));
            }
            catch (Exception)
            {
                // A solver that cannot read this input has nothing to offer for it, which is the
                // same outcome as returning empty. Half-typed input reaches here constantly.
            }
        }

        return chips;
    }

    /// <summary>
    /// Runs one chip. Returns an error result rather than throwing, except for cancellation, which
    /// is the caller's to handle.
    /// </summary>
    public async Task<SolverResult> SolveAsync(SolverChip chip, SolverInput input, CancellationToken ct)
    {
        var solver = _solvers.FirstOrDefault(s => s.Id == chip.SolverId);
        if (solver is null) return SolverResult.Error($"No solver is registered for '{chip.SolverId}'.");

        try
        {
            return await solver.SolveAsync(chip, input, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return SolverResult.Error($"{solver.DisplayName} failed: {e.Message}");
        }
    }

    /// <summary>The default set, in the order they appear under the definition area.</summary>
    public static SolverRegistry CreateDefault(Nexaflow.Features.Common.IAIService ai) => new(
    [
        new EqualsSolver(),
        new AlgebraSolver(),
        new CalculusSolver(),
        new StatsSolver(),
        new AiSolver(ai),
    ]);
}
