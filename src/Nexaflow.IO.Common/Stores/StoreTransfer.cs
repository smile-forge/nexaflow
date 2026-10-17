using System.Diagnostics;
using System.Security.Cryptography;

namespace Nexaflow.IO.Common.Stores;

/// <summary>How a copy between stores went: what arrived and was read back intact, and what did not.</summary>
public sealed record StoreCopyResult(int Files, long Bytes, IReadOnlyList<VerifiedFile> Verified, IReadOnlyList<TransferItemFailure> Failures, bool Completed);

/// <summary>
/// Copies files and folders from one store into a folder of another, every file read back from where it landed and
/// checked against the SHA-256 of what was read from its source: a copy that reads back differently is removed and
/// reported, never left to be found out later. There is no unchecked copy.
/// </summary>
/// <remarks>
/// Between two stores on this PC's file system it is <see cref="FileTransferEngine"/>'s copy, which reads back past the
/// cache. Otherwise — a phone over MTP, either way — each file is streamed, hashed as it is read from the source, and
/// read back from the destination through the destination store. Anything already there with the same name is kept and
/// the copy named alongside it ("photo (2).jpg").
/// </remarks>
public static class StoreTransfer
{
    public static async Task<StoreCopyResult> CopyAsync(IFileStore from, IReadOnlyList<StoreEntry> items, IFileStore to, string folderId,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (to.LocalPathOf(folderId) is { } destination && items.All(i => from.LocalPathOf(i.Id) is not null))
            return await OnDiskAsync([.. items.Select(i => from.LocalPathOf(i.Id)!)], destination, progress, ct);

        var run = new StreamedCopy(from, to, progress, ct);
        await run.MeasureAsync(items);
        foreach (var item in items) await run.CopyAsync(item, folderId);
        return run.Result();
    }

    static async Task<StoreCopyResult> OnDiskAsync(IReadOnlyList<string> sources, string destination, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var scan = await FileTransferEngine.ScanAsync(sources, ct);
        var items = sources.Select(s => new TransferItem(s, Path.Combine(destination, Path.GetFileName(s.Length > 3 ? s.TrimEnd('\\') : s)))).ToList();
        var result = await FileTransferEngine.RunAsync(new FileTransferRequest(TransferKind.Copy, items, ConflictPolicy.AutoRename, Verify: true),
            scan, progress ?? new Progress<TransferProgress>(), null, ct);
        return new StoreCopyResult(result.ItemsTransferred, result.BytesTransferred, result.Verified, result.Failures, result.Completed);
    }

    /// <summary>One streamed copy run: what it measured, what it has done, and what it found.</summary>
    sealed class StreamedCopy(IFileStore from, IFileStore to, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        const int BufferSize = 1 << 20;

        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly List<VerifiedFile> _verified = [];
        readonly List<TransferItemFailure> _failures = [];
        readonly Dictionary<string, HashSet<string>> _namesIn = new(StringComparer.Ordinal);
        long _bytesTotal, _bytesDone;
        int _filesTotal, _filesDone;

        public async Task MeasureAsync(IEnumerable<StoreEntry> items)
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                if (!item.IsFolder) { _bytesTotal += item.Size; _filesTotal++; continue; }
                try { await MeasureAsync(await from.ListAsync(item.Id, ct)); }
                catch (Exception e) when (e is not OperationCanceledException) { _failures.Add(new(item.Name, "read", 0, e.Message)); }
            }
        }

        public async Task CopyAsync(StoreEntry item, string folderId)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string name = await FreeNameAsync(folderId, item.Name);
                if (item.IsFolder)
                {
                    var made = await to.CreateFolderAsync(folderId, name, ct);
                    _namesIn[made.Id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var child in await from.ListAsync(item.Id, ct)) await CopyAsync(child, made.Id);
                }
                else await CopyFileAsync(item, folderId, name);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _failures.Add(new(item.Name, item.IsFolder ? "copy folder" : "copy", e.HResult, e.Message));
            }
        }

        async Task CopyFileAsync(StoreEntry file, string folderId, string name)
        {
            byte[] expected;
            StoreEntry written;
            await using (var source = await from.OpenReadAsync(file.Id, ct))
            using (var hashing = new HashingStream(source, read => Report(file.Name, read, verifying: false)))
            {
                try
                {
                    written = await to.WriteAsync(folderId, name, file.Size, file.Modified, hashing, ct);
                }
                catch
                {
                    // Stopped or failed part-way: what landed is not the file, so it does not stay.
                    await RemovePartialAsync(folderId, name);
                    throw;
                }
                expected = hashing.Finish();
            }

            byte[] actual;
            await using (var back = await to.OpenReadAsync(written.Id, ct))
            using (var hashing = new HashingStream(back, _ => Report(file.Name, 0, verifying: true)))
            {
                await hashing.CopyToAsync(Stream.Null, BufferSize, ct);
                actual = hashing.Finish();
            }

            if (!actual.AsSpan().SequenceEqual(expected))
            {
                await to.DeleteAsync(written, ct);
                _failures.Add(new(file.Name, "verify", 0, "It read back from where it landed differently from the original, so the copy was removed."));
                return;
            }
            _verified.Add(new VerifiedFile(written.Name, file.Size, Convert.ToHexString(expected)));
            _filesDone++;
        }

        async Task RemovePartialAsync(string folderId, string name)
        {
            try
            {
                if ((await to.ListAsync(folderId, CancellationToken.None)).FirstOrDefault(e => e.Name == name) is { } partial)
                    await to.DeleteAsync(partial, CancellationToken.None);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _failures.Add(new(name, "remove", e.HResult, $"A part-copied file could not be removed: {e.Message}"));
            }
        }

        /// <summary>The name a copy takes in a folder: its own, or "name (2).ext" when that is taken.</summary>
        async Task<string> FreeNameAsync(string folderId, string name)
        {
            if (!_namesIn.TryGetValue(folderId, out var taken))
                _namesIn[folderId] = taken = new HashSet<string>((await to.ListAsync(folderId, ct)).Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
            string free = name, stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
            for (int n = 2; taken.Contains(free); n++) free = $"{stem} ({n}){extension}";
            taken.Add(free);
            return free;
        }

        void Report(string current, int read, bool verifying)
        {
            _bytesDone += read;
            double seconds = _clock.Elapsed.TotalSeconds;
            progress?.Report(new TransferProgress(TransferPhase.Running, _bytesDone, _bytesTotal, _filesDone, _filesTotal, current,
                seconds > 0 ? (long)(_bytesDone / seconds) : 0, null, null, verifying));
        }

        public StoreCopyResult Result() => new(_filesDone, _verified.Sum(v => v.Bytes), _verified, _failures, Completed: true);
    }

    /// <summary>Reads through to another stream, hashing every byte that passes and saying how many did.</summary>
    sealed class HashingStream(Stream inner, Action<int> passed) : Stream
    {
        readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public byte[] Finish() => _hash.GetHashAndReset();

        public override int Read(byte[] buffer, int offset, int count) => Passed(buffer.AsSpan(offset, inner.Read(buffer, offset, count)));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await inner.ReadAsync(buffer, cancellationToken);
            return Passed(buffer.Span[..read]);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        int Passed(ReadOnlySpan<byte> bytes)
        {
            _hash.AppendData(bytes);
            if (bytes.Length > 0) passed(bytes.Length);
            return bytes.Length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _hash.Dispose();
            base.Dispose(disposing);
        }
    }
}
