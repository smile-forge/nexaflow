using System.Net;
using System.Text;

namespace Nexaflow.SiteGenerator;

/// <summary>Writes the site: a landing page, the reference index, and a page per help topic.</summary>
internal static class Site
{
    private const string Repo = "https://github.com/smile-forge/nexaflow";

    public static void Write(HelpCorpus corpus, string output, string version)
    {
        if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        Directory.CreateDirectory(output);

        // Pages is a Jekyll site unless told otherwise, and Jekyll drops folders beginning with an underscore —
        // which is where every picture on the site lives.
        File.WriteAllText(Path.Combine(output, ".nojekyll"), string.Empty);
        File.WriteAllText(Path.Combine(output, "assets", "site.css").EnsureFolder(), Css.Text);

        var byTopic = corpus.Pages.ToDictionary(p => p.Topic, StringComparer.OrdinalIgnoreCase);

        foreach (var page in corpus.Pages) WritePage(page, byTopic, output, version);

        WriteIndex(corpus, output, version);
        WriteLanding(corpus, output, version);
    }

    private static void WritePage(HelpPage page, IReadOnlyDictionary<string, HelpPage> byTopic, string output, string version)
    {
        var (html, topics) = Render.Page(page, byTopic);

        var body = new StringBuilder();
        body.Append($"""
            <nav class="crumbs"><a href="../">Reference</a> <span>/</span> {Encode(page.Group)}</nav>
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

    private static void WriteIndex(HelpCorpus corpus, string output, string version)
    {
        var body = new StringBuilder("""
            <header class="page-head">
              <h1>Reference</h1>
              <p class="lede">Every help page Nexaflow ships, the same text the app shows beside what you are
              working on. Press F1 in the app to read it there instead.</p>
            </header>

            """);

        foreach (var group in HelpCorpus.GroupOrder)
        {
            var pages = corpus.Pages.Where(p => p.Group == group).ToList();
            if (pages.Count == 0) continue;

            body.Append($"<section class=\"group\"><h2>{Encode(group)}</h2><div class=\"cards\">");
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

    private static void WriteLanding(HelpCorpus corpus, string output, string version)
    {
        var counts = HelpCorpus.GroupOrder
            .Select(g => (Group: g, Count: corpus.Pages.Count(p => p.Group == g)))
            .Where(g => g.Count > 0);

        var body = new StringBuilder($"""
            <section class="hero">
              <h1>Nexaflow</h1>
              <p class="tagline">One Windows workspace — and your assistant is looking at the same screen you are.</p>
              <p class="hero-body">
                File explorer, terminal, editors, viewers and trackers in one tabbed window, so the thing you
                need is already open. Your assistant reads the tab you are on and works the same controls you
                do — it is not somewhere else being told what you can see.
              </p>
              <p class="actions">
                <a class="button" href="{Repo}/releases/latest">Download {Encode(version)}</a>
                <a class="button ghost" href="help/">Browse the reference</a>
              </p>
              <p class="small">Windows 10 or 11 &middot; free and public domain &middot; no accounts, no telemetry</p>
            </section>

            <section class="group"><h2>What is in it</h2><div class="cards">
            """);

        foreach (var (group, count) in counts)
            body.Append($"""
                <a class="card" href="help/#{Slugify(group)}">
                  <h3>{Encode(group)}</h3>
                  <p>{count} help page{(count == 1 ? "" : "s")}</p>
                </a>
                """);

        body.Append("</div></section>\n");

        Write(Path.Combine(output, "index.html"),
              Shell("Nexaflow — a Windows workspace you and your assistant share",
                    "One tabbed Windows workspace for files, terminals, editors and viewers, with an assistant that sees the tab you are on.",
                    body.ToString(), "", version));
    }

    private static string Shell(string title, string description, string body, string toRoot, string version) => $"""
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
          <nav>
            <a href="{toRoot}help/">Reference</a>
            <a href="{Repo}/releases/latest">Download</a>
            <a href="{Repo}">GitHub</a>
          </nav>
        </header>
        <main>
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

    private static string Trim(string text, int max)
        => text.Length <= max ? text : text[..text.LastIndexOf(' ', max - 1)].TrimEnd(',', ';', ':') + "…";

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
