using System.Text.Json;

namespace Nexaflow.Tests.Sandbox.Session;

/// <summary>
/// The folder one sandbox session lives in, and the only channel between its two ends. It sits in the checkout under
/// <c>TestResults\sandbox\&lt;started&gt;</c> (git-ignored), so the host reads it where it is and the agent reads the same files
/// through the sandbox's mapping of the checkout. Every file is written aside and renamed into place, so neither end ever
/// reads one half-written.
/// </summary>
internal sealed class SessionFolder(string root)
{
    private const string CurrentPointer = "current";
    private const string Aside = ".writing";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Root { get; } = root;

    public string Requests => Path.Combine(Root, "requests");
    public string Responses => Path.Combine(Root, "responses");
    public string Shots => Path.Combine(Root, "shots");
    public string Runs => Path.Combine(Root, "runs");

    /// <summary>The Visual C++ runtime the host stages for the agent to install.</summary>
    public string Redist => Path.Combine(Root, "redist");

    public string AgentStatusFile => Path.Combine(Root, "agent.json");
    public string HostRecordFile => Path.Combine(Root, "session.json");
    public string AgentLog => Path.Combine(Root, "agent.log");

    /// <summary>The configuration the sandbox was started with, kept for reading — <c>wsb</c> is handed the text.</summary>
    public string ConfigurationFile => Path.Combine(Root, "sandbox.wsb");

    public string RequestFile(string id) => Path.Combine(Requests, id + ".json");
    public string ResponseFile(string id) => Path.Combine(Responses, id + ".json");
    public string RunFolder(string id) => Path.Combine(Runs, id);

    /// <summary>Requests not yet taken, oldest first. A file still being written carries <see cref="Aside"/> and is not one.</summary>
    public IEnumerable<string> PendingRequests() =>
        Directory.EnumerateFiles(Requests)
            .Where(p => Path.GetExtension(p).Equals(".json", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);

    /// <summary>Where every session of a checkout lives.</summary>
    public static string SessionsRoot(string checkout) => Path.Combine(checkout, "TestResults", "sandbox");

    /// <summary>A new session's folder, made the checkout's current one.</summary>
    public static SessionFolder CreateNew(string checkout)
    {
        var session = new SessionFolder(Path.Combine(SessionsRoot(checkout), DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        foreach (var folder in (string[])[session.Requests, session.Responses, session.Shots, session.Runs, session.Redist])
            Directory.CreateDirectory(folder);
        WriteText(Path.Combine(SessionsRoot(checkout), CurrentPointer), session.Root);
        return session;
    }

    /// <summary>The checkout's current session, or null when none was started or the last was stopped.</summary>
    public static SessionFolder? Current(string checkout)
    {
        var pointer = Path.Combine(SessionsRoot(checkout), CurrentPointer);
        if (!File.Exists(pointer)) return null;
        var root = File.ReadAllText(pointer).Trim();
        return Directory.Exists(root) ? new SessionFolder(root) : null;
    }

    public static void ClearCurrent(string checkout) => File.Delete(Path.Combine(SessionsRoot(checkout), CurrentPointer));

    public static void WriteJson<T>(string path, T value) => WriteText(path, JsonSerializer.Serialize(value, Json));

    /// <summary>The file's content, or null when it is not there.</summary>
    public static T? ReadJson<T>(string path) where T : class
    {
        // A file renamed into place is whole; a sharing violation only means the other end is replacing it right now.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 10)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static void WriteText(string path, string text)
    {
        var aside = path + Aside;
        File.WriteAllText(aside, text);
        // The other end may be reading the file this replaces, which holds the rename up for a moment.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(aside, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 40)
            {
                Thread.Sleep(25);
            }
        }
    }
}
