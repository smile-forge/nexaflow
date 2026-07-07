using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Common.Viewlets;

/// <summary>
/// Opt-in capability for a viewlet <em>view</em> (the element returned by <see cref="IFolderViewlet.CreateView"/>)
/// that runs background work or holds handles touching its folder — e.g. the .NET viewlet's NuGet-update scan,
/// which launches <c>dotnet</c> with the folder as its working directory and so locks it on Windows.
/// <para>
/// The host calls <see cref="QuiesceAsync"/> before a mutation is about to change or delete the folder (see
/// <see cref="IViewletController.QuiesceFolderAsync"/>). The implementation must stop that work and release
/// every handle — <b>killing and awaiting child processes</b>, not merely cancelling them — and must not
/// return until it has, so the caller can safely rename/delete the folder afterwards. Best-effort: it should
/// swallow its own failures rather than throw.
/// </para>
/// </summary>
public interface IViewletQuiescible
{
    Task QuiesceAsync(CancellationToken ct = default);
}
