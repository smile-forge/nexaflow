using Nexaflow.Tests.Sandbox.Agent;
using Nexaflow.Tests.Sandbox.Host;
using Nexaflow.Tests.Sandbox.Verbs;

namespace Nexaflow.Tests.Sandbox;

/// <summary>
/// One exe, both ends of a Windows Sandbox session. On the host it starts, shows, stops and talks to the sandbox; inside
/// it, <c>agent</c> serves what the host asks for — a journey run, a screenshot, a click — on the sandbox's own desktop.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h" or "-?")
        {
            Console.WriteLine(VerbCatalogue.Usage);
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            var verb = VerbCatalogue.Find(args[0]);
            var rest = args[1..];
            var arguments = VerbArguments.Parse(verb, rest);
            return verb.Side switch
            {
                VerbSide.Serve => SandboxAgent.Serve(arguments),
                VerbSide.Host => SandboxHost.Execute(verb, arguments),
                _ => SandboxHost.Forward(verb, rest),
            };
        }
        catch (SandboxException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }
}
