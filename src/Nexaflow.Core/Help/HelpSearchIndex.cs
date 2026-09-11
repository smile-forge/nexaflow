using Nexaflow.Features.Common.Search;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Help;

/// <summary>One help page's share of a search: how often it matched, and the first line it matched on.</summary>
internal sealed record HelpSearchResult(HelpTopic Topic, int MatchCount, string Preview)
{
    public string CountLabel => MatchCount == 1
        ? Str.Get("Help.OtherPages.Match")
        : Str.Format("Help.OtherPages.Matches", MatchCount);
}

/// <summary>One match: the page, which match it is on that page (0-based, in reading order — the ordinal the page's
/// own highlight steps through), and the line it sits on.</summary>
internal sealed record HelpHit(HelpTopic Topic, int Ordinal, string Preview);

/// <summary>
/// The "then every help page" half of a help search. Holds each page as <see cref="HelpPlainText"/> and matches with
/// the same <see cref="TextSearchMatcher"/> the open page highlights with, so its counts and ordinals agree with what
/// the reader sees. Built once, off the UI thread, the first time a search needs it.
/// </summary>
internal sealed class HelpSearchIndex(Func<IReadOnlyList<(HelpTopic Topic, string Markdown)>> source)
{
    private const int PreviewLength = 140;

    private readonly object _gate = new();
    private Task<IReadOnlyList<(HelpTopic Topic, string Text)>>? _pages;

    /// <summary>Starts (or joins) the one-off build.</summary>
    public Task EnsureBuiltAsync() => Pages();

    /// <summary>Every page but <paramref name="excludeTopic"/> that matches, most matches first.</summary>
    public IReadOnlyList<HelpSearchResult> Search(TextSearchMatcher matcher, string? excludeTopic)
    {
        var results = new List<HelpSearchResult>();
        foreach (var (topic, text) in Pages().GetAwaiter().GetResult())
        {
            if (string.Equals(topic.Topic, excludeTopic, StringComparison.OrdinalIgnoreCase)) continue;

            var count = 0;
            string? preview = null;
            foreach (var line in matcher.ScanLines(text))
            {
                count   += Math.Max(1, matcher.Occurrences(line.Text).Count);
                preview ??= Preview(line.Text);
            }
            if (count > 0) results.Add(new HelpSearchResult(topic, count, preview!));
        }

        return results
            .OrderByDescending(r => r.MatchCount)
            .ThenBy(r => r.Topic.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Individual matches — <paramref name="currentTopic"/>'s first, then every other page's, most-matched page
    /// first — at most <paramref name="cap"/> of them; <paramref name="total"/> counts every one.</summary>
    public IReadOnlyList<HelpHit> Hits(TextSearchMatcher matcher, string? currentTopic, int cap, out int total)
    {
        var perPage = new List<(HelpTopic Topic, List<HelpHit> Hits)>();
        foreach (var (topic, text) in Pages().GetAwaiter().GetResult())
        {
            var hits = new List<HelpHit>();
            foreach (var line in matcher.ScanLines(text))
            {
                var preview = Preview(line.Text);
                var count   = Math.Max(1, matcher.Occurrences(line.Text).Count);
                for (var i = 0; i < count; i++)
                    hits.Add(new HelpHit(topic, hits.Count, preview));
            }
            if (hits.Count > 0) perPage.Add((topic, hits));
        }

        total = perPage.Sum(p => p.Hits.Count);
        return perPage
            .OrderByDescending(p => string.Equals(p.Topic.Topic, currentTopic, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(p => p.Hits.Count)
            .ThenBy(p => p.Topic.Title, StringComparer.CurrentCultureIgnoreCase)
            .SelectMany(p => p.Hits)
            .Take(cap)
            .ToList();
    }

    private Task<IReadOnlyList<(HelpTopic Topic, string Text)>> Pages()
    {
        lock (_gate)
            return _pages ??= Task.Run<IReadOnlyList<(HelpTopic Topic, string Text)>>(
                () => source().Select(page => (page.Topic, HelpPlainText.Extract(page.Markdown))).ToList());
    }

    private static string Preview(string line)
    {
        var text = line.Trim();
        return text.Length <= PreviewLength ? text : text[..(PreviewLength - 1)] + "…";
    }
}
