
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using Nexaflow.Localization;

namespace Nexaflow.Session.Help;

/// <summary>
/// The help pages the language packs hold: one <c>help/&lt;PageKind&gt;.md</c> per page kind, shipped by the project
/// that owns that page (its <c>Localization/&lt;code&gt;/help/</c>), plus Core's <c>help/index.md</c> — shown for a
/// page kind with no help of its own, followed by a generated list of every help page there is.
/// <para>
/// Reads through <see cref="LanguageManager"/>, so a page the active language lacks comes from English, and the lot is
/// forgotten when the language changes. Nothing is read until Help is first opened.
/// </para>
/// <para>
/// The session makes one over its language (<see cref="Services.ShellHost"/>) and hands it to the Help page's registration,
/// by the feature injection, and to every runtime's assistant, as the tools that read it.
/// </para>
/// </summary>
public sealed partial class HelpLibrary
{
    public const string IndexTopic = "index";
    private const string IndexName = "Nexaflow.Core/help/index.md";

    private readonly LanguageManager _language;

    /// <summary>Whether a fenced block is drawn as something other than its text, as the toolkit's renderer draws it.</summary>
    private readonly Func<string?, bool> _fenceDrawn;
    private readonly object _gate = new();
    private IReadOnlyList<HelpTopic>? _topics;
    private HelpSearchIndex? _index;

    internal HelpLibrary(LanguageManager language, Func<string?, bool> fenceDrawn)
    {
        _language   = language;
        _fenceDrawn = fenceDrawn;
        language.Changed += Forget;
    }

    [GeneratedRegex(@"^[^/]+/help/[^/]+\.md$")]
    private static partial Regex HelpPageName();

    /// <summary>Every help page but the index, by title.</summary>
    public IReadOnlyList<HelpTopic> Topics
    {
        get { lock (_gate) return _topics ??= ReadTopics(); }
    }

    /// <summary>The "then every help page" half of a search, built the first time one needs it — from each page as it
    /// is shown, Topics list and back links included, so a match's ordinal is the one the page itself highlights.</summary>
    public HelpSearchIndex Index
    {
        get
        {
            lock (_gate)
                return _index ??= new HelpSearchIndex(() => Topics.Select(t => (t, Load(t.Topic).Markdown)).ToList(), PlainText);
        }
    }

    /// <summary>A help page as the text a reader sees (<see cref="HelpPlainText"/>): what the search finds and the assistant reads.</summary>
    public string PlainText(string? markdown) => HelpPlainText.Extract(markdown, _fenceDrawn);

    /// <summary>The help page for <paramref name="topic"/> (a page kind), if there is one.</summary>
    public HelpTopic? Find(string? topic)
        => string.IsNullOrWhiteSpace(topic)
            ? null
            : Topics.FirstOrDefault(t => string.Equals(t.Topic, topic, StringComparison.OrdinalIgnoreCase));

    /// <summary>The help for <paramref name="topic"/> (a page kind) — or, when it has none, the index saying so. Null,
    /// blank or <see cref="IndexTopic"/> asks for the index outright.</summary>
    public HelpDocument Load(string? topic)
    {
        // Every page is shown with its menu — the Topics list and the links back to it (HelpContents).
        if (Find(topic) is { } found && Read(found.LogicalName) is { } markdown)
            return new HelpDocument(found, HelpContents.AddTopics(markdown));

        var missing = string.IsNullOrWhiteSpace(topic) || string.Equals(topic, IndexTopic, StringComparison.OrdinalIgnoreCase)
            ? null
            : topic;
        var intro = Read(IndexName) ?? $"# {Str.Get("Help.Tab.Title")}\n";
        return new HelpDocument(new HelpTopic(IndexTopic, TitleOf(intro) ?? Str.Get("Help.Tab.Title"), IndexName),
                                HelpContents.AddTopics(BuildIndex(intro, missing)), missing);
    }

    /// <summary>A page as written — without the Topics menu the pane shows it with.</summary>
    public string? Source(HelpTopic topic) => Read(topic.LogicalName);

    /// <summary>The heading a help link points at — <c>searching</c> in <c>help:Help#searching</c> — or null.</summary>
    public static string? AnchorFromLink(string? url)
    {
        var hash = url?.IndexOf('#') ?? -1;
        return hash >= 0 && hash < url!.Length - 1 ? Uri.UnescapeDataString(url[(hash + 1)..].Trim()) : null;
    }

