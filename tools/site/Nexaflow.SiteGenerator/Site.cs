using System.Net;
using System.Text;

namespace Nexaflow.SiteGenerator;

/// <summary>Writes the site: a landing page, a page per section, the reference index, and every help page.</summary>
internal static class Site
{
    private const string Repo = "https://github.com/smile-forge/nexaflow";

    /// <summary>
    /// Features whose help ships but whose feature does not, so a card can say so rather than let the reader
    /// download the app expecting something that is not there yet.
    /// </summary>
    private static readonly HashSet<string> Unfinished = new(StringComparer.OrdinalIgnoreCase) { "solver", "network" };

    /// <summary>
    /// Figures for the front page, all drawn by the app's own renderer for the help pages. They are all Mermaid
    /// ones on purpose: the writers draw those in the dark palette, so the strip reads as one set rather than a
    /// jumble of light and dark grounds. The chemistry and word clouds are on the Author page.
    /// </summary>
    private static readonly (string File, string Alt)[] Proof =
    [
        ("mermaid-flowchart.png", "A flowchart drawn by Nexaflow"),
        ("mermaid-sankey.png", "A Sankey diagram drawn by Nexaflow"),
        ("mermaid-radar.png", "A radar chart drawn by Nexaflow"),
        ("mermaid-c4.png", "A C4 container diagram drawn by Nexaflow"),
        ("mermaid-ishikawa.png", "An Ishikawa fishbone diagram drawn by Nexaflow"),
        ("mermaid-quadrant.png", "A quadrant chart drawn by Nexaflow"),
    ];

    /// <summary>The notice every page carries while the working tree is ahead of the newest release, or nothing.</summary>
    private static string _unreleased = string.Empty;

    public static void Write(HelpCorpus corpus, ProductTree tree, string repo, string output, string version, string? aheadOf = null)
    {
        if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        Directory.CreateDirectory(output);

        // Pages is a Jekyll site unless told otherwise, and Jekyll drops folders beginning with an underscore —
        // which is where every picture on the site lives.
        File.WriteAllText(Path.Combine(output, ".nojekyll"), string.Empty);
        File.WriteAllText(Path.Combine(output, "assets", "site.css").EnsureFolder(), Css.Text);

        _unreleased = aheadOf is null
            ? string.Empty
            : $"""
               <p class="unreleased">These pages describe Nexaflow as it stands <b>after {Encode(aheadOf)}</b>, so some
               of what they cover is not in the current download yet. The latest release is
               <a href="{Repo}/releases/latest">{Encode(aheadOf)}</a>.</p>
               """;

        var byTopic = corpus.Pages.ToDictionary(p => p.Topic, StringComparer.OrdinalIgnoreCase);

        foreach (var page in corpus.Pages) WritePage(page, byTopic, output, version);
        foreach (var section in HelpCorpus.Sections) WriteSection(section, corpus, tree, repo, output, version);

        WriteIndex(corpus, output, version);
        WriteLanding(corpus, tree, output, version);
    }

    private static void WritePage(HelpPage page, IReadOnlyDictionary<string, HelpPage> byTopic, string output, string version)
    {
        var (html, topics) = Render.Page(page, byTopic);
        var section = HelpCorpus.SectionFor(page.Group);

        var crumb = section is null
            ? Encode(page.Group)
            : $"<a href=\"../../{section.Slug}/\">{Encode(section.Title)}</a>";

        var body = new StringBuilder($"""
            <nav class="crumbs"><a href="../">Reference</a> <span>/</span> {crumb}</nav>
            <header class="page-head">
              <h1>{Encode(page.Title)}</h1>
              {(page.Summary.Length > 0 ? $"<p class=\"lede\">{Render.Inline(page.Summary)}</p>" : "")}
            </header>

            """);

        if (topics.Count > 1)
        {
            body.Append("<nav class=\"topics\"><h2>Topics</h2><ul>");
            foreach (var topic in topics) body.Append($"<li><a href=\"#{topic.Id}\">{Encode(topic.Text)}</a></li>");
            body.Append("</ul></nav>\n");
        }

        body.Append("<article class=\"prose\">").Append(html).Append("</article>\n");

        Write(Path.Combine(output, "help", page.Slug, "index.html"),
              Shell(page.Title + " — Nexaflow help", Render.Plain(page.Summary), body.ToString(), "../../", version));
    }

