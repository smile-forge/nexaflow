using System.Threading.Tasks;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Services;

/// <summary>
/// AI-input query handler that opens a page or ribbon shortcut by name — the standard dot + ghost-completion
/// plumbing, no popup. Type <c>/name</c> (prefix-matched and completed) or a page's exact name; Enter opens it.
/// <para>
/// It reads the <b>raw</b> input rather than routing on a <see cref="Symbol"/>, because the leading "/" must
/// be visible to tell <c>/serv</c> (prefix quick-open) from bare <c>serv</c> (a normal question) — so it sets
/// <see cref="Symbol"/> null but <see cref="AlwaysConsidered"/> true (to still be scored when the regex
/// handler's "/" would narrow it out) and <see cref="DisplaySymbol"/> "/" (to still show the dot glyph).
/// </para>
/// </summary>
public sealed class PageQuickOpenHandler(IShellServices shell) : IQueryHandler
{
    public string Description =>
        "Opens a page (e.g. Services, Fonts, System Info) or ribbon shortcut by name. " +
        "Use /name — completed as you type — or a page's exact name.";

    public string? Symbol        => null;   // reads the raw input, including the leading "/", itself
    public string? DisplaySymbol => "/";    // …but still surfaces the "/" status-dot glyph
    public bool    AlwaysConsidered => true; // …and isn't monopolised by the regex handler's "/"

    public float CanProcess(string input, IPageViewModel? pageVm = null)
        => QuickOpen.Resolve(input, shell.GetQuickOpenTargets()) is not null ? 1f : 0f;

    public Task<string?> ProcessAsync(string input, IPageViewModel? pageVm = null)
    {
        QuickOpen.Resolve(input, shell.GetQuickOpenTargets())?.Target.Open();
        return Task.FromResult<string?>(null);   // opened a tab — nothing to render in chat
    }

    public Task<string?> CompleteAsync(string input, IPageViewModel? pageVm = null)
        => Task.FromResult(QuickOpen.Resolve(input, shell.GetQuickOpenTargets())?.Completion);
}
