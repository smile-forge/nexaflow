using System.Diagnostics;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;
using System.IO;

namespace Nexaflow.Tests.UIJourneys.Showcase;

/// <summary>
/// Captures the pictures of the app used by the README and the site, from a workspace built for the purpose.
///
/// Nothing of the machine it runs on may reach a published picture, so three things stand between the two. The
/// config directory is already a fresh temporary one, so no workspace, tab, provider key or recent path of the
/// owner's is loaded. The files on show are a tree this test writes, reached through a <c>subst</c> drive, so the
/// folder tree has no real parent to expose — a demo folder under a profile would put every other account's name
/// one node above it. And before any file is written, <see cref="AssertNothingPersonal"/> reads the window's whole
/// automation tree and fails on the user name, the machine name or the profile path appearing anywhere in it.
///
/// Writing is opt-in: set <c>NEXAFLOW_WRITE_SHOWCASE</c> to the output folder. Without it the tests still run and
/// still check the window is clean, which is the part worth having in a normal run.
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("captures documentation screenshots")]
public class ShowcaseCaptureTests : FileSystemUiTestBase
{
    /// <summary>The drive the demo tree is shown as. Standing at a drive root, the tree has no parent to show.</summary>
    private const string Drive = "N:";

    private const string Project = @"N:\Projects\Aurora";

    private static string _root = string.Empty;
    private static bool _mounted;

    protected override string? LaunchTabKind => null;   // the tabs come from the seeded workspace instead

    /// <summary>
    /// The theme this launch is dressed in. It comes from the environment rather than a data row because the
    /// workspace is seeded before the app starts, which is before a test method gets to say anything — so the
    /// sweep over the themes is a loop in the capture script, one launch per theme.
    /// </summary>
    private static string Theme => Environment.GetEnvironmentVariable("NEXAFLOW_SHOWCASE_THEME") is { Length: > 0 } t
        ? t
        : "Dark";

    /// <summary>
    /// Opens the demo tabs from config rather than by driving the address bar: a seeded tab lands on exactly
    /// the folder wanted, every run, and a picture of the wrong folder is not obvious from a green test.
    /// </summary>
    protected override void SeedConfig(string configDir)
    {
        var version = FileVersionInfo.GetVersionInfo(FindAppExe()).FileVersion ?? "1.0.0.0";

        Write(configDir, "shell", version, $$"""
            {
              "ConfigName": "shell",
              "FriendlyName": "Shell",
              "Theme": "{{Theme}}",
              "Language": "en",
              "TextFontSize": 13,
              "PrestartAtLogin": false,
              "DisableAnimationsOnBattery": false,
              "LastRunVersion": "{{version}}"
            }
            """);

        Write(configDir, "workcontexts", version, $$"""
            {
              "ConfigName": "workcontexts",
              "FriendlyName": "Workspaces",
              "Contexts": [
                {
                  "DefaultTabs": [
                    {
                      "PageKind": "FileSystem",
                      "PageParams": { "mode": "path", "path": "{{Project.Replace("\\", "\\\\")}}" },
                      "Pane": 0,
                      "Title": "Aurora",
                      "IsActive": true
                    }
                  ],
                  "LastSessionTabs": [],
                  "Name": "Default",
                  "Color": "#5B8CFF",
                  "Icon": "\u2B21"
                }
              ]
            }
            """);
    }

