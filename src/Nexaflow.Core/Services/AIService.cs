using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Nexaflow.Core.Services;

public sealed class AIService : IAIService
{
    // ── Provider registry ─────────────────────────────────────────────────

    private readonly Dictionary<string, ILlmProvider> _providers
        = new(StringComparer.OrdinalIgnoreCase);

    private AiConfig? _abilityConfig;

    /// <summary>Registers a named provider. Called by WorkspaceManager during startup.</summary>
    public void Register(string name, ILlmProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers[name] = provider;
    }

    /// <summary>All registered providers, keyed by name.</summary>
    public IReadOnlyDictionary<string, ILlmProvider> AllProviders => _providers;

    /// <summary>Loads the ability-to-provider mapping. Called once per context when providers are ready.</summary>
    public void LoadAbilityConfig(AiConfig config) => _abilityConfig = config;

    /// <summary>
    /// Returns the provider and model assigned to <paramref name="ability"/>, or null if none
    /// is configured or the configured provider is not registered.
    /// </summary>
    private (ILlmProvider Provider, string Model)? GetProvider(AiAbility ability)
    {
        if (_abilityConfig is null) return null;

        var key = ability.ToString();
        if (!_abilityConfig.Assignments.TryGetValue(key, out var columnId)
            || string.IsNullOrEmpty(columnId))
            return null;

        var pair = _abilityConfig.Columns.FirstOrDefault(c => c.Id == columnId);
        if (pair is null) return null;

        return _providers.TryGetValue(pair.ProviderName, out var p)
            ? (p, pair.Model)
            : null;
    }

    /// <summary>
    /// Context window (in tokens) for the model assigned to the Conversation ability,
    /// or null if no provider is configured or the provider doesn't expose model info.
    /// </summary>
    public async Task<int?> GetConversationContextWindowAsync(CancellationToken ct = default)
    {
        var resolved = GetProvider(AiAbility.Conversation);
        if (resolved is null) return null;
        try
        {
            var info = await resolved.Value.Provider.GetModelInfoAsync(resolved.Value.Model, ct);
            return info?.ContextWindowTokens;
        }
        catch { return null; }
    }

    // ── Persistence ───────────────────────────────────────────────────────

