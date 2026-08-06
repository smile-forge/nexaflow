using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Email.Model;
using Nexaflow.Search;
using Nexaflow.Visuals.Text.Markdown;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Email.ViewModels;

/// <summary>
/// The email viewer as a searchable page. It searches what the message is currently showing: the body pane
/// on screen, the raw headers only while that list is open, and every attachment — by name, by the text
/// inside it, and into any message it carries. Each hit says where it came from, and displaying one lights
/// up that place: the matched body text, the matching header row, or the attachment tile.
/// </summary>
internal sealed partial class EmailViewModel : ISearchable
{
    /// <summary>Hits handed to the agent in one call — enough to reason over without flooding its context.</summary>
    private const int SearchHitCap = 200;

    /// <summary>Header values and attachment text are unbounded (a Received chain, a whole document); cap
    /// the preview shown for a hit.</summary>
    private const int PreviewCap = 200;

    /// <summary>How deep to follow forwarded-message attachments. Bounds the recursion for a pathological
    /// message-in-message-in-… without needing a cycle check.</summary>
    private const int MaxNesting = 8;

    [ObservableProperty] private bool   _isSearchActive;
    [ObservableProperty] private int    _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    public bool HasSearchMatches => SearchMatchCount > 0;

    partial void OnSearchMatchCountChanged(int value) => OnPropertyChanged(nameof(HasSearchMatches));

    // ── View collaboration ────────────────────────────────────────────────────
    // The rendered body highlights itself (the view owns its FlowDocument); a plain-text / HTML-source body
    // is a text box whose match is shown by selecting it. Set by EmailView; absent in a headless test.

    /// <summary>Runs the matcher against the rendered body surface, highlighting it, and returns the matches.</summary>
    internal System.Func<TextSearchMatcher, IReadOnlyList<RenderedMatch>>? FindInRenderedBody { get; set; }
    internal System.Action<int>? StepRenderedBody { get; set; }
    internal System.Action?      ClearRenderedBody { get; set; }

    /// <summary>Asks the active body text box to select and reveal a span.</summary>
    public event System.Action<int, int>? BodySelectionRequested;

    private bool _renderedBodyActive;
    private (int Offset, int Length)[] _bodyMatches = [];
    private int _currentBody = -1;

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription =>
        string.IsNullOrEmpty(Subject)
            ? $"the open email message '{Path.GetFileName(_path)}' — its body, its headers, and its attachments"
            : $"the open email '{Subject}' — its body, its headers, and its attachments";

    /// <summary>Same curve as the other document viewers: a term or two is almost certainly a search.</summary>
    public float ScoreQuery(string input) => SearchScoring.TermCount(input) switch
    {
        1 => 0.9f,
        2 => 0.8f,
        3 => 0.6f,
        4 => 0.2f,
        _ => 0f,
    };

    /// <summary>The body currently on screen — what a search of "this page" looks in.</summary>
    private string ShownBody => BodyView switch
    {
        EmailBodyView.PlainText  => PlainText,
        EmailBodyView.HtmlSource => HtmlSource,
        _                        => RenderedMarkdown,
    };

    public async Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (!TextSearchMatcher.TryCreate(request, out var matcher, out var error))
            return SearchOutcome.Unsupported(error);

        if (!display)
            return CollectHits(matcher, ct);

