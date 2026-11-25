using Nexaflow.Providers.Common;

namespace Nexaflow.Session.Services.Tools;

/// <summary>
/// Fits a picture to the exact frame a video model starts its clip from — cut to the frame's proportions about its centre, then
/// scaled — which the UI toolkit's imaging does, so the app supplies it.
/// </summary>
public interface IPictureFitting
{
    /// <summary><paramref name="picture"/> cut to the frame's proportions about its centre, then scaled to <paramref name="width"/>
    /// × <paramref name="height"/>, as a PNG.</summary>
    MediaBytes FitToFrame(MediaBytes picture, int width, int height);
}

