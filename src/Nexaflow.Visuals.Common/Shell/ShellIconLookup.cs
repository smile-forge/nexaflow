using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Shell;

/// <summary>
/// What an icon request gives back at once: the best icon already in hand, or null when there is none yet; and, while a
/// better one is still to come, the ticket that stops waiting for it.
/// </summary>
public readonly record struct ShellIconLookup(ImageSource? Icon, IDisposable? Pending);