        return await _shell.RunOnUiAsync(() => Task.FromResult(Show(matcher, request, ct)));
    }

    // ShowResultsAsync is left at the interface default: a message is a fixed envelope and one body, not a
    // list, so there is nothing honest to narrow an arbitrary subset to.

    // ── Agent read (no UI) ──────────────────────────────────────────────────────

    private SearchOutcome CollectHits(TextSearchMatcher matcher, CancellationToken ct)
    {
        var hits  = new List<SearchHit>();
        var total = 0;

        // Headers — only when the raw list is open, mirroring what a "?" search would light up.
        if (HeadersExpanded)
            for (var i = 0; i < AllHeaders.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var h = AllHeaders[i];
                if (!matcher.Matches($"{h.Field}: {h.Value}")) continue;
                total++;
                Add(hits, $"header:{i}", h.Field, h.Value);
            }

        // Body — whatever pane is showing.
        foreach (var line in matcher.ScanLines(ShownBody))
        {
            ct.ThrowIfCancellationRequested();
            total++;
            Add(hits, $"body:{line.Index}", $"line {line.Index + 1}", line.Text);
        }

        // Attachments — name, text contents, and any message they carry.
        for (var i = 0; i < Attachments.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (!AttachmentMatches(Attachments[i].Source, matcher, MaxNesting)) continue;
            total++;
            Add(hits, $"attachment:{i}", $"attachment: {Attachments[i].DisplayName}",
                $"{Attachments[i].ContentType}, {Attachments[i].SizeText}");
        }

        return total == 0 ? SearchOutcome.None() : SearchOutcome.Found(hits, total);
    }

    private static void Add(List<SearchHit> hits, string id, string label, string preview)
    {
        if (hits.Count >= SearchHitCap) return;
        var text = preview.Trim();
        hits.Add(new SearchHit(id, label, text.Length > PreviewCap ? text[..PreviewCap] + "…" : text));
    }

    // ── Display (on the UI thread) ──────────────────────────────────────────────

    private SearchOutcome Show(TextSearchMatcher matcher, SearchRequest request, CancellationToken ct)
    {
        ClearHighlights();
        CurrentSearchTerm = SearchSyntax.Format(request);
        IsSearchActive    = true;   // true at zero too: "no matches for X" is a result worth showing

        var hits  = new List<SearchHit>();
        var total = 0;

        if (HeadersExpanded)
            for (var i = 0; i < AllHeaders.Count; i++)
            {
                var h = AllHeaders[i];
                if (!matcher.Matches($"{h.Field}: {h.Value}")) continue;
                h.Highlighted = true;
                total++;
                Add(hits, $"header:{i}", h.Field, h.Value);
            }

        for (var i = 0; i < Attachments.Count; i++)
        {
            if (!AttachmentMatches(Attachments[i].Source, matcher, MaxNesting)) continue;
            Attachments[i].Highlighted = true;
            total++;
            Add(hits, $"attachment:{i}", $"attachment: {Attachments[i].DisplayName}",
                $"{Attachments[i].ContentType}, {Attachments[i].SizeText}");
        }

        total += ShowBody(matcher, ct, hits);

        SearchMatchCount = total;
        return total == 0 ? SearchOutcome.None() : SearchOutcome.Found(hits, total);
    }

    // Lights up the body matches in the pane on screen, returning how many there were.
    private int ShowBody(TextSearchMatcher matcher, CancellationToken ct, List<SearchHit> hits)
    {
        // The rendered pane highlights its own visible text; a text pane is selected into.
        if (BodyView == EmailBodyView.Rendered)
        {
            _renderedBodyActive = true;
            var matches = FindInRenderedBody?.Invoke(matcher) ?? [];
            foreach (var m in matches.Take(SearchHitCap))
                Add(hits, $"body:{m.Ordinal}", $"body match {m.Ordinal + 1}", m.Preview);
            return matches.Count;
        }

        var found = new List<(int Offset, int Length)>();
        foreach (var line in matcher.ScanLines(ShownBody))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var (index, length) in matcher.Occurrences(line.Text))
            {
                found.Add((line.Offset + index, length));
                Add(hits, $"body:{found.Count}", $"line {line.Index + 1}", line.Text);
            }
        }

        _bodyMatches = [.. found];
        _currentBody = found.Count > 0 ? 0 : -1;
        if (found.Count > 0) BodySelectionRequested?.Invoke(found[0].Offset, found[0].Length);
        return found.Count;
    }

    // ── Attachment content search (name → text → nested message) ────────────────

    private bool AttachmentMatches(EmailAttachment a, TextSearchMatcher matcher, int depth)
    {
        if (matcher.Matches(a.DisplayName)) return true;

        if (IsTextContentType(a.ContentType) && Decode(a.Content) is { } text && matcher.ScanLines(text).Any())
            return true;

        // A carried message (message/rfc822): search its headers, its body, and — recursively — anything it
        // in turn carries, so "find invoice" reaches an invoice attached to a forwarded mail.
        if (depth > 0 && IsMessageContentType(a.ContentType) && ParseNested(a) is { } nested)
        {
            if (nested.Headers.Any(h => matcher.Matches($"{h.Field}: {h.Value}"))) return true;

            var body = !string.IsNullOrEmpty(nested.TextBody) ? nested.TextBody : nested.HtmlBody;
            if (!string.IsNullOrEmpty(body) && matcher.ScanLines(body).Any()) return true;

            if (nested.Attachments.Any(na => AttachmentMatches(na, matcher, depth - 1))) return true;
        }
        return false;
    }

    private static string? Decode(byte[] bytes)
    {
        try   { return Encoding.UTF8.GetString(bytes); }
        catch { return null; }
    }

    private static EmailDocument? ParseNested(EmailAttachment a)
    {
        try
        {
            using var ms = new MemoryStream(a.Content, writable: false);
            return Reading.EmailDocumentReader.Instance.Read(ms, a.DisplayName);
        }
        catch { return null; }
    }

    private static bool IsMessageContentType(string? contentType) =>
        contentType?.Split(';')[0].Trim().Equals("message/rfc822", System.StringComparison.OrdinalIgnoreCase) == true;

    // ── Navigation / dismissal (the toolbar chip) ─────────────────────────────

    [RelayCommand]
    private void FindNextMatch() => StepBody(+1);

    [RelayCommand]
    private void FindPreviousMatch() => StepBody(-1);

    // Stepping walks the body occurrences — the positional matches. Header and attachment hits are shown as
    // steady highlights on their row/tile, not points to scroll between.
    private void StepBody(int delta)
    {
        if (_renderedBodyActive) { StepRenderedBody?.Invoke(delta); return; }
        if (_bodyMatches.Length == 0) return;
        _currentBody = (_currentBody + delta + _bodyMatches.Length) % _bodyMatches.Length;
        BodySelectionRequested?.Invoke(_bodyMatches[_currentBody].Offset, _bodyMatches[_currentBody].Length);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        ClearHighlights();
        SearchMatchCount  = 0;
        CurrentSearchTerm = string.Empty;
        IsSearchActive    = false;
    }

    private void ClearHighlights()
    {
        if (_renderedBodyActive) ClearRenderedBody?.Invoke();
        _renderedBodyActive = false;
        _bodyMatches = [];
        _currentBody = -1;
        foreach (var h in AllHeaders)  h.Highlighted = false;
        foreach (var a in Attachments) a.Highlighted = false;
    }
}
