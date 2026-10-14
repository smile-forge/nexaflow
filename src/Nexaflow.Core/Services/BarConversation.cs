using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services;

/// <summary>
/// The conversation held in the AI bar: each request typed into it continues the last, the model given everything the
/// earlier runs added — what was said, what was called and what came of it — until the reply is dismissed or continued as a
/// conversation, which ends it. A run that ended without a reply adds nothing, and nor does one still running when its
/// conversation ended.
/// </summary>
public sealed class BarConversation
{
    private readonly List<LlmMessage> _thread = [];
    private readonly List<(string Request, string Reply)> _exchanges = [];
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private int _generation;

    /// <summary>Each request answered, and its reply, oldest first.</summary>
    public IReadOnlyList<(string Request, string Reply)> Exchanges => _exchanges;

    /// <summary>The files the conversation's runs touched.</summary>
    public IReadOnlyCollection<string> Paths => _paths;

    /// <summary>The run of the next request: the conversation so far as its history, the reply written in
    /// <paramref name="replyStyle"/>, and what the run adds kept through <paramref name="keep"/> — which hands it back on the
    /// caller's thread, where <see cref="Keep"/> takes it.</summary>
    public AgentRunOptions Run(string replyStyle, Action<Action> keep)
    {
        var generation = _generation;
        return new AgentRunOptions
        {
            History    = [.. _thread],
            ReplyStyle = replyStyle,
            Ran        = ran => keep(() => Keep(generation, ran)),
        };
    }

    /// <summary>A request answered, with <paramref name="reply"/>.</summary>
    public void Answered(string request, string reply) => _exchanges.Add((request, reply));

    /// <summary>Files a run of the conversation touched.</summary>
    public void Touched(IEnumerable<string> paths) => _paths.UnionWith(paths);

    /// <summary>Ends the conversation: the next request starts a new one.</summary>
    public void End()
    {
        _thread.Clear();
        _exchanges.Clear();
        _paths.Clear();
        _generation++;
    }

    private void Keep(int generation, IReadOnlyList<LlmMessage> ran)
    {
        if (generation != _generation) return;
        if (ran is [.., { Role: LlmRole.Assistant, ToolCalls: null or [] } last] && last.Text.Trim().Length > 0)
            _thread.AddRange(ran);
    }
}
