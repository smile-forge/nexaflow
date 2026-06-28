using System.IO;
using LibVLCSharp;

namespace Nexaflow.Features.Video.Interop;

/// <summary>
/// Loads the libvlc native libraries exactly once per process. <see cref="LibVLCSharp.Core.Initialize()"/>
/// must run before any <see cref="LibVLC"/> is constructed. The <c>VideoLAN.LibVLC.Windows</c> package
/// copies the natives to <c>libvlc\win-x64</c> beside the app; we point Core there explicitly (and fall
/// back to its default probing if the folder isn't where we expect).
/// </summary>
internal static class VlcInit
{
    private static readonly object _gate = new();
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_gate)
        {
            if (_initialized) return;

            var nativeDir = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64");
            if (Directory.Exists(nativeDir))
                Core.Initialize(nativeDir);
            else
                Core.Initialize();

            _initialized = true;
        }
    }
}
