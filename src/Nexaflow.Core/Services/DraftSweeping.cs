using System.IO;
using Nexaflow.IO.Common;

namespace Nexaflow.Core.Services;

/// <summary>
/// The app clearing its own drafts (<see cref="DraftLocation.Sweep"/>), as Windows' clean-up of the temp folder once did but never
/// while one is in use: a little after it starts, then once every <see cref="Every"/> for as long as it runs — a session left open for
/// weeks, or the windowless prestart, clears as a day of starts would. Each time, what the tabs name is asked where the tabs
/// are, and the drafts are swept off it; a sweep still going when the next is due lets that one pass.
/// </summary>
/// <param name="named">Every path a tab names, open or to be opened again — asked on the thread the tabs are on.</param>
/// <param name="sweep">The sweep itself, handed what the tabs name.</param>
internal sealed class DraftSweeping(Func<Task<IReadOnlyList<string>>> named, Action<IReadOnlyList<string>> sweep, TimeProvider time) : IDisposable
{
    /// <summary>How often drafts are swept while the app runs.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromDays(1);

    /// <summary>
    /// How long after the app starts the first sweep waits: nothing about clearing drafts is urgent, and the disk is the
    /// starting app's while it loads its features and opens its tabs.
    /// </summary>
    public static readonly TimeSpan FirstAfter = TimeSpan.FromMinutes(2);

    private ITimer? _timer;
    private int _sweeping;

    /// <summary>Sweeps once <see cref="FirstAfter"/> has passed, then every <see cref="Every"/>.</summary>
    public void Start() => _timer ??= time.CreateTimer(_ => _ = SweepAsync(), null, FirstAfter, Every);

    /// <summary>One sweep: what the tabs name, then the drafts swept off the UI thread.</summary>
    internal async Task SweepAsync()
    {
        if (Interlocked.Exchange(ref _sweeping, 1) == 1) return;
        try
        {
            var paths = await named();
            await Task.Run(() => sweep(paths));
        }
        catch (IOException) { /* the drafts folder moved under it: the next sweep tries again */ }
        catch (UnauthorizedAccessException) { }
        finally { Volatile.Write(ref _sweeping, 0); }
    }

    public void Dispose() => _timer?.Dispose();
}
