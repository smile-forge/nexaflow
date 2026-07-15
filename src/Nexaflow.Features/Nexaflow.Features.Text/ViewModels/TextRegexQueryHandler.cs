using Nexaflow.Features.Common;
using System.Text.RegularExpressions;

namespace Nexaflow.Features.Text.ViewModels;

/// <summary>
/// Global query handler that searches the active text file using a regular expression. Its <see cref="Symbol"/>
/// is "\": a leading backslash forces regex interpretation (<c>prefixed</c>); without it, the handler still
/// competes when the input looks like a regex (contains regex special characters).
/// </summary>
public sealed class TextRegexQueryHandler : IQueryHandler
{
    private static readonly char[] RegexSpecialChars =
        ['\\', '.', '[', ']', '{', '}', '(', ')', '*', '+', '?', '^', '$', '|'];

    public string  Symbol      => "\\";
    public string Description =>
        "Searches the open text file using a regular expression. " +
        "Prefix with \\ to force regex, or type a pattern containing regex characters.";

    public float CanProcess(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm is not TextViewModel) return 0f;

        var trimmed = input.Trim();
        if (trimmed.Length == 0) return 0f;

        try { _ = new Regex(trimmed); }
        catch { return 0f; }   // not a valid regex → not ours

        // Explicit "\" → almost certainly a regex request; otherwise infer from regex special characters.
        if (prefixed) return 0.95f;
        return trimmed.IndexOfAny(RegexSpecialChars) >= 0 ? 0.85f : 0.3f;
    }

    public async Task<string?> ProcessAsync(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm is TextViewModel vm)
            await vm.SearchRegexAsync(input.Trim(), CancellationToken.None);
        return null; // results appear as highlights in the view
    }
}
