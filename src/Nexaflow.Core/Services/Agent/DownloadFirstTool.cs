using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Agent;

/// <summary>
/// A tool whose abilities wait on the download of a model Nexaflow runs itself: offered as it is, and called, it first
/// offers each download (<see cref="IAIService.MakeReadyAsync"/>) — running only where every ability serves by then, and
/// otherwise telling the model the user has been asked, so it says so rather than trying again.
/// </summary>
internal sealed class DownloadFirstTool(IClientTool inner, IReadOnlyList<AiAbility> waiting,
                                        Func<AiAbility, CancellationToken, Task<bool>> makeReady) : IClientTool
{
    public string Name => inner.Name;
    public string Description => inner.Description;
    public IReadOnlyList<ClientToolParameter> Parameters => inner.Parameters;
    public ToolSafety Safety => inner.Safety;
    public bool Parallelizable => inner.Parallelizable;
    public bool ExemptFromRepeatGuard => inner.ExemptFromRepeatGuard;
    public IReadOnlyList<AiAbility> Needs => inner.Needs;
    public ToolCallAnnouncement? Announce(JsonObject arguments) => inner.Announce(arguments);
    public ToolSafety SafetyOf(JsonObject arguments) => inner.SafetyOf(arguments);
    public bool RunsInBackground(JsonObject arguments) => inner.RunsInBackground(arguments);

    /// <summary>What the model is told of a call that waits on a download: so it tells the user, rather than calling again.</summary>
    private const string ToldTheModel =
        "The model this tool needs is not downloaded yet. The user has been offered its download; tell them it can be used " +
        "once the download finishes, and do not call the tool again now.";

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        foreach (var ability in waiting)
            if (!await makeReady(ability, ct))
                return ToolResult.Error(Str.Get("Shell.Ai.ToolAwaitsDownload"), ToldTheModel);
        return await inner.InvokeAsync(arguments, ct);
    }
}