    private static void WriteSection(Section section, HelpCorpus corpus, ProductTree tree, string repo, string output, string version)
    {
        var intro = Path.Combine(repo, "tools", "site", "content", section.Slug + ".md");
        if (!File.Exists(intro))
            throw new InvalidOperationException($"section '{section.Slug}' has no opening page at {intro}");

        var (title, lede, prose) = Opening(File.ReadAllText(intro).Replace("\r\n", "\n"), section.Title);

        var body = new StringBuilder($"""
            <header class="page-head">
              <h1>{Encode(title)}</h1>
              <p class="lede">{Render.Inline(lede)}</p>
            </header>
            <article class="prose">{Render.Loose(prose)}</article>

            """);

        var pages = corpus.Pages.Where(p => p.Group == section.Group).ToList();
        body.Append($"<section class=\"group\"><h2>{pages.Count} page{(pages.Count == 1 ? "" : "s")} on this</h2><div class=\"cards\">");
        foreach (var page in pages) body.Append(Card(page, tree, $"../help/{page.Slug}/"));
        body.Append("</div></section>\n");

        Write(Path.Combine(output, section.Slug, "index.html"),
              Shell(title + " — Nexaflow", Render.Plain(lede), body.ToString(), "../", version));
    }

    private static void WriteIndex(HelpCorpus corpus, string output, string version)
    {
        var body = new StringBuilder("""
            <header class="page-head">
              <h1>Reference</h1>
              <p class="lede">Every help page Nexaflow ships — the same text the app shows beside what you are
              working on. Press F1 in the app to read it there instead.</p>
            </header>

            """);

        foreach (var group in HelpCorpus.GroupOrder)
        {
            var pages = corpus.Pages.Where(p => p.Group == group).ToList();
            if (pages.Count == 0) continue;

            var section = HelpCorpus.SectionFor(group);
            var heading = section is null
                ? Encode(group)
                : $"<a href=\"../{section.Slug}/\">{Encode(group)}</a>";

            body.Append($"<section class=\"group\" id=\"{Slugify(group)}\"><h2>{heading}</h2><div class=\"cards\">");
            foreach (var page in pages)
                body.Append($"""
                    <a class="card" href="{page.Slug}/">
                      <h3>{Encode(page.Title)}</h3>
                      <p>{Encode(Trim(Render.Plain(page.Summary), 150))}</p>
                    </a>
                    """);
            body.Append("</div></section>\n");
        }

        Write(Path.Combine(output, "help", "index.html"),
              Shell("Reference — Nexaflow", "Every help page Nexaflow ships.", body.ToString(), "../", version));
    }

    private static void WriteLanding(HelpCorpus corpus, ProductTree tree, string output, string version)
    {
        var aiReady = corpus.Pages.Count(p => tree.Find(p.Node)?.AiReady == true);

        var body = new StringBuilder($"""
            <section class="hero">
              <h1>Nexaflow</h1>
              <p class="tagline">One Windows workspace — and your assistant is looking at the same screen you are.</p>
              <p class="hero-body">
                File explorer, terminal, editors, viewers and trackers in one tabbed window, so the thing you need
                is already open. Your assistant reads the tab you are on and works the same controls you do. It is
                not somewhere else being told what you can see.
              </p>
              <p class="actions">
                <a class="button" href="{Repo}/releases/latest">Download {Encode(version)}</a>
                <a class="button ghost" href="together/">How the AI works</a>
              </p>
              <p class="small">Windows 10 or 11 &middot; free and public domain &middot; no accounts, no telemetry</p>
            </section>

            <section class="strip">
              <h2>Drawn on your machine, from text you type</h2>
              <p class="small">Every one of these came out of the renderer that ships in the app — no browser, no
                 diagram server, nothing sent anywhere. <a href="author/">See how</a>.</p>
              <div class="figures">
            """);

        foreach (var (file, alt) in Proof)
            body.Append($"<img src=\"help/_img/Nexaflow.Features.Markdown/images/markdown/{file}\" alt=\"{Encode(alt)}\" loading=\"lazy\">");

        body.Append($"""
              </div>
            </section>

            <section class="group"><h2>What is in it</h2><div class="cards">
            """);

        foreach (var section in HelpCorpus.Sections)
        {
            var pages = corpus.Pages.Count(p => p.Group == section.Group);
            body.Append($"""
                <a class="card" href="{section.Slug}/">
                  <h3>{Encode(section.Title)}</h3>
                  <p>{pages} page{(pages == 1 ? "" : "s")}</p>
                </a>
                """);
        }

        body.Append($"""
            </div></section>

            <section class="group"><h2>Two things worth knowing</h2><div class="cards">
              <a class="card" href="together/">
                <h3>{aiReady} features the assistant can work</h3>
                <p>Not "AI-powered" as a label — each of these hands the model the page's own state and the same
                   controls you use, and asks you before it changes anything.</p>
              </a>
              <a class="card" href="help/">
                <h3>{corpus.Pages.Count} help pages, published from the app</h3>
                <p>The reference here is the app's own help, generated from the files it ships, so what you read
                   before installing is what F1 shows you afterwards.</p>
              </a>
            </div></section>
            """);

        Write(Path.Combine(output, "index.html"),
              Shell("Nexaflow — a Windows workspace you and your assistant share",
                    "One tabbed Windows workspace for files, terminals, editors and viewers, with an assistant that sees the tab you are on.",
                    body.ToString(), "", version));
    }

