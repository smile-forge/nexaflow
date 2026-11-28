using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nexaflow.IO.Common.Shell;

namespace Nexaflow.Visuals.Common.Shell;

/// <summary>
/// The WPF picture of an icon the shell drew. Made once per <see cref="ShellIconPixels"/> and kept beside it for as long as the
/// shell's caches keep the pixels, so a row asking again is handed the same frozen picture rather than a new copy.
/// </summary>
public static class ShellIconBitmaps
{
    private static readonly ConditionalWeakTable<ShellIconPixels, BitmapSource> Made = new();

    /// <summary>The frozen picture of <paramref name="icon"/>, usable from any thread.</summary>
    public static ImageSource Of(ShellIconPixels icon) => Made.GetValue(icon, static pixels =>
    {
        var bitmap = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Pbgra32, null, pixels.Pixels, pixels.Stride);
        bitmap.Freeze();
        return bitmap;
    });
}

/// <summary>A WPF thread as the shell's answers are handed to it: behind its input and layout, one instance per dispatcher.</summary>
public sealed class DispatcherAnswerThread : IAnswerThread
{
    private static readonly ConditionalWeakTable<Dispatcher, DispatcherAnswerThread> Threads = new();

    private readonly Dispatcher _dispatcher;

    private DispatcherAnswerThread(Dispatcher dispatcher) => _dispatcher = dispatcher;

    /// <summary>The one answer thread of <paramref name="dispatcher"/>.</summary>
    public static DispatcherAnswerThread For(Dispatcher dispatcher) => Threads.GetValue(dispatcher, static d => new DispatcherAnswerThread(d));

    public void Post(Action action) => _dispatcher.InvokeAsync(action, DispatcherPriority.Background);
}