    private static void Write(string configDir, string area, string version, string json)
    {
        var folder = Path.Combine(configDir, area);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, $"config_{version}.json"), json);
    }

    [ClassInitialize]
    public static void BuildDemoWorkspace(TestContext _)
    {
        _root = Path.Combine(Path.GetTempPath(), "nexaflow-showcase");
        WriteTree(_root);

        // A subst drive inherits the label of the volume it is taken from; the temp folder is on the system
        // drive, whose label is the stock one on any machine.
        _mounted = Run("subst", $"{Drive} \"{_root}\"") || Directory.Exists(Drive + @"\");
    }

    [ClassCleanup]
    public static void ReleaseDemoWorkspace()
    {
        if (_mounted) Run("subst", $"{Drive} /D");
        _mounted = false;
    }

    [TestMethod]
    public void CaptureTheFileBrowser()
    {
        Assert.IsTrue(_mounted, $"the demo tree could not be mounted as {Drive}");

        Assert.IsNotNull(WaitForId("DirectoryTree", 20), "the file browser did not open on the demo folder.");
        Thread.Sleep(900);

        Shot("nexaflow-files.png");
    }

    [TestMethod]
    public void CaptureTheMarkdownEditor()
    {
        Assert.IsTrue(_mounted, $"the demo tree could not be mounted as {Drive}");

        NavigateFileBrowserTo(Path.Combine(Project, "docs"));
        OpenFile("architecture.md");

        // The diagrams are laid out and drawn after the document opens; a picture taken too early catches
        // the text with holes where they will be.
        Assert.IsNotNull(WaitForId("MarkdownView", 20), "the Markdown tab did not open.");
        Thread.Sleep(2500);

        Shot($"nexaflow-markdown-{Theme.ToLowerInvariant()}.png");
    }

    // ── the picture, and what has to be true before it is taken ──────────────

    private void Shot(string file)
    {
        AssertNothingPersonal();

        var folder = Environment.GetEnvironmentVariable("NEXAFLOW_WRITE_SHOWCASE");
        if (string.IsNullOrEmpty(folder))
        {
            Assert.Inconclusive("the window is clean; set NEXAFLOW_WRITE_SHOWCASE to write the picture");
            return;
        }

        Directory.CreateDirectory(folder);
        Capture.Element(MainWindow).ToFile(Path.Combine(folder, file));
    }

    /// <summary>
    /// Reads every name and help string in the window and refuses the picture if the machine's owner is named
    /// anywhere in it. A path on screen is the obvious leak, but a window title, a tooltip or a breadcrumb will
    /// do it just as well, so this reads the tree rather than the places a leak was expected.
    /// </summary>
    private void AssertNothingPersonal()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] forbidden =
        [
            Environment.UserName,
            Environment.MachineName,
            profile,
            Path.GetDirectoryName(profile) ?? string.Empty,
        ];

        var wanted = forbidden.Where(f => f.Length > 3).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var found = new List<string>();

        Read(MainWindow, text =>
        {
            foreach (var bad in wanted)
                if (text.Contains(bad, StringComparison.OrdinalIgnoreCase))
                    found.Add($"'{bad}' in \"{Short(text)}\"");
        });

        Assert.AreEqual(0, found.Count,
                        "the window names the machine or its owner, so the picture was not taken:\n  "
                      + string.Join("\n  ", found.Distinct()));
    }

    private static void Read(AutomationElement element, Action<string> onText)
    {
        foreach (var text in new[] { element.Properties.Name.ValueOrDefault, element.Properties.HelpText.ValueOrDefault })
            if (!string.IsNullOrEmpty(text)) onText(text);

        foreach (var child in element.FindAllChildren()) Read(child, onText);
    }

    private static string Short(string text) => text.Length <= 80 ? text : text[..77] + "...";

    // ── the tree on show ─────────────────────────────────────────────────────

    private void OpenFile(string fileName)
    {
        var cell = WaitForName(fileName, 10);
        Assert.IsNotNull(cell, $"'{fileName}' is not in the file list.");

        var row = cell;
        while (row is not null && row.ControlType != ControlType.DataItem) row = row.Parent;

        var target = row ?? cell!;
        target.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        target.Patterns.SelectionItem.PatternOrDefault?.Select();
        target.Focus();
        Wait.UntilInputIsProcessed();

        using (Keyboard.Pressing(VirtualKeyShort.SHIFT))
            Keyboard.Type(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
    }

    /// <summary>A small, invented project — enough for the window to look like somebody is working in it.</summary>
    private static void WriteTree(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);

        foreach (var folder in new[] { @"Projects\Aurora\docs", @"Projects\Aurora\src", @"Projects\Aurora\samples",
                                       @"Projects\Harbour", "Notes" })
            Directory.CreateDirectory(Path.Combine(root, folder));

        var aurora = Path.Combine(root, @"Projects\Aurora");

        File.WriteAllText(Path.Combine(aurora, "README.md"),
            "# Aurora\n\nA small service that reads meters and files the readings.\n");
        File.WriteAllText(Path.Combine(aurora, "CHANGELOG.md"), "# Changelog\n\n## 0.4\n\n- Quarantine bad readings\n");
        File.WriteAllText(Path.Combine(aurora, "aurora.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(aurora, "settings.json"),
            "{\n  \"interval\": \"00:15:00\",\n  \"quarantine\": true\n}\n");

        File.WriteAllText(Path.Combine(aurora, @"src\Reader.cs"),
            "namespace Aurora;\n\npublic sealed class Reader\n{\n    public int Read() => 0;\n}\n");
        File.WriteAllText(Path.Combine(aurora, @"src\Filing.cs"),
            "namespace Aurora;\n\npublic sealed class Filing\n{\n    public void Save(int reading) { }\n}\n");
        File.WriteAllText(Path.Combine(aurora, @"src\Quarantine.cs"),
            "namespace Aurora;\n\npublic sealed class Quarantine\n{\n    public void Hold(int reading) { }\n}\n");
        File.WriteAllText(Path.Combine(aurora, @"samples\readings.csv"),
            "meter,taken,reading\nM-104,2026-09-01,4812\nM-104,2026-09-08,4907\nM-221,2026-09-01,1330\n");
        File.WriteAllText(Path.Combine(aurora, @"docs\notes.md"), "# Notes\n\nThe meter ids are not contiguous.\n");

        File.WriteAllText(Path.Combine(root, @"Notes\meeting.md"), "# Meeting\n\n- Ship the reader\n");
        File.WriteAllText(Path.Combine(root, @"Projects\Harbour\README.md"), "# Harbour\n\nStores what Aurora files.\n");

        File.WriteAllText(Path.Combine(aurora, @"docs\architecture.md"), Architecture);
    }

    private const string Architecture =
        """
        # Aurora — how it fits together

        Meters are read on a timer, the readings are checked, and anything that passes is filed.

        ```mermaid
        flowchart LR
            Timer([Timer]) --> Reader[Meter reader]
            Reader --> Check{Reading sane?}
            Check -- yes --> Filing[(Filing store)]
            Check -- no --> Quarantine[Quarantine]
            Filing --> Report[Monthly report]
        ```

        ## A reading, end to end

        ```mermaid
        sequenceDiagram
            participant T as Timer
            participant R as Reader
            participant S as Store
            T->>R: due(meter)
            R->>R: read and check
            R->>S: file(reading)
            S-->>R: filed
        ```

        ## What is left

        | Piece | State |
        |:------|:------|
        | Reader | done |
        | Filing | done |
        | Report | in progress |
        """;

    private static bool Run(string exe, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c {exe} {arguments}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            process?.WaitForExit(10_000);
            return process?.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
