using System.Buffers;

namespace Nexaflow.Tests.Features.Images;

/// <summary>Bytes a fake tool gives back as its result's pixels, as the app's tools give theirs: owned, and let go on dispose.</summary>
internal sealed class ArrayMemoryOwner(byte[] bytes) : IMemoryOwner<byte>
{
    public Memory<byte> Memory => bytes;

    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}
