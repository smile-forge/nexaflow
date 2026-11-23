namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// One program run on a location's host, to its end and without a terminal. It is found by name on the host's login
/// <c>PATH</c>, as a person's shell there finds it, and handed its arguments exactly as given — no shell reads them, so a
/// name holding a space, a quote or a <c>$</c> reaches it whole. Nothing is on its stdin.
/// </summary>
/// <param name="Program">The program's name, or its path on the host.</param>
/// <param name="Arguments">Its arguments, as given.</param>
/// <param name="WorkingDirectory">The folder it starts in: a provider path to <see cref="ILocationShell"/>, a path of the
/// file system to <see cref="IProgramHost"/>.</param>
public sealed record ProgramRun(string Program, IReadOnlyList<string> Arguments, string WorkingDirectory)
{
    /// <summary>Variables laid over the program's environment, or null for none.</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
}

/// <summary>How a program ended, and what it wrote to its stdout and its stderr, each decoded as UTF-8.</summary>
public sealed record ProgramExit(int ExitCode, string Output, string Errors)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// The host of a location, running programs on a folder there — <c>git</c>, <c>dotnet</c> — as they run on that host, so
/// what they change keeps the names and permissions the host gives it, which a Windows program working through a folder
/// Windows shows the location as would not. Given by <see cref="IVirtualFileSystem.GetProgramHost"/>; every path it takes
/// is a path of that file system.
/// </summary>
public interface IProgramHost
{
    /// <summary>The location's name as people read it — what a message saying where a program ran names.</summary>
    string Label { get; }

    /// <summary>The host's own path of a path on this location, named by the location's path or by a Windows folder
    /// showing it; null for a path anywhere else. What a program is handed for a file it works on.</summary>
    string? HostPathOf(string path);

    /// <summary>This file system's path of a path the host names — one a program printed — so what is done to it goes
    /// through the location.</summary>
    string PathOnLocation(string hostPath);

    /// <summary>
    /// Runs the program in <see cref="ProgramRun.WorkingDirectory"/>, a path on this location, connecting first if need
    /// be. Each line it writes, to stdout or stderr, reaches <paramref name="lines"/> as it is written. Cancelling ends the
    /// program and what it started. Fails with <see cref="LocationException"/> — <see cref="LocationError.NotFound"/> when
    /// the program or the folder is not there — and with <see cref="ArgumentException"/> for a folder elsewhere.
    /// </summary>
    ValueTask<ProgramExit> RunProgramAsync(ProgramRun run, IProgress<string>? lines, CancellationToken ct);
}
