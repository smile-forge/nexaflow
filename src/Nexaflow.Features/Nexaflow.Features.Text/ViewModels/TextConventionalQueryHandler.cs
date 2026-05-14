using Nexaflow.Features.Common;
using System.Text.RegularExpressions;

namespace Nexaflow.Features.Text.ViewModels;

/// <summary>
/// Global query handler that searches the active text file for plain text.
/// Scores based on term count: fewer terms = higher confidence it is a text search.
/// </summary>
public sealed class TextConventionalQueryHandler : IQueryHandler
{
    // Splits on whitespace but keeps quoted phrases as a single term
    private static readonly Regex TermSplitter =
        new(@"""[^""]*""|'[^']*'|\S+", RegexOptions.Compiled);

    // /pattern/ syntax is exclusively handled by the regex handler
    private static readonly Regex SlashSyntax = new(@"^/.*/$", RegexOptions.Compiled);

    public string Description =>
        "Searches the open text file for the entered text using plain-text matching.";

    public float CanProcess(string input)
    {
        if (ActiveTextViewTracker.Instance.Current is null) return 0f;

        var trimmed = input.Trim();
        if (trimmed.Length == 0) return 0f;

        // Yield to the regex handler for /pattern/ syntax
        if (SlashSyntax.IsMatch(trimmed)) return 0f;

        var terms = TermSplitter.Matches(trimmed).Count;
        return terms switch
        {
            1 => 0.9f,
            2 => 0.8f,
            3 => 0.6f,
            4 => 0.2f,
            _ => 0f,
        };
    }

    public async Task<string?> ProcessAsync(string input)
    {
        var vm = ActiveTextViewTracker.Instance.Current;
        if (vm is null) return "No text file is currently open.";

        await vm.SearchConventionalAsync(input.Trim(), CancellationToken.None);
        return null; // results appear as highlights in the view
    }
}
