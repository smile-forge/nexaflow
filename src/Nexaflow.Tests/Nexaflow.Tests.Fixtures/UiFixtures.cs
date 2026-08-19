using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Builds the on-disk material the UI journeys open. It lives here, and is invoked by the suite that owns
/// each format as part of that suite's normal run, because a journey must not construct its own input: the
/// journeys reference nothing but the built application, and anything they had to build would mean linking
/// the very assembly they are supposed to drive through its UI.
/// <para>
/// Everything below is written with the BCL, or — where a real tool is unavoidable — the command line.
/// A fixture that needs a product library to write it (a disk image needs DiscUtils) is built by the suite
/// that already references it, into the same <c>ui</c> corpus folder.
/// </para>
/// </summary>
public static class UiFixtures
{
    /// <summary>The corpus subtree the journeys look in.</summary>
    public static string Root => TestSampleData.Path("ui");

    // ── Projects ──────────────────────────────────────────────────────────────

    public static string ProjectsRoot => Path.Combine(Root, "projects");
    public static string ProjectsActive => Path.Combine(ProjectsRoot, "_projects");
    public static string ProjectsAlpha => Path.Combine(ProjectsActive, "Alpha");

    /// <summary>
    /// A project directory (active + shelf) with a couple of v2 <c>.project</c> files, and the
    /// workspace-scoped config that makes the feature launch <b>enabled</b> — so the real UI (list, buckets,
    /// detail, viewlet) renders instead of the disabled placeholder.
    /// <para>
    /// The config is written as <c>config_0.0.0.1.json</c> rather than under the feature's current version.
    /// ConfigManager migrates the newest <i>older</i> file forward, so any low version loads — which is why
    /// this needs no reference to the assembly that owns the config, and why the fixture does not go stale
    /// when that assembly's version bumps.
    /// </para>
    /// </summary>
    public static void SeedProjects()
    {
        var shelf = Path.Combine(ProjectsRoot, "_shelf");
        var archive = Path.Combine(ProjectsRoot, "_archive");

        Directory.CreateDirectory(archive);
        WriteProject(ProjectsAlpha, "Alpha", "The alpha project.");
        WriteProject(Path.Combine(shelf, "Gamma"), "Gamma", "A shelved project.");

        // Only the Contexts subtree is staged into a test's throwaway config dir; the project folders stay
        // here, which is why these paths are absolute.
        var cfgDir = Path.Combine(ProjectsRoot, "Contexts", "Default", "projects");
        Directory.CreateDirectory(cfgDir);

        // BacklogStatuses is omitted so the ctor-seeded nine survive (ConfigManager populates the existing
        // instance in place, keeping properties absent from the JSON).
        var json = $$"""
        {
          "EnableProjects": true,
          "ProjectDirectory": {{Str(ProjectsActive)}},
          "ShelfDirectory": {{Str(shelf)}},
          "ArchiveDirectory": {{Str(archive)}}
        }
        """;
        File.WriteAllText(Path.Combine(cfgDir, "config_0.0.0.1.json"), json);
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

    // ── Git ───────────────────────────────────────────────────────────────────

    public static string GitRepo => Path.Combine(Root, "git-repo");

    /// <summary>
    /// A real repository with one commit, a second branch and an uncommitted file — so the Git viewlet's
    /// branch picker has a choice and its status line renders counts rather than "clean". Driven through the
    /// <c>git</c> command line, which produces a genuine repository without linking the library the feature
    /// reads it with. Rebuilt from scratch each time, so a journey that dirties it cannot poison the next run.
    /// </summary>
    public static void SeedGitRepo()
    {
        Delete(GitRepo);
        Directory.CreateDirectory(GitRepo);
        File.WriteAllText(Path.Combine(GitRepo, "readme.md"), "# journey");

        Git("init", "--initial-branch=main");
        Git("config", "user.name", "Tester");
        Git("config", "user.email", "test@example.com");
        Git("config", "commit.gpgsign", "false");
        Git("add", "--all");
        Git("commit", "-m", "initial");
        Git("branch", "feature/journey");

        File.WriteAllText(Path.Combine(GitRepo, "scratch.txt"), "untracked");
    }

    private static void Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = GitRepo,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("git could not be started — is it on PATH?");
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({p.ExitCode}): {stderr}");
    }

    // ── Disk images ───────────────────────────────────────────────────────────

    /// <summary>Where the disk-image journey looks. Written by the suite that references DiscUtils.</summary>
    public static string DiskFolder => Path.Combine(Root, "disk");

    // ── Shared ────────────────────────────────────────────────────────────────

    /// <summary>Deletes a fixture folder, clearing the read-only bit git sets on objects under .git.</summary>
    public static void Delete(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(folder, recursive: true);
        }
        catch { /* best-effort temp cleanup */ }
    }

    /// <summary>JSON-encodes a string (quoted + escaped) — needed for Windows paths with backslashes.</summary>
    private static string Str(string s) => JsonSerializer.Serialize(s);
}
