using System.Threading.Tasks;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Services;

/// <summary>
/// AI-input query handler that opens a page or ribbon shortcut by name — the standard symbol dot + ghost
/// completion plumbing, no popup. Its <see cref="Symbol"/> is "/": typing <c>/name</c> routes here with
/// <c>prefixed: true</c> (prefix-matched and completed as you type); a bare exact page name reaches it with
/// <c>prefixed: false</c> and opens on Enter without hijacking a normal AI question. The two modes differ
/// only in how <see cref="QuickOpen"/> matches, which is exactly what the <c>prefixed</c> flag selects.
/// </summary>
public sealed class PageQuickOpenHandler(IShellServices shell) : IQueryHandler
{
    public string Description =>
        "Opens a page (e.g. Services, Fonts, System Info) or ribbon shortcut by name. " +
        "Use /name — completed as you type — or a page's exact name.";

    public string? Symbol => "/";

    public float CanProcess(string input, bool prefixed, IPageViewModel? pageVm = null)
        => QuickOpen.Resolve(input, prefixed, shell.GetQuickOpenTargets()) is not null ? 1f : 0f;

    public Task<string?> ProcessAsync(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        QuickOpen.Resolve(input, prefixed, shell.GetQuickOpenTargets())?.Target.Open();
        return Task.FromResult<string?>(null);   // opened a tab — nothing to render in chat
    }

    public Task<string?> CompleteAsync(string input, bool prefixed, IPageViewModel? pageVm = null)
        => Task.FromResult(QuickOpen.Resolve(input, prefixed, shell.GetQuickOpenTargets())?.Completion);
}
