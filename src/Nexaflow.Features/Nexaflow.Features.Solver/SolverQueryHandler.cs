using Nexaflow.Features.Common;
using Nexaflow.Features.Solver.Solving;
using Nexaflow.Features.Solver.ViewModels;

namespace Nexaflow.Features.Solver;

/// <summary>
/// Sends <c>?…</c> from the AI bar straight into the Solver.
/// <para>
/// Typing <c>?3+5</c> puts <c>3+5</c> in the Text tab and leaves the chips to offer what they make
/// of it. The point is that the shortest path from "I have a sum" to "I have an answer" should not
/// require finding the tab first — the AI bar is always there, so it becomes the way in.
/// </para>
/// <para>
/// It only claims input that is actually prefixed. Un-prefixed text scores zero: the AI bar belongs
/// to the assistant, and a handler that grabbed every arithmetic-looking phrase would take questions
/// the user meant to ask it.
/// </para>
/// </summary>
public sealed class SolverQueryHandler(IShellServices shell) : IQueryHandler
{
    /// <inheritdoc/>
    public string Description =>
        "Opens the Solver on an expression. Use for a calculation, formula or maths question typed after '?'.";

    /// <inheritdoc/>
    public string? Symbol => "?";

    /// <inheritdoc/>
    public float CanProcess(string input, bool prefixed, IPageViewModel? pageVm = null)
        => prefixed && !string.IsNullOrWhiteSpace(input) ? 1f : 0f;

    /// <inheritdoc/>
    public async Task<string?> ProcessAsync(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        var problem = input.Trim();
        if (problem.Length == 0) return null;

        // Re-uses the open Solver when there is one, so a second '?' lands on the surface already in
        // front of the user rather than starting again somewhere else. OpenTab returns nothing, so
        // the tab is looked up again afterwards.
        await shell.RunOnUiAsync(() =>
        {
            if (shell.FindTab(SolverTabRegistration.StaticPageKind) is null)
                shell.OpenTab(SolverTabRegistration.StaticPageKind);

            if (shell.FindTab(SolverTabRegistration.StaticPageKind) is not { } page) return;
            if (page.GetOrCreateContent() is not Views.SolverView { ViewModel: SolverViewModel vm }) return;

            vm.Mode = DefinitionMode.Text;
            vm.DefinitionText = problem;
        }).ConfigureAwait(false);

        // Silent: the Solver itself is the answer to this, and a chat bubble repeating the input
        // would just be noise beside it.
        return null;
    }
}
