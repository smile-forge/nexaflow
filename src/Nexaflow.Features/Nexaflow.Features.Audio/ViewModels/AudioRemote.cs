namespace Nexaflow.Features.Audio.ViewModels;

/// <summary>
/// The audio page's remote in the shell chrome while it plays in the background: pause and skip on the player it wraps,
/// so the remote drives the same commands as the page and stays in step with it.
/// </summary>
public sealed record AudioRemote(AudioViewModel Player);
