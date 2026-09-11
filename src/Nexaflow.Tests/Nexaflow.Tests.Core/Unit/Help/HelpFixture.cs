using System.IO;
using Nexaflow.Core.Help;
using Nexaflow.Core.Localization;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Help pages in fake language packs, behind a real <see cref="LanguageManager"/> and <see cref="HelpLibrary"/>:
/// English with an index, a Text viewer page (with a picture) and a Markdown page; French with its own Text page only.
/// </summary>
internal sealed class HelpFixture : IDisposable
{
    // A 1×1 PNG — the smallest picture WPF will decode.
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    public const string TextPage =
        "# Text viewer\n\nOpen any text file; the pane shows it with line numbers.\n\n![a screenshot](images/shot.png)\n\n" +
        "## Searching\n\nType in the search box to find text in the pane.\n";

    public const string MarkdownPage =
        "# Markdown\n\nTables, diagrams and callouts render in the pane.\n\n```mermaid\ngraph TD; paneNode\n```\n";

    private readonly string _dir;

    public HelpFixture(bool withFrench = false)
    {
        _dir = Path.Combine(Path.GetTempPath(), "nf-help-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        var packs = new Dictionary<string, FakePack>
        {
            ["en"] = new FakePack("en",
                    ("Nexaflow.Core/help/index.md", "# Nexaflow help\n\nEvery page has help.\n"),
                    ("Nexaflow.Features.Text/help/Text.md", TextPage),
                    ("Nexaflow.Features.Markdown/help/Markdown.md", MarkdownPage),
                    ("Nexaflow.Features.Text/strings.json", "{}"))
                .With("Nexaflow.Features.Text/help/images/shot.png", Png),
        };
        if (withFrench)
            packs["fr"] = new FakePack("fr", ("Nexaflow.Features.Text/help/Text.md", "# Visionneuse de texte\n\nOuvrez un fichier.\n"));

        foreach (var code in packs.Keys)
            File.WriteAllBytes(Path.Combine(_dir, $"Nexaflow.Language.{code}.dll"), []);

        Language = new LanguageManager(_dir, (code, _) => packs[code]);
        Library  = new HelpLibrary(Language);
    }

    public LanguageManager Language { get; }
    public HelpLibrary Library { get; }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
