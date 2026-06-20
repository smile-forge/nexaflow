using System;

namespace Nexaflow.Features.Common;

/// <summary>
/// Handle to a file watch registered through <see cref="IShellServices.WatchFile"/>. Dispose it to stop
/// watching (the shell drops the underlying watcher once its last subscriber goes). While
/// <see cref="Enabled"/> is <c>false</c> the shell withholds change callbacks and coalesces any that
/// arrive into a single one delivered when the watch is re-enabled — so a view can disable its watch
/// while it is unloaded and catch up in one callback when it comes back.
/// </summary>
public interface IFileWatch : IDisposable
{
    /// <summary>
    /// When <c>false</c>, change callbacks are held (and coalesced). Setting it back to <c>true</c>
    /// flushes a single callback if any change arrived while disabled. Defaults to <c>true</c>.
    /// </summary>
    bool Enabled { get; set; }
}
