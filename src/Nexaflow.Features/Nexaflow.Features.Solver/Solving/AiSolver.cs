using Nexaflow.Features.Common;

namespace Nexaflow.Features.Solver.Solving;

/// <summary>
/// The catch-all: hands the whole definition to the model.
/// <para>
/// Always offered while there is anything to solve, because this is the only solver that can read a
/// word problem, an equation the engine declined, or a question phrased as prose. Two chips —
/// straight to the answer, or with the working shown — because those are genuinely different asks
/// and the prompt for each is different.
/// </para>
/// </summary>
/// <param name="ai">The active workspace's AI service, injected into the registration.</param>
public sealed class AiSolver(IAIService ai) : ISolver
{
    private const string AnswerPrompt =
        """
        You are a mathematics solver embedded in a desktop application.

        Give ONLY the answer. Nothing else.

        There is a separate "Solve by steps" action for working, so working here is not brevity the
        user has to skim past — it is content they explicitly did not ask for. Do not show your
        method, do not explain how you got there, do not restate the question, do not add a
        "therefore" or a summary line, and do not append the steps after the answer.

        Output the answer alone: usually a single display-maths line, occasionally a single short
        sentence where the answer is not an expression. If a caveat genuinely changes whether the
        answer is right (a domain restriction, an assumed branch), add at most one short line for it.

        Format every mathematical expression as LaTeX: inline as $...$ and displayed as $$...$$.
        The application typesets these. Never write maths as plain text or as a code block.

        If the input is not a mathematical problem, say so in one sentence rather than inventing one.
        If it is ambiguous, state the reading you took and answer that.
        """;

    private const string StepsPrompt =
        """
        You are a mathematics tutor embedded in a desktop application.

        Work the problem the user gives you through step by step. Number the steps. For each one,
        show the expression as it stands and say briefly which rule or identity moved it on. End
        with the answer on its own line, clearly marked.

        Format every mathematical expression as LaTeX: inline as $...$ and displayed as $$...$$.
        The application typesets these. Never write maths as plain text or as a code block.

        Show the working even where the answer is obvious — being asked for steps is the whole
        request. If the input is not a mathematical problem, say so in one sentence.
        """;

    /// <inheritdoc/>
    public string Id => "ai";

    /// <inheritdoc/>
    public string DisplayName => "AI";

    /// <inheritdoc/>
    public int Order => 90;

    /// <inheritdoc/>
    public IReadOnlyList<SolverChip> CanSolve(SolverInput input)
    {
        if (input.IsEmpty) return [];

        return
        [
            new SolverChip(Id, "solve", "Solve", "✦", "Ask the AI to solve this"),
            new SolverChip(Id, "steps", "Solve by steps", "✦", "Ask the AI to show its working"),
        ];
    }

    /// <inheritdoc/>
    public async Task<SolverResult> SolveAsync(SolverChip chip, SolverInput input, CancellationToken ct)
    {
        var system = chip.Id == "steps" ? StepsPrompt : AnswerPrompt;

        try
        {
            var answer = await ai.RunProblemSolvingAsync(system, BuildUserPrompt(input), ct);

            if (string.IsNullOrWhiteSpace(answer))
                return SolverResult.Error(
                    "No answer came back. Check that a model is assigned to **Problem Solving** or " +
                    "**Analysis** in Manage AI.");

            return new SolverResult(answer.Trim());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return SolverResult.Error($"The AI could not be reached: {e.Message}");
        }
    }

    /// <summary>
    /// Tells the model which editor the text came from, because the same characters mean different
    /// things in each: a LaTeX definition is a formula, a Text one may be a question about it.
    /// </summary>
    private static string BuildUserPrompt(SolverInput input) => input.Mode switch
    {
        DefinitionMode.Calc =>
            $"Evaluate this expression.\n\n```\n{input.Trimmed}\n```",

        DefinitionMode.Latex =>
            $"Here is a LaTeX expression.\n\n```latex\n{input.Trimmed}\n```",

        _ => input.Trimmed,
    };
}
