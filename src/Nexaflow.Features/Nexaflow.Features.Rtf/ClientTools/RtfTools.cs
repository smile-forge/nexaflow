using System.Text;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Rtf.ViewModels;

namespace Nexaflow.Features.Rtf.ClientTools;

/// <summary>
/// The AI tools the rich-text page exposes. The page only shows its document, so the one tool only reads it: the words the
/// document draws, by paragraph, a range at a time — read-only, so it runs without asking. Finding where something is
/// goes through the page's own search, which every searchable page offers the agent.
/// </summary>
internal static class RtfTools
{
    /// <summary>
    /// Characters handed back in one call. A long document is read a stretch at a time rather than poured into the
    /// conversation whole; the answer says where to carry on from.
    /// </summary>
    internal const int ReadCap = 40_000;

    public static IReadOnlyList<IClientTool> For(RtfViewModel vm) =>
    [
        new DelegateClientTool(
            "rtf_read_text",
            "Read the words of this rich-text document as it draws them — a paragraph to a line, numbered, a table's cells "
            + "a tab apart. Without a range it reads from the start for as much as one answer holds, and says where to carry on.",
            [
                new ClientToolParameter("from", "1-based paragraph to start at.", Required: false, Type: "integer"),
                new ClientToolParameter("count", "How many paragraphs to read.", Required: false, Type: "integer"),
            ],
            ToolSafety.SafeOperation,
            (args, _) => Task.FromResult(Read(vm, ToolArgs.Int(args, "from", 1), ToolArgs.IntOrNull(args, "count"))),
            parallelizable: true),
    ];

    /// <summary>Paragraphs <paramref name="from"/> onwards, <paramref name="count"/> of them or as many as one answer holds.</summary>
    internal static ToolResult Read(RtfViewModel vm, int from, int? count)
    {
        if (!vm.IsLoaded) return ToolResult.Error("The document hasn't finished loading.");
        if (vm.HasError) return ToolResult.Error($"The document couldn't be read: {vm.ErrorMessage}");

        var paragraphs = vm.Words.Length == 0 ? [] : vm.Words.Split('\n');
        if (paragraphs.Length == 0) return ToolResult.Ok("document is empty", "(the document draws no words)");

        // Reading paragraph 1 when the model asked for paragraph 500 would have it reason about the wrong part of the document.
        if (from < 1 || from > paragraphs.Length)
            return ToolResult.Error($"There is no paragraph {from}: the document has {paragraphs.Length:N0}.");

        var last = count is { } wanted ? Math.Min(paragraphs.Length, from - 1 + Math.Max(1, wanted)) : paragraphs.Length;

        var text = new StringBuilder();
        var at = from;

        for (; at <= last; at++)
        {
            var line = $"{at}: {paragraphs[at - 1]}\n";
            if (text.Length > 0 && text.Length + line.Length > ReadCap) break;
            text.Append(line);
        }

        var read = at - from;
        if (at <= last)
            text.Append($"[Stopped at paragraph {at - 1} of {paragraphs.Length:N0} to keep this answer short — call again with from={at} for the rest.]");

        return ToolResult.Ok($"read {read} paragraph(s) of {paragraphs.Length:N0}", text.ToString().TrimEnd('\n'));
    }
}
