using System.IO;
using System.Text.Json;
using Nexaflow.Features.Projects;

namespace Nexaflow.Tests.Features.Projects.UI;

/// <summary>
/// Seeds an isolated UI-test config dir so the Projects feature launches <b>enabled</b> (its
/// workspace-scoped config lives under <c>Contexts\Default\projects\config_&lt;version&gt;.json</c>) with a
/// real project directory (active + shelf) and a couple of v2 <c>.project</c> files — so the enabled UI
/// (list, buckets, detail, viewlet) renders instead of the disabled placeholder.
/// </summary>
internal static class ProjectsUiSeed
{
    public static string ProjectsRoot(string configDir) => Path.Combine(configDir, "_projects");
    public static string ShelfRoot(string configDir)    => Path.Combine(configDir, "_shelf");
    public static string ArchiveRoot(string configDir)  => Path.Combine(configDir, "_archive");
    public static string AlphaFolder(string configDir)  => Path.Combine(ProjectsRoot(configDir), "Alpha");

    public static void Write(string configDir)
    {
        Directory.CreateDirectory(ArchiveRoot(configDir));
        WriteProject(AlphaFolder(configDir), "Alpha", "The alpha project.");
        WriteProject(Path.Combine(ShelfRoot(configDir), "Gamma"), "Gamma", "A shelved project.");

        var version = typeof(ProjectsConfig).Assembly.GetName().Version!.ToString();
        var cfgDir  = Path.Combine(configDir, "Contexts", "Default", "projects");
        Directory.CreateDirectory(cfgDir);

        // Minimal enabled config — BacklogStatuses is omitted so the ctor-seeded nine survive (ConfigManager
        // populates the existing instance in place, keeping properties absent from the JSON).
        var json = $$"""
        {
          "EnableProjects": true,
          "ProjectDirectory": {{Str(ProjectsRoot(configDir))}},
          "ShelfDirectory": {{Str(ShelfRoot(configDir))}},
          "ArchiveDirectory": {{Str(ArchiveRoot(configDir))}}
        }
        """;
        File.WriteAllText(Path.Combine(cfgDir, $"config_{version}.json"), json);
    }

    private static void WriteProject(string folder, string name, string description)
    {
        Directory.CreateDirectory(folder);
        var json = $$"""
        {
          "SchemaVersion": 2,
          "Name": {{Str(name)}},
          "Description": {{Str(description)}},
          "CompletionCriteria": [ { "Text": "ships", "Status": "Should" } ],
          "Backlog": [ { "Id": "{{Guid.NewGuid()}}", "Title": "First task", "Description": "do it", "StatusKey": "NotStarted" } ]
        }
        """;
        File.WriteAllText(Path.Combine(folder, ".project"), json);
    }

    /// <summary>JSON-encodes a string (quoted + escaped) — needed for Windows paths with backslashes.</summary>
    private static string Str(string s) => JsonSerializer.Serialize(s);
}
