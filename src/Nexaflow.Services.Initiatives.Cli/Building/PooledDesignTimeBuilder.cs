using Nexaflow.Syntax.Compiler;

namespace Nexaflow.Services.Initiatives.Cli.Building;

/// <summary>
/// Design-time builds — what the compile host asks to learn a project's compiler arguments — on the resident process's
/// workers rather than in an MSBuild started cold for each: a quarter of a second where a process of its own takes a
/// second, and a fresh working tree asks it of every project it loads.
/// <para>
/// The pool it is handed is the design-time one, not the one <c>nfi build</c> uses: an edit check waits on its answer,
/// and must not queue behind a build of the same tree that takes minutes.
/// </para>
/// </summary>
internal sealed class PooledDesignTimeBuilder(BuildPool pool, string codeRoot) : IDesignTimeBuilder
{
    public (IReadOnlyList<string>? Args, string? Error) CommandLineOf(string csproj, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(DesignTimeRequest.Deadline);

        var report = pool.Build(new BuildOrder(codeRoot, [csproj], [DesignTimeRequest.Target], DesignTimeRequest.Properties,
                                               Restore: false) { Items = [DesignTimeRequest.CommandLineItem] },
                                deadline.Token);

        if (report.Problem is { } problem)
            return (null, cancellation.IsCancellationRequested ? "cancelled"
                        : deadline.IsCancellationRequested ? $"its design-time build took over {DesignTimeRequest.Deadline.TotalMinutes:F0} minutes"
                        : problem);

        if (report.Projects is not [{ Succeeded: true } built])
            return (null, report.Diagnostics.FirstOrDefault(d => d.Severity == "error") is { } error
                ? $"{error.File}({error.Line},{error.Column}): error {error.Code}: {error.Message}"
                : "its design-time build failed");

        return built.Items?.GetValueOrDefault(DesignTimeRequest.CommandLineItem) is { Count: > 0 } args
            ? (args, null)
            : (null, "its design-time build produced no compiler command line");
    }
}
