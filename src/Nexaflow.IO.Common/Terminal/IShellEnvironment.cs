namespace Nexaflow.IO.Common.Terminal;

/// <summary>Which of a shell's environments is read: as the shell holds it now, or as a new shell starts with it.</summary>
public enum ShellEnvironmentView
{
    /// <summary>The variables the shell holds now, as its latest prompt left them — what <c>env</c> prints in it.</summary>
    Live,

    /// <summary>The variables a new shell starts with, once its startup files have run.</summary>
    Startup,
}

/// <summary>
/// What a shell's host knows of the shell's environment, read beside the shell rather than typed into it: the variables of
/// the machine the shell runs on, not this computer's. A transport whose host can say offers one
/// (<see cref="ITerminalTransport.ShellEnvironment"/>).
/// </summary>
public interface IShellEnvironment
{
    /// <summary>The views this host can read, the one to show first leading: <see cref="ShellEnvironmentView.Live"/> only
    /// where the shell reports its variables at each prompt.</summary>
    IReadOnlyList<ShellEnvironmentView> Views { get; }

    /// <summary>The variables of <paramref name="view"/>, by name as the shell names them; null while the host cannot say —
    /// <see cref="ShellEnvironmentView.Live"/> before the shell's first prompt.</summary>
    ValueTask<IReadOnlyDictionary<string, string>?> ReadAsync(ShellEnvironmentView view, CancellationToken ct);

    /// <summary>The host's own path of the startup file a variable is kept in for new shells (<c>/home/adam/.profile</c>), or
    /// null when this shell reads none that a line can be kept in.</summary>
    ValueTask<string?> FindStartupFileAsync(CancellationToken ct);
}
