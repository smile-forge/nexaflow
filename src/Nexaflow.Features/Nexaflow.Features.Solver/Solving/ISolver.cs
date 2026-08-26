namespace Nexaflow.Features.Solver.Solving;

/// <summary>Which editor the definition area is showing, and therefore how its text should be read.</summary>
public enum DefinitionMode
{
    /// <summary>A calculator line: numbers, operators and function names.</summary>
    Calc,

    /// <summary>Raw LaTeX. The <c>$$</c> fences are the view's business, never the solver's.</summary>
    Latex,

    /// <summary>Free markdown prose. Only the AI solvers can make sense of it.</summary>
    Text,
}

/// <summary>
/// The definition exactly as the user left it, plus everything needed to read it.
/// <para>
/// The reading options live here rather than on the solver so that every solver stays a pure
/// function of its input — which is what lets the whole engine be tested without a config, a
/// dispatcher or a shell.
/// </para>
/// </summary>
/// <param name="Mode">Which editor produced <paramref name="Text"/>.</param>
/// <param name="Text">The definition, unfenced and untrimmed.</param>
/// <param name="AngleUnit">How to read an angle — follows the palette's DEG/RAD toggle.</param>
/// <param name="Decimals">Places to round a decimal answer to.</param>
public sealed record SolverInput(
    DefinitionMode Mode,
    string Text,
    AngleUnit AngleUnit = AngleUnit.Radians,
    int Decimals = 6)
{
    /// <summary>The definition with surrounding whitespace gone — what every solver actually parses.</summary>
    public string Trimmed { get; } = Text.Trim();

    /// <summary>Nothing to solve.</summary>
    public bool IsEmpty => Trimmed.Length == 0;
}

/// <summary>
/// One action a solver is offering for the current definition, rendered as a chip under the
/// definition area.
/// </summary>
/// <param name="SolverId">The <see cref="ISolver.Id"/> that produced this chip.</param>
/// <param name="Id">Stable within the solver — <c>"simplify"</c>, <c>"d/x"</c>. Used for ordering and tests.</param>
/// <param name="Label">What the chip reads, e.g. <c>"simplify"</c> or <c>"d/dx"</c>.</param>
/// <param name="Glyph">A short leading symbol, or empty for none.</param>
/// <param name="Description">Tooltip: one line saying what pressing it will do.</param>
/// <param name="Payload">
/// Whatever the solver parsed while deciding it could offer this, handed straight back to
/// <see cref="ISolver.SolveAsync"/>. It exists so the expression is parsed once per keystroke rather
/// than again per press.
/// </param>
public sealed record SolverChip(
    string SolverId,
    string Id,
    string Label,
    string Glyph,
    string Description,
    object? Payload = null)
{
    /// <summary>Unique across every solver — what the view keys chips by.</summary>
    public string Key => $"{SolverId}.{Id}";
}

/// <summary>What a solver produced: markdown destined for a result cell.</summary>
/// <param name="Markdown">
/// The result, as markdown. Maths belongs in <c>$$…$$</c> — the shared renderer typesets it.
/// </param>
/// <param name="IsError">
/// True when this reports a failure rather than an answer. The cell styles it differently; it is
/// still a normal result, not an exception.
/// </param>
public sealed record SolverResult(string Markdown, bool IsError = false)
{
    /// <summary>A failure the user should see, phrased for them rather than for a log.</summary>
    public static SolverResult Error(string message) => new(message, IsError: true);
}

/// <summary>
/// Something that can recognise a kind of problem and answer it.
/// <para>
/// Implementations are pure and WPF-free: they take text, they return markdown. That is what lets
/// every one of them be unit-tested without a dispatcher, and it is worth preserving.
/// </para>
/// </summary>
public interface ISolver
{
    /// <summary>Stable id, lowercase — <c>"equals"</c>, <c>"algebra"</c>. Appears in <see cref="SolverChip.Key"/>.</summary>
    string Id { get; }

    /// <summary>Human name, for the tooltip and the AI's view of the page.</summary>
    string DisplayName { get; }

    /// <summary>Where this solver's chips sit in the strip. Lower is further left.</summary>
    int Order { get; }

    /// <summary>
    /// The chips this solver offers for <paramref name="input"/>, or empty when it has nothing to
    /// contribute.
    /// <para>
    /// Called on every keystroke (debounced), so it must be cheap and must not throw — the registry
    /// treats a throw as "no chips", but a solver that parses defensively gives a better answer than
    /// one that relies on that.
    /// </para>
    /// </summary>
    IReadOnlyList<SolverChip> CanSolve(SolverInput input);

    /// <summary>
    /// Answer one of this solver's own chips. Runs off the UI thread; may be cancelled when the
    /// user removes the cell or closes the tab.
    /// </summary>
    Task<SolverResult> SolveAsync(SolverChip chip, SolverInput input, CancellationToken ct);
}
