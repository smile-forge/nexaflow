using Nexaflow.Features.Common;
using Nexaflow.Features.Solver.Solving;
using Nexaflow.Features.Solver.ViewModels;

namespace Nexaflow.Features.Solver;

/// <summary>
/// Sends <c>?…</c> from the AI bar into the Solver, while the Solver is the page in front.
/// <para>
/// Typing <c>?3+5</c> there puts <c>3+5</c> in the Text tab and leaves the chips to offer what they
/// make of it — the bar is a faster way back to the definition than the tab is, once the tab is open.
/// </para>
/// <para>
/// It claims only prefixed input, and only on its own page. Un-prefixed text scores zero because the
/// AI bar belongs to the assistant, and a handler that grabbed every arithmetic-looking phrase would
/// take questions the user meant to ask it. The page gate is the other half of the same idea:
/// <c>?</c> is the shell-wide search route, so claiming it everywhere does not open the Solver from
/// anywhere — it ties with search and opens nothing at all.
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
    /// <remarks>
    /// Gated on the Solver's own page, like every other symbol handler is gated on its page type
    /// (Console on the terminal, FileSystem on the browser). A symbol is only unambiguous while one
    /// handler can claim it for a given page: <c>?</c> is also the shell's search route, and a handler
    /// that claimed every prefixed string took a tie with it on every searchable page — two 1.0 scores,
    /// no clear winner, so the bar showed no symbol and the query fell through to disambiguation. From
    /// the outside that is a "?" that silently does nothing, which is what shipped after the Solver
    /// landed.
    /// </remarks>
    public float CanProcess(string input, bool prefixed, IPageViewModel? pageVm = null)
        => pageVm is SolverViewModel && prefixed && !string.IsNullOrWhiteSpace(input) ? 1f : 0f;

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
