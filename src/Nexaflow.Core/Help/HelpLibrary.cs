using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Core.Localization;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Help;

/// <summary>
/// The help pages the language packs hold: one <c>help/&lt;PageKind&gt;.md</c> per page kind, shipped by the project
/// that owns that page (its <c>Localization/&lt;code&gt;/help/</c>), plus Core's <c>help/index.md</c> — shown for a
/// page kind with no help of its own, followed by a generated list of every help page there is.
/// <para>
/// Reads through <see cref="LanguageManager"/>, so a page the active language lacks comes from English, and the lot is
/// forgotten when the language changes. Nothing is read until Help is first opened.
/// </para>
/// </summary>
internal sealed partial class HelpLibrary
{
    public const string IndexTopic = "index";
    private const string IndexName = "Nexaflow.Core/help/index.md";

    private static HelpLibrary? _shared;

    /// <summary>The process's library, over the app's language packs.</summary>
    public static HelpLibrary Shared => _shared ??= new HelpLibrary(LanguageManager.Instance);

    private readonly LanguageManager _language;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, ImageSource?> _images = new(StringComparer.Ordinal);
    private IReadOnlyList<HelpTopic>? _topics;
    private HelpSearchIndex? _index;

    internal HelpLibrary(LanguageManager language)
    {
        _language = language;
        language.Changed += Forget;
    }

    [GeneratedRegex(@"^[^/]+/help/[^/]+\.md$")]
    private static partial Regex HelpPageName();

    /// <summary>Every help page but the index, by title.</summary>
    public IReadOnlyList<HelpTopic> Topics
    {
        get { lock (_gate) return _topics ??= ReadTopics(); }
    }

    /// <summary>The "then every help page" half of a search, built the first time one needs it.</summary>
    public HelpSearchIndex Index
    {
        get
        {
            lock (_gate)
                return _index ??= new HelpSearchIndex(() => Topics.Select(t => (t, Read(t.LogicalName) ?? "")).ToList());
        }
    }

    /// <summary>The help page for <paramref name="topic"/> (a page kind), if there is one.</summary>
    public HelpTopic? Find(string? topic)
        => string.IsNullOrWhiteSpace(topic)
            ? null
            : Topics.FirstOrDefault(t => string.Equals(t.Topic, topic, StringComparison.OrdinalIgnoreCase));

    /// <summary>The help for <paramref name="topic"/> (a page kind) — or, when it has none, the index saying so. Null,
    /// blank or <see cref="IndexTopic"/> asks for the index outright.</summary>
    public HelpDocument Load(string? topic)
    {
        if (Find(topic) is { } found && Read(found.LogicalName) is { } markdown)
            return new HelpDocument(found, markdown);

        var missing = string.IsNullOrWhiteSpace(topic) || string.Equals(topic, IndexTopic, StringComparison.OrdinalIgnoreCase)
            ? null
            : topic;
        var intro = Read(IndexName) ?? $"# {Str.Get("Help.Tab.Title")}\n";
        return new HelpDocument(new HelpTopic(IndexTopic, TitleOf(intro) ?? Str.Get("Help.Tab.Title"), IndexName),
                                BuildIndex(intro, missing), missing);
    }

    /// <summary>
    /// A picture a help page shows, resolved against the page's own pack folder (<c>images/x.png</c> beside
    /// <c>help/Markdown.md</c> is <c>Nexaflow.Features.Markdown/help/images/x.png</c>). Never climbs out of the page's
    /// project, never follows a scheme. Decoded fully into memory and frozen — every window's UI thread may draw it —
    /// and kept, so a page shown again costs nothing.
    /// </summary>
    public ImageSource? ResolveImage(HelpTopic page, string src)
    {
        if (string.IsNullOrWhiteSpace(src) || src.Contains(':')) return null;   // http:, data:, a drive — not a pack path
        return Combine(page.Folder, src) is { } name ? _images.GetOrAdd(name, Decode) : null;
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

    private ImageSource? Decode(string logicalName)
    {
        try
        {
            using var stream = _language.OpenResource(logicalName);
            if (stream is null) return null;

            // Copied out, so nothing holds the pack's stream once the picture is decoded.
            var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            buffer.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption  = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = buffer;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;   // not a picture WPF can decode: the alt text shows instead
        }
    }

    private void Forget()
    {
        lock (_gate)
        {
            _topics = null;
            _index  = null;
        }
        _images.Clear();
    }
}
