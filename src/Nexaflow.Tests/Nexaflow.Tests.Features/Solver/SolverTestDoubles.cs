using System;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using NSubstitute;

namespace Nexaflow.Tests.Features.Solver;

/// <summary>Stand-ins for the two services the Solver is handed.</summary>
internal static class SolverTestDoubles
{
    /// <summary>Held while the shell's UI work runs, as the UI thread is: what a test reads under it is never half changed.</summary>
    public static Lock Ui { get; } = new();

    /// <summary>An AI service that answers instantly, or refuses when <paramref name="answer"/> is null.</summary>
    public static IAIService Ai(string? answer = "**42**")
    {
        var ai = Substitute.For<IAIService>();
        ai.RunProblemSolvingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(_ => Task.FromResult(answer));
        return ai;
    }

    /// <summary>
    /// A shell whose <c>RunOnUiAsync</c> runs inline, one action at a time under <see cref="Ui"/>.
    /// <para>
    /// Inline is what the real one does when it is already on the UI thread, and it is what lets a test observe the
    /// ViewModel's state without a dispatcher; one at a time under <see cref="Ui"/> is what the one UI thread makes of
    /// work handed to it from a background one, so a test reading the ViewModel under <see cref="Ui"/> never sees a
    /// collection half rewritten. Left unconfigured, the substitute returns a null Task and every await through it throws.
    /// </para>
    /// </summary>
    public static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();

        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(ci =>
        {
            lock (Ui) ci.Arg<Action>()();
            return Task.CompletedTask;
        });

        return shell;
    }
}
