using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Agent;

/// <summary>
/// Which tools the model is offered at each step of a run. A page with a few tools offers them all. Past that, the
/// Disambiguation model picks the few the next step needs — chosen again whenever the work moves on (the user adds an
/// instruction, a step's calls are answered), from everything the user has asked and what the steps so far did, so what
/// is offered follows the work rather than its first sentence. With no model to pick, the first few are offered. The
/// caller's own tools are always offered, and so is the tool that lists the rest; once the model has had them listed,
/// every one is.
/// </summary>
internal sealed class ToolChooser
{
    /// <summary>How many of the page's tools are offered at once when it has more.</summary>
    public const int Offered = 4;

    /// <summary>How much of a call's outcome the picking model reads: enough to know what it did.</summary>
    private const int OutcomeShown = 200;

    private readonly IReadOnlyList<IClientTool> _pageTools;
    private readonly IReadOnlyList<IClientTool> _always;
    private readonly ILlmProvider? _ranker;
    private readonly string _context;
    private readonly List<string> _asked = [];
    private readonly List<string> _done = [];
    private IReadOnlyList<IClientTool>? _chosen;
    private bool _listed;

    /// <param name="pageTools">The tools the page and the shell offer, the ones picked among.</param>
    /// <param name="lister">The tool that lists every page tool to the model.</param>
    /// <param name="own">The caller's own tools, never picked away.</param>
    /// <param name="ranker">The model that picks, or null to offer the first few.</param>
    /// <param name="context">What the user is looking at, as the conversation model reads it.</param>
    /// <param name="instruction">What the user asked.</param>
    public ToolChooser(IReadOnlyList<IClientTool> pageTools, IClientTool lister, IReadOnlyList<IClientTool> own,
                       ILlmProvider? ranker, string context, string instruction)
    {
        _pageTools = pageTools;
        _always    = [lister, .. own];
        _ranker    = Picks(pageTools.Count) ? ranker : null;
        _context   = context;
        _asked.Add(instruction);
        All = [.. pageTools.Concat(_always).Distinct()];
    }

    /// <summary>Whether a page with this many tools has a model pick among them.</summary>
    public static bool Picks(int pageTools) => pageTools > Offered;

    /// <summary>Every tool the run can call, offered or not: a call to any of them is run.</summary>
    public IReadOnlyList<IClientTool> All { get; }

    /// <summary>The user added instructions while the run worked.</summary>
    public void Asked(IReadOnlyList<string> instructions)
    {
        if (instructions.Count == 0) return;
        _asked.AddRange(instructions);
        _chosen = null;
    }

    /// <summary>A call was answered.</summary>
    public void Did(string tool, JsonObject arguments, string outcome)
    {
        var shown = outcome.Length <= OutcomeShown ? outcome : outcome[..OutcomeShown] + "…";
        _done.Add($"{tool} {arguments.ToJsonString()}: {shown.ReplaceLineEndings(" ")}");
        _chosen = null;
    }

    /// <summary>The model had every tool listed: from now on every one is offered.</summary>
    public void Listed()
    {
        _listed = true;
        _chosen = null;
    }

    /// <summary>The tools to offer for the next step — chosen again only when the work has moved on since.</summary>
    public async Task<IReadOnlyList<IClientTool>> ChooseAsync(CancellationToken ct)
    {
        if (_chosen is not null) return _chosen;

        IReadOnlyList<IClientTool> page = _listed || !Picks(_pageTools.Count) ? _pageTools
                                        : _ranker is null                     ? [.. _pageTools.Take(Offered)]
                                        : await PickAsync(_ranker, ct);
        return _chosen = [.. page.Concat(_always).Distinct()];
    }

    /// <summary>Asks the picking model for the tools the next step needs; the first few when it cannot say.</summary>
    private async Task<IReadOnlyList<IClientTool>> PickAsync(ILlmProvider ranker, CancellationToken ct)
    {
        var system = "You select the tools the next step of a user's request needs. Reply with the numbers of up to " +
                     $"{Offered} tools, comma-separated, most needed first. Reply 0 if none are needed.";

        var user = new StringBuilder($"Context: {_context}\n");
        user.Append("The user asked: \"").Append(_asked[0]).Append("\"\n");
        foreach (var added in _asked.Skip(1)) user.Append("Then: \"").Append(added).Append("\"\n");
        if (_done.Count > 0)
        {
            user.Append("Done so far:\n");
            foreach (var step in _done) user.Append("- ").Append(step).Append('\n');
        }
        user.Append("\nTools:\n");
        for (int i = 0; i < _pageTools.Count; i++) user.Append(i + 1).Append(". ").Append(_pageTools[i].Name).Append(" — ").Append(_pageTools[i].Description).Append('\n');
        user.Append("\nMost relevant tool numbers for what is left to do:");

        string raw;
        try { raw = (await ranker.CompleteAsync([new(LlmRole.System, system), new(LlmRole.User, user.ToString())], ct))?.RawText ?? ""; }
        catch (OperationCanceledException) { throw; }
        catch { return [.. _pageTools.Take(Offered)]; }

        var picked = new List<IClientTool>();
        foreach (var n in Numbers(raw))
        {
            if (n < 1 || n > _pageTools.Count) continue;
            var tool = _pageTools[n - 1];
            if (!picked.Contains(tool)) picked.Add(tool);
            if (picked.Count >= Offered) break;
        }
        return picked.Count > 0 ? picked : [.. _pageTools.Take(Offered)];
    }

    /// <summary>Every whole number written in <paramref name="s"/>, in order.</summary>
    private static IEnumerable<int> Numbers(string s)
    {
        var i = 0;
        while (i < s.Length)
        {
            if (!char.IsDigit(s[i])) { i++; continue; }
            var start = i;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (int.TryParse(s.AsSpan(start, i - start), out var n)) yield return n;
        }
    }
}
