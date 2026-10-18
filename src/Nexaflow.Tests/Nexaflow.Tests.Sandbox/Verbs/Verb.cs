namespace Nexaflow.Tests.Sandbox.Verbs;

/// <summary>Which end of a session carries a verb out.</summary>
internal enum VerbSide
{
    /// <summary>The host does it: the verb manages the sandbox itself.</summary>
    Host,

    /// <summary>The agent does it: the host forwards the verb as typed and prints the answer.</summary>
    Agent,

    /// <summary>The agent process itself, started inside the sandbox when it signs in.</summary>
    Serve,
}

/// <summary>
/// One verb of the sandbox command line. Both ends parse a verb's arguments against the same declaration, so the host can
/// refuse a bad one before it crosses into the sandbox and the agent reads exactly what was typed.
/// </summary>
/// <param name="Synopsis">How it is written, for usage and for the error a bad argument gets.</param>
/// <param name="ValueOptions">Options that take a value (<c>--depth 3</c>); one given twice keeps both.</param>
/// <param name="Flags">Options that stand alone (<c>--window</c>).</param>
/// <param name="SendsInput">It moves the pointer or types, so the agent refuses it while a journey run is driving the desktop.</param>
internal sealed record Verb(
    string Name,
    VerbSide Side,
    string Synopsis,
    string Summary,
    int MinPositionals = 0,
    int MaxPositionals = 0,
    IReadOnlySet<string>? ValueOptions = null,
    IReadOnlySet<string>? Flags = null,
    bool SendsInput = false)
{
    public IReadOnlySet<string> ValueOptions { get; } = ValueOptions ?? new HashSet<string>();
    public IReadOnlySet<string> Flags { get; } = Flags ?? new HashSet<string>();
}
