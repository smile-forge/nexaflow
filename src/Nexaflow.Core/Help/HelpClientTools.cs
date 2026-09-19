using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;

namespace Nexaflow.Core.Help;

/// <summary>
/// The help corpus as tools, offered on every page rather than by a feature.
///
/// A model that was not trained on Nexaflow cannot guess that a fenced <c>smiles</c> block draws a molecule, and
/// it will not search for a word it has never read — so the ladder here has three rungs on purpose: what pages
/// exist, which of them mention a thing, and then the page itself. Without the first rung a search tool only
/// answers questions the model already knew to ask.
///
/// The pages are the ones F1 shows, read from the same language pack, so an answer given here and an answer read
/// on screen cannot disagree — and neither can go stale while the help is kept current.
/// </summary>
/// <param name="library">The help to read. Injected so a test can hand it a pack of its own.</param>
internal sealed class HelpClientTools(HelpLibrary library)
{
    /// <summary>Pages listed for one search. Enough to choose from, short enough not to crowd the turn.</summary>
    private const int MaxResults = 15;

    /// <summary>A help page is a few thousand characters; this only catches one that has grown unreasonable.</summary>
    private const int MaxPageChars = 20_000;

    public IReadOnlyList<IClientTool> Tools =>
    [
        new DelegateClientTool(
            "list_help_topics",
            "Lists every help page Nexaflow ships, by id and title. Start here when you need to know what the "
          + "app can do or what a document can contain — the titles name the features and the kinds of block "
          + "the Markdown editor draws. Takes no arguments.",
            [],
            ToolSafety.SafeOperation,
            ListAsync,
            parallelizable: true),

        new DelegateClientTool(
            "search_help",
            "Searches every help page and returns the ones that mention the query, most matches first, with a "
          + "line of context. Use it to find the page that covers something before answering from memory. "
          + "Supports the help pane's own syntax: `split pane` for lines holding both words, `pan*` for a "
          + "prefix, and `/F[0-9]+/` for a regular expression.",
            [
                new ClientToolParameter("query", "What to look for, in the syntax described above."),
            ],
            ToolSafety.SafeOperation,
            SearchAsync,
            parallelizable: true),

        new DelegateClientTool(
            "read_help",
            "Returns one help page in full, as markdown, given an id from list_help_topics or search_help. Read "
          + "the page before telling the user how to do something, so what you tell them matches what the app "
          + "actually does.",
            [
                new ClientToolParameter("topic", "The page id, e.g. 'Markdown' or 'MarkdownScience'."),
            ],
            ToolSafety.SafeOperation,
            ReadAsync,
            parallelizable: true),
    ];

    private Task<ToolResult> ListAsync(JsonObject args, CancellationToken ct)
    {
        var topics = library.Topics;
        if (topics.Count == 0)
            return Task.FromResult(ToolResult.Error("No help available", "This build ships no help pages."));

        var sb = new StringBuilder($"{topics.Count} help page(s). Read one with read_help(topic=<id>).\n");
        foreach (var topic in topics) sb.Append("id=").Append(topic.Topic).Append(" | ").Append(topic.Title).Append('\n');

        return Task.FromResult(ToolResult.Ok($"{topics.Count} help page(s)", sb.ToString()));
    }

    private async Task<ToolResult> SearchAsync(JsonObject args, CancellationToken ct)
    {
        var query = ToolArgs.Str(args, "query", "text", "pattern");
        if (string.IsNullOrWhiteSpace(query))
            return ToolResult.Error("No query given", "The 'query' argument is required.");

        if (!TextSearchMatcher.TryCreate(SearchSyntax.ParseRequest(query), out var matcher, out var error))
            return ToolResult.Error("Bad query", error);

        await library.Index.EnsureBuiltAsync();
        ct.ThrowIfCancellationRequested();

        var results = library.Index.Search(matcher, excludeTopic: null);
        if (results.Count == 0)
            return ToolResult.Ok(
                $"No help mentions '{query}'",
                $"No help page mentions '{query}'. list_help_topics shows every page there is — the wording on "
              + "the page may differ from yours.");

        var shown = results.Take(MaxResults).ToList();
        var sb    = new StringBuilder($"{results.Count} page(s) mention '{query}'");
        if (shown.Count < results.Count) sb.Append($", showing {shown.Count}");
        sb.Append(":\n");

        foreach (var result in shown)
            sb.Append("id=").Append(result.Topic.Topic)
              .Append(" | ").Append(result.Topic.Title)
              .Append(" | ").Append(result.CountLabel)
              .Append(" | ").Append(result.Preview)
              .Append('\n');

        return ToolResult.Ok($"{results.Count} page(s) mention '{query}'", sb.ToString());
    }

    private Task<ToolResult> ReadAsync(JsonObject args, CancellationToken ct)
    {
        var wanted = ToolArgs.Str(args, "topic", "page", "id");
        if (string.IsNullOrWhiteSpace(wanted))
            return Task.FromResult(ToolResult.Error("No topic given", "The 'topic' argument is required."));

        if (library.Find(wanted) is not { } topic)
            return Task.FromResult(ToolResult.Error(
                $"No help page '{wanted}'",
                $"There is no help page with id '{wanted}'. Call list_help_topics for the ids that exist."));

        var markdown = library.Load(topic.Topic).Markdown;
        if (markdown.Length > MaxPageChars)
            markdown = markdown[..MaxPageChars] + "\n\n[…the rest of this page was not read]";

        return Task.FromResult(ToolResult.Ok($"Read help: {topic.Title}", markdown));
    }
}
