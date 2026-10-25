using System;
using System.Text.RegularExpressions;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// A regular expression a caller typed — a grep, a <c>like</c>, a diagnostics filter — compiled with the one limit every
/// such pattern gets.
/// <para>
/// A pattern that can match the same text many ways (<c>[^\n]*:\s*[^\n]*\b</c>) backtracks without end on a long enough
/// line, and it runs in the resident process beside every other session's questions: one such sweep held a core for
/// nearly ten minutes. Past <see cref="Budget"/> on any one match it is stopped, and the caller is told why
/// (<see cref="TooSlow"/>) rather than left waiting.
/// </para>
/// </summary>
public static class UserPattern
{
    /// <summary>How long one match may run. Generous for any pattern that is merely slow; a pattern past it is not
    /// going to finish.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(90);

    /// <exception cref="ArgumentException">The pattern is not a regular expression.</exception>
    public static Regex Compile(string pattern, RegexOptions options = RegexOptions.None) => new(pattern, options, Budget);

    /// <summary>What to tell a caller whose <paramref name="pattern"/> ran past <see cref="Budget"/>.</summary>
    public static string TooSlow(string pattern) =>
        $"/{pattern}/ ran past {Budget.TotalSeconds:F0}s on a single match and was stopped: it backtracks. Anchor it, "
      + "or replace a wildcard that can match the same text as the one beside it ([^\\n]*, .*) with one that cannot.";
}
