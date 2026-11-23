namespace Nexaflow.Features.Common;

/// <summary>When a <see cref="IUiTimer"/>'s tick runs, against the rest of the UI thread's work.</summary>
public enum UiTimerPriority
{
    /// <summary>After input and layout, with other work that can wait: a poll, a debounce, a slideshow. The usual choice.</summary>
    Background,

    /// <summary>With rendering, ahead of background work: a tick that draws every frame, such as a playing spectrum.</summary>
    Render,
}