    private static string Card(HelpPage page, ProductTree tree, string href)
    {
        var node = tree.Find(page.Node);
        var text = node is { Description.Length: > 0 } ? node.Description : page.Summary;

        var badges = new StringBuilder();
        if (page.Node is not null && Unfinished.Contains(page.Node)) badges.Append("<span class=\"badge soon\">Not finished yet</span>");
        if (node?.AiReady == true) badges.Append("<span class=\"badge ai\">AI Ready</span>");
        if (node?.Searchable == true) badges.Append("<span class=\"badge\">Answers <code>?</code></span>");

        return $"""
            <a class="card" href="{href}">
              <h3>{Encode(page.Title)}</h3>
              <p>{Encode(Trim(Render.Plain(text), 210))}</p>
              {(badges.Length > 0 ? $"<p class=\"badges\">{badges}</p>" : "")}
            </a>
            """;
    }

    /// <summary>A section's opening page: its title, the line under it, and the prose after that.</summary>
    private static (string Title, string Lede, string Prose) Opening(string markdown, string fallback)
    {
        var lines = markdown.Split('\n');
        var at = Array.FindIndex(lines, l => l.StartsWith("# ", StringComparison.Ordinal));
        if (at < 0) return (fallback, string.Empty, markdown);

        var i = at + 1;
        while (i < lines.Length && lines[i].Trim().Length == 0) i++;

        var lede = new List<string>();
        while (i < lines.Length && lines[i].Trim() is { Length: > 0 } text && !text.StartsWith('#'))
        {
            lede.Add(text);
            i++;
        }

        return (lines[at][2..].Trim(), string.Join(' ', lede), string.Join('\n', lines[i..]));
    }

    private static string Shell(string title, string description, string body, string toRoot, string version)
    {
        var nav = new StringBuilder();
        foreach (var section in HelpCorpus.Sections)
            nav.Append($"<a href=\"{toRoot}{section.Slug}/\">{Encode(section.Title)}</a>");
        nav.Append($"<a href=\"{toRoot}help/\">Reference</a>");
        nav.Append($"<a href=\"{Repo}/releases/latest\">Download</a>");

        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{Encode(title)}</title>
            <meta name="description" content="{Encode(description)}">
            <link rel="stylesheet" href="{toRoot}assets/site.css">
            </head>
            <body>
            <header class="site">
              <a class="wordmark" href="{toRoot}">Nexaflow</a>
              <nav>{nav}</nav>
            </header>
            <main>
            {_unreleased}
            {body}
            </main>
            <footer class="site">
              <p>Nexaflow {Encode(version)} &middot; public domain under the Unlicense &middot;
                 <a href="{Repo}">source on GitHub</a></p>
              <p class="small">These pages are the app's own help, published from the same files it ships.</p>
            </footer>
            </body>
            </html>
            """;
    }

    private static string Trim(string text, int max)
        => text.Length <= max ? text : text[..text.LastIndexOf(' ', max - 1)].TrimEnd(',', ';', ':', '.') + "…";

    private static string Slugify(string text)
        => new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    private static void Write(string path, string html) => File.WriteAllText(path.EnsureFolder(), html);

    /// <summary>Copies every picture a help pack carries into one place per project.</summary>
    public static void CopyImages(string repo, string output)
    {
        var src = Path.Combine(repo, "src");
        foreach (var project in Directory.GetDirectories(src)
                     .Where(d => Path.GetFileName(d) != "Nexaflow.Tests")
                     .SelectMany(d => Directory.GetDirectories(d).Prepend(d)))
        {
            var help = Path.Combine(project, "Localization", "en", "help");
            if (!Directory.Exists(help)) continue;

            foreach (var file in Directory.EnumerateFiles(help, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
                var target = Path.Combine(output, "help", "_img", Path.GetFileName(project), Path.GetRelativePath(help, file));
                File.Copy(file, target.EnsureFolder(), overwrite: true);
            }
        }
    }
}

internal static class PathExtensions
{
    /// <summary>Makes sure the file's folder exists, and hands the path back so it can be used inline.</summary>
    public static string EnsureFolder(this string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}
