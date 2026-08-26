using System;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using NSubstitute;

namespace Nexaflow.Tests.Features.Solver;

/// <summary>Stand-ins for the two services the Solver is handed.</summary>
internal static class SolverTestDoubles
{
    /// <summary>An AI service that answers instantly, or refuses when <paramref name="answer"/> is null.</summary>
    public static IAIService Ai(string? answer = "**42**")
    {
        var ai = Substitute.For<IAIService>();
        ai.RunProblemSolvingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(_ => Task.FromResult(answer));
        return ai;
    }

    /// <summary>
    /// A shell whose <c>RunOnUiAsync</c> runs inline.
    /// <para>
    /// That is not a shortcut — it is what the real one does when it is already on the UI thread,
    /// and it is what lets a test observe the ViewModel's state without a dispatcher. Left
    /// unconfigured, the substitute returns a null Task and every await through it throws.
    /// </para>
    /// </summary>
    public static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();

        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(ci =>
        {
            ci.Arg<Action>()();
            return Task.CompletedTask;
        });

        return shell;
    }
}
