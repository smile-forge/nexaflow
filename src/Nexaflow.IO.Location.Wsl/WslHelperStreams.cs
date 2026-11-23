using Nexaflow.IO.Location.Wsl.Protocol;

namespace Nexaflow.IO.Location.Wsl;

/// <summary>
/// A distro file read through the helper (<see cref="WslOp.Read"/>) rather than WSL's file server, which cannot name a file
/// whose name is no UTF-8. Seekable: each read asks for what it is given room for, up to <see cref="WslWire.MaxChunk"/>.
/// </summary>
internal sealed class WslHelperReadStream(WslSession session, string path, long length) : Stream
{
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;

    public override long Position
    {
        get => _position;
        set => _position = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty) return 0;
        var read = await session.ReadRangeAsync(path, _position, Math.Min(buffer.Length, WslWire.MaxChunk), cancellationToken)
                                .ConfigureAwait(false);
        read.CopyTo(buffer);
        _position += read.Length;
        return read.Length;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override long Seek(long offset, SeekOrigin origin)
        => Position = origin switch
        {
            SeekOrigin.Begin   => offset,
            SeekOrigin.Current => _position + offset,
            _                  => length + offset,
        };

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// A distro file written through the helper (<see cref="WslOp.Write"/>) rather than WSL's file server, which cannot name a
/// file whose name is no UTF-8. The file is already there, made by the write that opened it; what is written here goes in
/// order, in chunks of up to <see cref="WslWire.MaxChunk"/>, each where the last ended — or at the file's end, appending.
/// </summary>
internal sealed class WslHelperWriteStream(WslSession session, string path, bool append) : Stream
{
    private readonly byte[] _pending = new byte[WslWire.MaxChunk];
    private int _held;
    private long _written;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _written + _held;

    public override long Position
    {
        get => Length;
        set => throw new NotSupportedException();
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (!buffer.IsEmpty)
        {
            int taken = Math.Min(buffer.Length, _pending.Length - _held);
            buffer[..taken].CopyTo(_pending.AsMemory(_held));
            _held += taken;
            buffer = buffer[taken..];
            if (_held == _pending.Length) await SendAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Write(byte[] buffer, int offset, int count)
        => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override Task FlushAsync(CancellationToken cancellationToken) => SendAsync(cancellationToken).AsTask();

    public override void Flush() => SendAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();

    private async ValueTask SendAsync(CancellationToken ct)
    {
        if (_held == 0) return;
        await session.WriteRangeAsync(path, append ? WireWrite.Append : WireWrite.At, _written, _pending.AsMemory(0, _held), ct)
                     .ConfigureAwait(false);
        _written += _held;
        _held = 0;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