    /// <summary>
    /// The name in the language pack of a picture a help page shows, resolved against the page's own pack folder (<c>images/x.png</c>
    /// beside <c>help/Markdown.md</c> is <c>Nexaflow.Features.Markdown/help/images/x.png</c>). Never climbs out of the page's project,
    /// never follows a scheme: null for anything else.
    /// </summary>
    public string? PictureName(HelpTopic page, string src)
        => string.IsNullOrWhiteSpace(src) || src.Contains(':')   // http:, data:, a drive — not a pack path
            ? null
            : Combine(page.Folder, src);

    /// <summary>The bytes of the picture named <paramref name="name"/> (<see cref="PictureName"/>) in the active language, or English;
    /// null when no pack holds it. Read whole, so nothing holds the pack's stream once they are returned.</summary>
    public byte[]? ReadPicture(string name)
    {
        using var stream = _language.OpenResource(name);
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>The help page a link inside help points at: <c>help:Text</c>, or a page by file name
    /// (<c>Text.md</c>, <c>../../Nexaflow.Features.Text/help/Text.md</c>). Null for anything else — a web link opens
    /// in the browser.</summary>
    public static string? TopicFromLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var target = url.Split('#', 2)[0].Trim();

        if (target.StartsWith("help:", StringComparison.OrdinalIgnoreCase))
            return target[5..].Trim() is { Length: > 0 } topic ? topic : IndexTopic;
        if (target.Contains(':') || !target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return null;

        var file = target.Replace('\\', '/').Split('/')[^1];
        return file[..^3] is { Length: > 0 } name ? name : null;
    }

    /// <summary><paramref name="folder"/>/<paramref name="src"/> with <c>./</c> and <c>../</c> collapsed; null when it
    /// would climb above the folder's first segment (the project).</summary>
    internal static string? Combine(string folder, string src)
    {
        var parts = new List<string>(folder.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var segment in Uri.UnescapeDataString(src).Replace('\\', '/').Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (parts.Count <= 1) return null;
                parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(segment);
        }
        return string.Join('/', parts);
    }

    // ── Reading ───────────────────────────────────────────────────────────

    private IReadOnlyList<HelpTopic> ReadTopics()
    {
        var topics = new Dictionary<string, HelpTopic>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _language.ResourceNames(n => HelpPageName().IsMatch(n)).Order(StringComparer.Ordinal))
        {
            var topic = name[(name.LastIndexOf('/') + 1)..^3];
            if (string.Equals(topic, IndexTopic, StringComparison.OrdinalIgnoreCase) || topics.ContainsKey(topic)) continue;
            topics[topic] = new HelpTopic(topic, TitleOf(Read(name)) ?? topic, name);
        }
        return topics.Values.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private string? Read(string logicalName)
    {
        using var stream = _language.OpenResource(logicalName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    // The first "# " heading — a page's title, as it names itself.
    private static string? TitleOf(string? markdown)
    {
        if (markdown is null) return null;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("# ", StringComparison.Ordinal))
                return line[2..].Trim().TrimEnd('#').Trim() is { Length: > 0 } title ? title : null;
        }
        return null;
    }

    // Core's index page, then the list of every help page. When a page kind with no help was asked for, say so
    // straight under the heading — the reader expected something else here.
    private string BuildIndex(string intro, string? missing)
    {
        var sb = new StringBuilder();
        var newline = intro.IndexOf('\n');
        if (missing is not null && intro.StartsWith("# ", StringComparison.Ordinal) && newline > 0)
            sb.Append(intro[..newline].TrimEnd()).Append("\n\n> ").Append(Str.Format("Help.NoShowcase", missing))
              .Append('\n').Append(intro[newline..]);
        else if (missing is not null)
            sb.Append("> ").Append(Str.Format("Help.NoShowcase", missing)).Append("\n\n").Append(intro);
        else
            sb.Append(intro);

        sb.Append("\n\n## ").Append(Str.Get("Help.Index.AllTopics")).Append("\n\n");
        foreach (var topic in Topics)
            sb.Append("- [").Append(topic.Title).Append("](help:").Append(topic.Topic).Append(")\n");
        return sb.ToString();
    }

    private void Forget()
    {
        lock (_gate)
        {
            _topics = null;
            _index  = null;
        }

    }
}
