using System.Text;

namespace Nexaflow.Tests.Sandbox.Verbs;

/// <summary>Every verb the sandbox command line knows, and its usage text.</summary>
internal static class VerbCatalogue
{
    public static readonly Verb Start = new(
        "start", VerbSide.Host,
        "start [--monitor <name>] [--memory <MB>] [--offline] [--audio]",
        "Boot a sandbox, open its window maximised on a monitor beside your work, and start the agent in it. The monitor is "
        + "part of its name (DELL) or its device (DISPLAY2); unnamed, the first that is not the primary. Memory defaults to "
        + "16384 MB. --offline cuts its network; --audio lets the voice journeys hear the host's microphone.",
        ValueOptions: new HashSet<string> { "monitor", "memory" },
        Flags: new HashSet<string> { "offline", "audio" });

    public static readonly Verb Show = new(
        "show", VerbSide.Host,
        "show [--monitor <name>]",
        "Reopen the sandbox's window, or move it. Keep it open: closed, the sandbox has no desktop to capture or drive.",
        ValueOptions: new HashSet<string> { "monitor" });

    public static readonly Verb Status = new(
        "status", VerbSide.Host,
        "status",
        "The session folder, the sandbox, its window, and what the agent is doing.");

    public static readonly Verb Stop = new(
        "stop", VerbSide.Host,
        "stop",
        "Close the app and cancel any run, then tear the sandbox down. The session folder and its results stay.");

    public static readonly Verb Run = new(
        "run", VerbSide.Agent,
        "run [<filter>]",
        "Run the UI journeys, filtered with the test runner's --filter syntax (FullyQualifiedName~HelpJourney), and follow "
        + "their console. Ctrl+C stops following; the run goes on. Closes an app `launch` opened first.",
        MaxPositionals: 1, SendsInput: true);

    public static readonly Verb Cancel = new(
        "cancel", VerbSide.Agent,
        "cancel",
        "Stop the journey run in progress.");

    public static readonly Verb Launch = new(
        "launch", VerbSide.Agent,
        "launch [--tab <page-kind>] [--param <key=value>]...",
        "Start Nexaflow the way a journey does — fresh config root, --uiTest — optionally on one page, to look at or drive.",
        ValueOptions: new HashSet<string> { "tab", "param" }, SendsInput: true);

    public static readonly Verb Close = new(
        "close", VerbSide.Agent,
        "close",
        "Close the app `launch` started, and report any crash it logged.");

    public static readonly Verb Shot = new(
        "shot", VerbSide.Agent,
        "shot [--window]",
        "Screenshot the sandbox's screen, or with --window just Nexaflow's main window, and print the PNG's path.",
        Flags: new HashSet<string> { "window" });

    public static readonly Verb Tree = new(
        "tree", VerbSide.Agent,
        "tree [<target>] [--depth <n>]",
        "The UI Automation tree of every Nexaflow window, or under one element: control type, name, AutomationId, bounds.",
        MaxPositionals: 1, ValueOptions: new HashSet<string> { "depth" });

    public static readonly Verb Click = new(
        "click", VerbSide.Agent,
        "click <target> [--right] [--double]",
        "Move the pointer to the target and click it, as a person would.",
        MinPositionals: 1, MaxPositionals: 1, Flags: new HashSet<string> { "right", "double" }, SendsInput: true);

    public static readonly Verb Type = new(
        "type", VerbSide.Agent,
        "type <text> [--into <target>]",
        "Type the text into whatever has focus, or click the target first. Text starting with -- goes after a lone --.",
        MinPositionals: 1, MaxPositionals: 1, ValueOptions: new HashSet<string> { "into" }, SendsInput: true);

    public static readonly Verb Key = new(
        "key", VerbSide.Agent,
        "key <chord>",
        "Press a key or a chord: enter, esc, f5, ctrl+shift+p, alt+f4.",
        MinPositionals: 1, MaxPositionals: 1, SendsInput: true);

    public static readonly Verb Agent = new(
        "agent", VerbSide.Serve,
        "agent --session <folder>",
        "The agent itself. `start` runs it inside the sandbox; there is no reason to run it by hand.",
        ValueOptions: new HashSet<string> { "session" });

    public static readonly IReadOnlyList<Verb> All =
        [Start, Show, Status, Stop, Run, Cancel, Launch, Close, Shot, Tree, Click, Type, Key, Agent];

    public static Verb Find(string name) =>
        All.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new SandboxException($"Unknown verb '{name}'.{Environment.NewLine}{Environment.NewLine}{Usage}");

    public static string Usage { get; } = BuildUsage();

    private static string BuildUsage()
    {
        var text = new StringBuilder();
        text.AppendLine("Nexaflow.Tests.Sandbox — the UI journeys, and the app, inside Windows Sandbox.");
        Section("On the host:", VerbSide.Host);
        Section("In the sandbox, through its agent:", VerbSide.Agent);
        text.AppendLine();
        text.AppendLine("A <target> is an AutomationId, name:<text> for an element's name, or x,y in the sandbox's screen pixels.");
        text.Append("Build the solution first: the sandbox runs the app and the journeys from this checkout's build.");
        return text.ToString();

        void Section(string heading, VerbSide side)
        {
            text.AppendLine().AppendLine(heading);
            foreach (var verb in All.Where(v => v.Side == side))
                text.AppendLine($"  {verb.Synopsis}").AppendLine($"      {verb.Summary}");
        }
    }
}
