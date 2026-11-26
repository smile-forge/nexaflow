namespace Nexaflow.IO.Common.Shell;

/// <summary>
/// An icon the shell drew: 32-bit premultiplied BGRA pixels, top row first, <see cref="Width"/> × <see cref="Height"/>. It names
/// no UI toolkit, so any toolkit makes its own picture of it. The caches hand out one instance per icon drawn, so a toolkit
/// can convert each once and keep its picture beside it.
/// </summary>
public sealed class ShellIconPixels(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    /// <summary>The pixels, <see cref="Stride"/> bytes to a row.</summary>
    public byte[] Pixels { get; } = pixels;

    /// <summary>The bytes in one row.</summary>
    public int Stride => Width * 4;
}

/// <summary>
/// The UI thread a shell answer is handed to. A toolkit gives each of its threads one instance, so the answers waiting for a
/// thread are handed over together; each is run behind that thread's input and layout.
/// </summary>
public interface IAnswerThread
{
    /// <summary>Runs <paramref name="action"/> on the thread, behind its input and layout. Callable from any thread.</summary>
    void Post(Action action);
}