    private readonly Workspace _workspace;
    private readonly string _baseDir;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented               = true,
        PropertyNameCaseInsensitive = true
    };

    private List<ConversationRecord> _conversations = [];

    public ConversationRecord? ActiveConversation { get; }

    /// <param name="workspace">The owning Workspace — used to resolve query handlers via FeatureManager.</param>
    /// <param name="conversationsDir">Full path to the directory where this profile's conversations are stored.</param>
    public AIService(Workspace workspace, string conversationsDir)
    {
        _workspace = workspace;
        _baseDir   = conversationsDir;
    }

    public async Task<IEnumerable<ConversationRecord>> LoadAllAsync()
    {
        var result = new List<ConversationRecord>();
        if (!Directory.Exists(_baseDir)) return result;

        foreach (var dir in Directory.EnumerateDirectories(_baseDir))
        {
            var file = Path.Combine(dir, "conversation.json");
            if (!File.Exists(file)) continue;
            try
            {
                await using var fs  = File.OpenRead(file);
                var rec = await JsonSerializer.DeserializeAsync<ConversationRecord>(fs, JsonOpts);
                if (rec is not null) result.Add(rec);
            }
            catch { /* skip corrupt files */ }
        }

        result.Sort((a, b) => b.StartedAt.CompareTo(a.StartedAt));
        _conversations = result;
        return result;
    }

    public async Task SaveAsync(ConversationRecord activeConversation)
    {
        try
        {
            var dir  = Path.Combine(_baseDir, activeConversation.Id);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "conversation.json");
            await using var fs = File.Open(file, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(fs, activeConversation, JsonOpts);
        }
        catch { /* never crash on persistence failures */ }
    }

    public async Task SaveConversationArtifactAsync(string conversationId, string name, string json)
    {
        try
        {
            var dir = Path.Combine(_baseDir, conversationId);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, ArtifactFileName(name));
            await File.WriteAllTextAsync(file, json);
        }
        catch { /* never crash on persistence failures */ }
    }

    public async Task<string?> LoadConversationArtifactAsync(string conversationId, string name)
    {
        try
        {
            var file = Path.Combine(_baseDir, conversationId, ArtifactFileName(name));
            return File.Exists(file) ? await File.ReadAllTextAsync(file) : null;
        }
        catch { return null; }
    }

    /// <summary>Maps an artifact name to a safe <c>{name}.json</c> file name.</summary>
    private static string ArtifactFileName(string name)
    {
        var safe = string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        if (safe.Length == 0) safe = "artifact";
        return safe + ".json";
    }

    // ── AI routing ────────────────────────────────────────────────────────

    public (IReadOnlyList<(IQueryHandler Handler, float Score)> Scored,
            IQueryHandler? ClearWinner,
            string EffectiveText)
        ScoreHandlers(string text, IPageViewModel? pageVm)
    {
        var allHandlers   = FeatureManager.Instance.GetQueryHandlers(_workspace).ToList();
        var effectiveText = text;

        // Symbol prefix → narrow to matching handlers and strip the prefix character
        var symbolMatches = allHandlers
            .Where(h => h.Symbol is { Length: 1 } s && text.StartsWith(s))
            .ToList();
        if (symbolMatches.Count > 0)
        {
            allHandlers   = symbolMatches;
            effectiveText = text[1..].TrimStart();
        }

        var scored = allHandlers
            .Select(h => (Handler: h, Score: h.CanProcess(effectiveText, pageVm)))
            .Where(x => x.Score > 0f)
            .OrderByDescending(x => x.Score)
            .ToList();

        IQueryHandler? clearWinner = null;
        if (scored.Count > 0)
        {
            var top    = scored[0].Score;
            var second = scored.Count > 1 ? scored[1].Score : 0f;
            if (top >= 0.8f && (scored.Count == 1 || top - second > 0.2f))
                clearWinner = scored[0].Handler;
        }

        return (scored, clearWinner, effectiveText);
    }

    public async Task<IQueryHandler?> DisambiguateToolSelection(
        IPageViewModel? page, string input,
        IReadOnlyList<(IQueryHandler Handler, float Score)> candidates)
    {
        var resolved = GetProvider(AiAbility.Disambiguation);
        if (resolved is null) return null;
        var (provider, model) = resolved.Value;

        var context  = page?.GetContext() ?? "No specific context.";
        var toolList = string.Join("\n", candidates.Select((c, i) =>
            $"{i + 1}. [confidence {c.Score:P0}] {c.Handler.Description}"));

        var systemPrompt = "You are a routing assistant. Reply with only a single digit — the number of the best tool, or 0 if none apply.";
        var userPrompt   =
            $"Context: {context}\n" +
            $"User typed: \"{input}\"\n\n" +
            $"Tools:\n0. None of these apply\n{toolList}\n\n" +
            "Which tool number should handle this request?";

        var response = await provider.CompleteAsync(
            [new(LlmRole.System, systemPrompt), new(LlmRole.User, userPrompt)],
            model);
        var raw      = response?.RawText?.Trim() ?? string.Empty;

        var digit = raw.FirstOrDefault(char.IsDigit);
        if (digit == default) return null;

        var idx = digit - '0';
        return (idx >= 1 && idx <= candidates.Count) ? candidates[idx - 1].Handler : null;
    }

    public async Task<int?> DisambiguateOptionAsync(
        string contextDescription,
        string question,
        IReadOnlyList<(string Label, string Detail)> options,
        CancellationToken ct = default)
    {
        if (options is null || options.Count == 0) return null;

        var resolved = GetProvider(AiAbility.Disambiguation);
        if (resolved is null) return null;
        var (provider, model) = resolved.Value;

        var optionList = string.Join("\n", options.Select((o, i) =>
            $"{i + 1}. {o.Label} — {o.Detail}"));

        var systemPrompt =
            "You are a disambiguation assistant. Reply with only a single number — the index of the best option, or 0 if none apply.";
        var userPrompt =
            $"Context:\n{contextDescription}\n\n" +
            $"Question: {question}\n\n" +
            $"Options:\n0. None of these apply\n{optionList}\n\n" +
            "Reply with only the number of your chosen option.";

        var response = await provider.CompleteAsync(
            [new(LlmRole.System, systemPrompt), new(LlmRole.User, userPrompt)],
            model);
        var raw = response?.RawText?.Trim() ?? string.Empty;

        // Allow multi-digit answers (e.g. "12") so we don't cap at 9 options.
        int i = 0;
        while (i < raw.Length && !char.IsDigit(raw[i])) i++;
        int start = i;
        while (i < raw.Length && char.IsDigit(raw[i])) i++;
        if (start == i) return null;
        if (!int.TryParse(raw.AsSpan(start, i - start), out var picked)) return null;

        if (picked <= 0 || picked > options.Count) return null;
        return picked - 1;
    }

    public async Task<string?> RunAnalysisAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var resolved = GetProvider(AiAbility.Analysis);
        if (resolved is null) return null;
        var (provider, model) = resolved.Value;

        var response = await provider.CompleteAsync(
            [new(LlmRole.System, systemPrompt), new(LlmRole.User, userPrompt)],
            model, null, ct);
        return response?.RawText;
    }

    // ── Client-side agent loop ───────────────────────────────────────────────

    /// <summary>Hard ceiling on automated tool turns, to bound a runaway loop.</summary>
    private const int MaxAgentSteps = 8;

    /// <summary>Above this many page tools, filter to the most relevant + get_client_commands.</summary>
    private const int MaxUnfilteredTools = 4;

    public async Task<AiResponse?> RunAgentAsync(
        IPageViewModel? page, string input, bool includeContext,
        IToolApprovalCoordinator approval, CancellationToken ct = default)
    {
        var resolved = GetProvider(AiAbility.Conversation);
        if (resolved is null) return null;
        var (provider, model) = resolved.Value;

        var context   = includeContext ? page?.GetContext() ?? "No specific context." : "No specific context.";
        var pageTools = includeContext ? page?.GetClientTools() ?? [] : [];

        // Built-in discovery tool, plus the resolvable catalogue (page tools + the built-in).
        var getCommands = BuildGetClientCommandsTool(pageTools);
        var fullCatalog = new List<IClientTool>(pageTools) { getCommands };

        // Expose at most MaxUnfilteredTools page tools (most relevant first) + get_client_commands.
        IReadOnlyList<IClientTool> exposed;
        try
        {
            if (pageTools.Count > MaxUnfilteredTools)
            {
                var ranked = await RankToolsAsync(input, pageTools, context, ct);
                exposed = [.. ranked, getCommands];
            }
            else
            {
                exposed = fullCatalog;
            }
        }
        catch (OperationCanceledException) { return null; }

        var messages = new List<LlmMessage>
        {
            new(LlmRole.System, BuildSystemPrompt(context, exposed)),
            new(LlmRole.User,   input)
        };

        var artifacts = new List<string>();   // files the tools read/created, for conversation context

        try
        {
            var planMode = false;
            for (var step = 0; step < MaxAgentSteps; step++)
            {
                ct.ThrowIfCancellationRequested();

                var resp = await provider.CompleteAsync(messages, model, null, ct);
                var raw  = resp?.RawText?.Trim() ?? string.Empty;
                var turn = ClientBlockParser.Parse(raw);

                // 1. A plan was proposed and not yet approved — approve once, then run unattended.
                if (turn.Plan is not null && !planMode)
                {
                    messages.Add(new(LlmRole.Assistant, raw));
                    if (!await approval.RequestPlanApprovalAsync(turn.Plan, ct))
                    {
                        messages.Add(new(LlmRole.User,
                            "TOOL RESULTS\nThe user declined the plan. Stop, or suggest a different approach."));
                        continue;
                    }
                    planMode = true;
                    messages.Add(new(LlmRole.User,
                        "TOOL RESULTS\nThe user approved the plan. Begin executing the steps by emitting client_tool blocks."));
                    continue;
                }

                // 2. Pure prefill.
                if (turn.Prefill is not null && turn.ToolCalls.Count == 0)
                    return AiResponse.AsPrefill(turn.Prefill);

                // 3. No tool calls.
                if (turn.ToolCalls.Count == 0)
                {
                    if (turn.ParseErrors.Count > 0)
                    {
                        // The model tried to act but emitted something malformed — let it self-correct.
                        messages.Add(new(LlmRole.Assistant, raw));
                        messages.Add(new(LlmRole.User,
                            "TOOL RESULTS\n" + string.Join('\n', turn.ParseErrors) +
                            "\nFix the block and try again, or just answer."));
                        continue;
                    }
                    var final = turn.ExplanationMarkdown.Length > 0 ? turn.ExplanationMarkdown : raw;
                    approval.ShowFinal(final);
                    return AiResponse.AsMessage(final, artifacts);
                }

                // 4. A batch of tool calls.
                messages.Add(new(LlmRole.Assistant, raw));

                var calls = turn.ToolCalls
                    .Select(c => (Call: c, Tool: FindTool(fullCatalog, c.Tool)))
                    .ToList();

                var needApproval = !planMode &&
                    calls.Any(rc => rc.Tool is { Safety: ToolSafety.RequiresApproval });

                if (needApproval &&
                    !await approval.RequestToolBatchApprovalAsync(turn.ExplanationMarkdown, turn.ToolCalls, ct))
                {
                    var denied = string.Join(", ", turn.ToolCalls.Select(c => c.Tool));
                    messages.Add(new(LlmRole.User,
                        $"TOOL RESULTS\nThe user denied: {denied}. Do not retry — ask what they would prefer, or finish."));
                    continue;
                }

                var results = await ExecuteBatchAsync(calls, approval, ct);
                foreach (var (_, r) in results)
                    if (r.Attachments is { } att)
                        foreach (var p in att)
                            if (!artifacts.Contains(p)) artifacts.Add(p);

                messages.Add(new(LlmRole.User, FormatToolResults(results)));
            }
        }
        catch (OperationCanceledException) { return null; }

        const string capped = "I've reached the maximum number of automated steps, so I've stopped here. " +
                              "_(stopped: max steps reached)_";
        approval.ShowFinal(capped);
        return AiResponse.AsMessage(capped, artifacts);
    }

    private static IClientTool? FindTool(IReadOnlyList<IClientTool> catalog, string name)
        => catalog.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    private static async Task<IReadOnlyList<(string Tool, ToolResult Result)>> ExecuteBatchAsync(
        IReadOnlyList<(ToolCall Call, IClientTool? Tool)> calls,
        IToolApprovalCoordinator approval, CancellationToken ct)
    {
        var results     = new (string Tool, ToolResult Result)[calls.Count];
        var parallelIdx = new List<int>();

        for (var i = 0; i < calls.Count; i++)
        {
            var (call, tool) = calls[i];
            if (tool is null)
            {
                results[i] = (call.Tool, ToolResult.Error(
                    $"Unknown tool '{call.Tool}'",
                    $"There is no client tool named '{call.Tool}'. Call get_client_commands to see what's available."));
                continue;
            }
            if (tool.Parallelizable) { parallelIdx.Add(i); continue; }

            approval.ReportProgress($"Running {tool.Name}…");
            results[i] = (call.Tool, await InvokeSafelyAsync(tool, call, ct));
        }

        if (parallelIdx.Count > 0)
        {
            approval.ReportProgress(parallelIdx.Count == 1
                ? $"Running {calls[parallelIdx[0]].Tool!.Name}…"
                : $"Running {parallelIdx.Count} tools…");

            var tasks = parallelIdx.Select(i => InvokeSafelyAsync(calls[i].Tool!, calls[i].Call, ct)).ToArray();
            var done  = await Task.WhenAll(tasks);
            for (var p = 0; p < parallelIdx.Count; p++)
                results[parallelIdx[p]] = (calls[parallelIdx[p]].Call.Tool, done[p]);
        }

        return results;
    }

    private static async Task<ToolResult> InvokeSafelyAsync(IClientTool tool, ToolCall call, CancellationToken ct)
    {
        try { return await tool.InvokeAsync(call.Arguments, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ToolResult.Error($"{tool.Name} failed: {ex.Message}"); }
    }

    private static string FormatToolResults(IReadOnlyList<(string Tool, ToolResult Result)> results)
    {
        var sb = new StringBuilder("TOOL RESULTS\n");
        foreach (var (tool, r) in results)
        {
            sb.Append("## ").Append(tool);
            if (r.IsError) sb.Append(" (error)");
            sb.Append('\n').Append(r.ModelText).Append("\n\n");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Asks the Disambiguation-ability model to pick the most relevant page tools for the request.
    /// Falls back to the first <see cref="MaxUnfilteredTools"/> when no ranker is configured.
    /// </summary>
    private async Task<IReadOnlyList<IClientTool>> RankToolsAsync(
        string input, IReadOnlyList<IClientTool> tools, string context, CancellationToken ct)
    {
        var resolved = GetProvider(AiAbility.Disambiguation);
        if (resolved is null) return [.. tools.Take(MaxUnfilteredTools)];
        var (provider, model) = resolved.Value;

        var list   = string.Join('\n', tools.Select((t, i) => $"{i + 1}. {t.Name} — {t.Description}"));
        var system = "You select the tools most relevant to a user request. Reply with the numbers of up to " +
                     $"{MaxUnfilteredTools} relevant tools, comma-separated, most relevant first. Reply 0 if none seem relevant.";
        var user   = $"Context: {context}\nUser request: \"{input}\"\n\nTools:\n{list}\n\nMost relevant tool numbers:";

        string raw;
        try
        {
            var resp = await provider.CompleteAsync(
                [new(LlmRole.System, system), new(LlmRole.User, user)], model, null, ct);
            raw = resp?.RawText ?? string.Empty;
        }
        catch (OperationCanceledException) { throw; }
        catch { return [.. tools.Take(MaxUnfilteredTools)]; }

        var picked = new List<IClientTool>();
        foreach (var n in ExtractNumbers(raw))
        {
            if (n < 1 || n > tools.Count) continue;
            var tool = tools[n - 1];
            if (!picked.Contains(tool)) picked.Add(tool);
            if (picked.Count >= MaxUnfilteredTools) break;
        }
        return picked.Count > 0 ? picked : [.. tools.Take(MaxUnfilteredTools)];
    }

    private static IEnumerable<int> ExtractNumbers(string s)
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

    private static IClientTool BuildGetClientCommandsTool(IReadOnlyList<IClientTool> pageTools)
        => new DelegateClientTool(
            "get_client_commands",
            "List every client tool available here, with descriptions and parameters.",
            [],
            ToolSafety.ReadOnly,
            (_, _) => Task.FromResult(ToolResult.Ok(
                $"{pageTools.Count} tool(s) available", DescribeCatalog(pageTools))),
            parallelizable: true);

    private string BuildSystemPrompt(string context, IReadOnlyList<IClientTool> tools)
    {
        var persona = _workspace.Profile.Persona;
        var aiName  = string.IsNullOrWhiteSpace(persona?.Name) ? "Aria" : persona!.Name!.Trim();

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(persona?.SystemPrompt))
            sb.Append(persona!.SystemPrompt!.Trim()).Append("\n\n");

        sb.Append($"You are {aiName}, an AI assistant embedded in Nexaflow, a desktop application. ")
          .Append("You are talking to the user through an advanced client that natively renders Markdown — ")
          .Append("including GitHub-flavored tables, LaTeX math (inline $…$ and block $$…$$), and Mermaid and ")
          .Append("Nomnoml diagrams in fenced code blocks. Use them freely to make answers clear.\n\n")
          .Append($"The user is currently looking at: {context}.\n\n");

        sb.Append("# Client-side tools\n")
          .Append("You can act inside the application by calling CLIENT-SIDE tools. These run locally in the app ")
          .Append("on the user's machine — they are NOT server-side or MCP tools. Invoke one by emitting a fenced ")
          .Append("code block tagged `client_tool` whose body is a single JSON object:\n\n")
          .Append("```client_tool\n{\"tool\": \"tool_name\", \"arguments\": {\"key\": \"value\"}}\n```\n\n")
          .Append("How the loop works:\n")
          .Append("- Briefly explain what you're about to do (one or two sentences), then emit the block(s), then STOP. Write nothing after the block.\n")
          .Append("- The harness runs the tools and replies with a message beginning \"TOOL RESULTS\" containing each tool's output. Continue from there.\n")
          .Append("- Text you write OUTSIDE fenced blocks is shown to the user as your message.\n")
          .Append("- To run several INDEPENDENT tools at once, emit multiple `client_tool` blocks in the same reply — the harness runs them together as one batch without asking you in between. Only batch tools that don't depend on each other.\n")
          .Append("- When you have everything you need, reply normally with no tool block — that is your final answer.\n")
          .Append("- Read-only tools run immediately; tools that change the machine ask the user to approve first.\n")
          .Append("- If you're unsure what you can do here, call `get_client_commands` for the full list.\n\n")
          .Append("To pre-fill the user's input box instead of acting (e.g. to suggest a command for them to run), emit:\n\n")
          .Append("```client_prefill\n{\"text\": \"suggested input\"}\n```\n\n");

        sb.Append("# Plans (optional)\n")
          .Append("For multi-step work you MAY first propose a plan the user approves once, instead of asking per step. Emit one:\n\n")
          .Append("```client_plan\n{\"title\": \"…\", \"mermaid\": \"flowchart TD; A[…]-->B[…]\", ")
          .Append("\"steps\": [{\"title\":\"…\",\"tool\":\"tool_name\"},{\"title\":\"Decide …\",\"decision\":true}]}\n```\n\n")
          .Append("After approval, run the plan's tool steps without asking again until a step marked \"decision\": true (reassess there) or the work is done.\n\n");

        sb.Append("# Available tools\n").Append(DescribeCatalog(tools));
        return sb.ToString();
    }

    private static string DescribeCatalog(IEnumerable<IClientTool> tools)
    {
        var sb = new StringBuilder();
        foreach (var t in tools)
            sb.Append(DescribeTool(t)).Append('\n');
        var s = sb.ToString().TrimEnd();
        return s.Length == 0 ? "(no tools available here)" : s;
    }

    private static string DescribeTool(IClientTool t)
    {
        var sb = new StringBuilder();
        sb.Append("- ").Append(t.Name);
        if (t.Safety == ToolSafety.RequiresApproval) sb.Append("  [needs approval]");
        sb.Append(": ").Append(t.Description);
        foreach (var p in t.Parameters)
        {
            sb.Append("\n    • ").Append(p.Name).Append(" (").Append(p.Type);
            if (!p.Required) sb.Append(", optional");
            sb.Append("): ").Append(p.Description);
        }
        return sb.ToString();
    }
}
