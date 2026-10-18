namespace Nexaflow.Tests.Sandbox.Host;

/// <summary>A host folder mapped read-only into the sandbox, and where the sandbox sees it.</summary>
internal sealed record SharedFolder(string Host, string Sandbox);

/// <summary>
/// The <c>NEXAFLOW_TEST_*</c> variables with which a machine points its suites at what it has — models, media, runtime
/// packs — carried from the host into the sandbox, so a journey finds there what it finds on the host. A value naming a
/// path in the checkout is rewritten to that path in the checkout's mapping; one naming a path elsewhere has its folder
/// mapped read-only and is rewritten to match; any other value goes across as it is.
/// </summary>
internal sealed class ForwardedEnvironment
{
    public const string Prefix = "NEXAFLOW_TEST_";

    /// <summary>Where the host folders the variables name appear, one per variable.</summary>
    private const string SharedRoot = @"C:\nexaflow-host";

    private ForwardedEnvironment(IReadOnlyDictionary<string, string> variables, IReadOnlyList<SharedFolder> folders)
    {
        Variables = variables;
        Folders = folders;
    }

    /// <summary>The variables as the sandbox reads them.</summary>
    public IReadOnlyDictionary<string, string> Variables { get; }

    /// <summary>The host folders they name, outside the checkout.</summary>
    public IReadOnlyList<SharedFolder> Folders { get; }

    public static ForwardedEnvironment FromHost(string checkout)
    {
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var folders = new List<SharedFolder>();

        foreach (var (name, value) in Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                     .Select(e => ((string)e.Key, e.Value as string ?? "")))
        {
            if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!Path.IsPathFullyQualified(value) || !(Directory.Exists(value) || File.Exists(value)))
            {
                variables[name] = value;
                continue;
            }

            var path = Path.GetFullPath(value);
            var inCheckout = Path.GetRelativePath(checkout, path);
            if (!inCheckout.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathFullyQualified(inCheckout))
            {
                variables[name] = Path.Combine(SandboxConfiguration.Checkout, inCheckout);
                continue;
            }

            var isFolder = Directory.Exists(path);
            var shared = new SharedFolder(isFolder ? path : Path.GetDirectoryName(path)!, Path.Combine(SharedRoot, name));
            folders.Add(shared);
            variables[name] = isFolder ? shared.Sandbox : Path.Combine(shared.Sandbox, Path.GetFileName(path));
        }
        return new ForwardedEnvironment(variables, folders);
    }
}
