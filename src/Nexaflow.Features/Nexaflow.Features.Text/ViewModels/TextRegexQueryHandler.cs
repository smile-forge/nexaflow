using Nexaflow.Features.Common;
using System.Text.RegularExpressions;

namespace Nexaflow.Features.Text.ViewModels;

/// <summary>
/// Global query handler that searches the active text file using a regular expression.
/// Scores highly when input looks like a regex pattern (/pattern/ syntax or contains
/// regex special characters).
/// </summary>
public sealed class TextRegexQueryHandler : IQueryHandler
{
    private static readonly Regex SlashSyntax = new(@"^/.*/$", RegexOptions.Compiled);

    private static readonly char[] RegexSpecialChars =
        ['\\', '.', '[', ']', '{', '}', '(', ')', '*', '+', '?', '^', '$', '|'];

    public string  Symbol      => "/";
    public string Description =>
        "Searches the open text file using a regular expression. " +
        "Use /pattern/ syntax or a string containing regex characters.";

    public float CanProcess(string input, IPageView? page = null)
    {
        if (ActiveTextViewTracker.Instance.Current is null) return 0f;

        var trimmed = input.Trim();
        if (trimmed.Length == 0) return 0f;

        // /pattern/ syntax — almost certainly a regex request
        if (SlashSyntax.IsMatch(trimmed)) return 0.95f;

        var hasSpecial = trimmed.IndexOfAny(RegexSpecialChars) >= 0;

        try
        {
            _ = new Regex(trimmed);
            return hasSpecial ? 0.85f : 0.3f;
        }
        catch
        {
            return 0f;
        }
    }

    public async Task<string?> ProcessAsync(string input, IPageView? page = null)
    {
        var vm = ActiveTextViewTracker.Instance.Current;
        if (vm is null) return "No text file is currently open.";

        var trimmed = input.Trim();
        // Strip surrounding slashes if /pattern/ syntax
        var pattern = SlashSyntax.IsMatch(trimmed) ? trimmed[1..^1] : trimmed;

        await vm.SearchRegexAsync(pattern, CancellationToken.None);
        return null; // results appear as highlights in the view
    }
}
