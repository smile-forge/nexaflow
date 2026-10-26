using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.IO.Common;

/// <summary>
/// The one place bulk copy, move and permanent delete happen. Four properties are the reason it
/// exists rather than a recursive <c>File.Copy</c> loop:
/// <list type="bullet">
/// <item><description><b>It never throws.</b> Every per-item fault — including one raised while
/// <em>enumerating</em> a directory, which is the case a naive recursion leaves outside its
/// <c>try</c> — becomes a <see cref="TransferItemFailure"/> and the walk continues with the next
/// sibling. A cancellation ends the run with <see cref="TransferResult.Completed"/> false rather
/// than an exception, so a caller always gets the tally of what did land.</description></item>
/// <item><description><b>A move deletes each source only after that source's own copy is
/// verified</b> — length checked, renamed into place, and only then removed. A directory goes with a
/// non-recursive <see cref="Directory.Delete(string)"/>, so it can only vanish once every child
/// already has. Copy-then-<c>Directory.Delete(recursive: true)</c> gated on "the copy did not throw"
/// is how a half-finished move eats the original.</description></item>
/// <item><description><b>Running out of space parks the run instead of abandoning it.</b> The
/// destination volume is checked before each file and the run pauses on
/// <see cref="PauseReason.OutOfSpace"/>, so freeing space and retrying continues into the same
/// destination.</description></item>
/// <item><description><b>Reparse points are never recursed</b> — a junction is recreated as an empty
/// directory and listed. That is both the loop guard and what Explorer does.</description></item>
/// </list>
/// Win32 codes come back on the failures; user-facing wording is deliberately the caller's job.
/// </summary>
public static class FileTransferEngine
{
    /// <summary>Extension a cross-volume copy writes under until it is verified. A run killed with the
    /// process leaves one of these rather than a truncated file wearing the real name.</summary>
    internal const string PartialSuffix = ".nexaflow-partial";

    private const int BufferSize          = 1024 * 1024;
    private const int ErrorNotSameDevice  = 17;
    private const int ErrorHandleDiskFull = 39;
    private const int ErrorDiskFull       = 112;
    private const int ErrorAccessDenied   = 5;
    private const int ErrorSharingViolation = 32;

    internal static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Measures <paramref name="sources"/> so a run can report a determinate percentage. Unreadable
    /// parts of the tree are skipped and flagged through <see cref="TransferScan.Partial"/> — a scan
    /// never fails, because refusing to start a copy over a folder you cannot list helps nobody.
    /// </summary>
    public static Task<TransferScan> ScanAsync(IReadOnlyList<string> sources, CancellationToken ct = default)
        => ScanAsync(sources, fileSystem: null, ct);

    /// <summary>As <see cref="ScanAsync(IReadOnlyList{string}, CancellationToken)"/>, measuring a source the disk
    /// cannot reach through <paramref name="fileSystem"/> (<see cref="FileTransferRequest.FileSystem"/>).</summary>
    public static async Task<TransferScan> ScanAsync(IReadOnlyList<string> sources, IVirtualFileSystem? fileSystem,
                                                     CancellationToken ct = default)
    {
        var tally  = new ScanTally();
        var onDisk = new List<string>(sources.Count);
        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            if (fileSystem is null) onDisk.Add(source);
            else if (fileSystem.ResolveOnDisk(source) is { } real) onDisk.Add(real);
            else await MeasureThroughAsync(fileSystem, source, tally, ct);
        }

        await Task.Run(() =>
        {
            foreach (var source in onDisk)
            {
                ct.ThrowIfCancellationRequested();

                if (Directory.Exists(source)) Measure(source, tally, ct);
                else if (File.Exists(source)) tally.AddFile(LongPath.Prefix(source));
            }
        }, ct);

        return tally.ToScan();
    }

    /// <summary><see cref="Measure"/> through the file system: a link is counted as nothing and not followed, and a
    /// folder that cannot be listed makes the totals a floor.</summary>
    private static async Task MeasureThroughAsync(IVirtualFileSystem fileSystem, string path, ScanTally tally,
                                                  CancellationToken ct)
    {
        LocationEntry? entry;
        try { entry = await fileSystem.GetEntryAsync(path, ct); }
        catch (OperationCanceledException) { throw; }
        catch { tally.Partial = true; return; }

        if (entry is null) return;
        if (!entry.IsDirectory) { tally.AddFile(entry.Size); return; }

        tally.Folders++;
        var subs = new List<string>();
        try
        {
            await foreach (var child in fileSystem.EnumerateAsync(path, ct))
            {
                if (!child.IsDirectory) tally.AddFile(child.Size);
                else if (!child.IsSymlink) subs.Add(Path.Combine(path, child.Name));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { tally.Partial = true; }

        foreach (var sub in subs) await MeasureThroughAsync(fileSystem, sub, tally, ct);
    }

    private static void Measure(string directory, ScanTally tally, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        tally.Folders++;

        string[] files;
        try { files = Directory.GetFiles(LongPath.Prefix(directory)); }
        catch { tally.Partial = true; return; }

        foreach (var file in files) tally.AddFile(file);

        string[] subs;
        try { subs = Directory.GetDirectories(LongPath.Prefix(directory)); }
        catch { tally.Partial = true; return; }

        foreach (var sub in subs)
        {
            if (IsReparsePoint(sub)) continue;   // counted as nothing; it is not recursed
            Measure(LongPath.Display(sub), tally, ct);
        }
    }

    /// <summary>Running totals for <see cref="ScanAsync"/>. A file whose length cannot be read still
    /// counts as a file — the byte total is a floor, which is what <see cref="TransferScan.Partial"/>
    /// exists to say.</summary>
    private sealed class ScanTally
    {
        public long Bytes;
        public int  Files;
        public int  Folders;
        public bool Partial;

        public void AddFile(string path)
        {
            Files++;
            try { Bytes += new FileInfo(path).Length; }
            catch { Partial = true; }
        }

        /// <summary>A file whose length a listing already gave.</summary>
        public void AddFile(long length)
        {
            Files++;
            Bytes += length;
        }

        public TransferScan ToScan() => new(Bytes, Files, Folders, Partial);
    }
    /// <summary>
    /// Carries out <paramref name="request"/>. Pass <paramref name="scan"/> to get a determinate
    /// percentage; pass null and progress reports an item count with no byte total. A null
    /// <paramref name="prompt"/> means nobody is listening, so a run that would pause records a
    /// failure and moves on instead of waiting for an answer that will never come.
    /// </summary>
    public static Task<TransferResult> RunAsync(
        FileTransferRequest request,
        TransferScan? scan = null,
        IProgress<TransferProgress>? progress = null,
        IFileTransferPrompt? prompt = null,
        CancellationToken ct = default)
        => Task.Run(() => new Run(request, scan, progress, prompt, ct).ExecuteAsync(), CancellationToken.None);

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(LongPath.Prefix(path)) & FileAttributes.ReparsePoint) != 0; }
        catch { return false; }
    }

    private static int CodeOf(Exception ex) => ex switch
    {
        PathTooLongException        => 206,
        FileNotFoundException       => 2,
        DirectoryNotFoundException  => 3,
        UnauthorizedAccessException => 5,
        _                           => ex.HResult & 0xFFFF,
    };

    private static bool IsDiskFull(Exception ex) => CodeOf(ex) is ErrorDiskFull or ErrorHandleDiskFull;

    /// <summary>
    /// Whether a failed rename is worth retrying as a copy. A cross-volume move is the obvious one,
    /// but a directory rename also fails with <c>ERROR_ACCESS_DENIED</c> or <c>ERROR_SHARING_VIOLATION</c>
    /// when something inside the tree is open — where a copy succeeds, because it reads each file with
    /// <see cref="FileShare.ReadWrite"/> | <see cref="FileShare.Delete"/> rather than needing the whole
    /// subtree to itself. Falling back costs nothing when the copy is genuinely impossible: it then
    /// fails per item, with a message naming the file rather than the folder.
    /// </summary>
    private static bool CanCopyInstead(Exception ex)
        => CodeOf(ex) is ErrorNotSameDevice or ErrorAccessDenied or ErrorSharingViolation;

    /// <summary>Test seam standing in for the real volume query, so the out-of-space paths can be
    /// exercised without filling a disk. Null in every real run.</summary>
    internal static Func<string, long?>? FreeSpaceProbe;

    /// <summary>Test seam: called with each verified copy's partial file after it is written and before it is read back,
    /// so a test can change what landed and see the read-back catch it. Null in production.</summary>
    internal static Action<string>? AfterWriteProbe;

    /// <summary>Free bytes on the volume holding <paramref name="path"/>, or null when it cannot be
    /// determined (a UNC share, a removed drive). Unknown means "go ahead" — a mid-flight disk-full
    /// still parks the run, so a failed check costs a retry rather than a wrong refusal.</summary>
    private static long? FreeSpaceFor(string path)
    {
        if (FreeSpaceProbe is { } probe) return probe(path);

        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch { return null; }
    }

    private static string DescribeSpace(string destination, long bytes)
    {
        long? free = FreeSpaceFor(destination);
        string need = Bytes(bytes);
        string have = free is null ? "the destination" : Bytes(free.Value);
        return $"There isn't enough free space to continue — {need} needed, {have} free.";
    }

    private static string Bytes(long value)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double v = value;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{value} {units[0]}" : $"{v:0.#} {units[u]}";
    }

    // ── One execution ─────────────────────────────────────────────────────────

    private sealed class Run(
        FileTransferRequest request,
        TransferScan? scan,
        IProgress<TransferProgress>? progress,
        IFileTransferPrompt? prompt,
        CancellationToken ct)
    {
        private readonly List<TransferItemFailure> _failures = [];
        private readonly List<string> _skippedReparsePoints  = [];
        private readonly List<string> _renamed               = [];
        private readonly List<string> _partials              = [];

        private readonly List<VerifiedFile> _verified = [];
        private bool _verifying;

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private TimeSpan? _lastReportAt;


        private long    _bytesDone;
        private int     _itemsDone;
        private string? _currentItem;
        private bool    _cancelled;

        private int  ItemsTotal => scan?.TotalFiles ?? request.Items.Count;
        private long BytesTotal => scan?.TotalBytes ?? 0;

        private bool Stopping => _cancelled || ct.IsCancellationRequested;

        public async Task<TransferResult> ExecuteAsync()
        {
            Report(TransferPhase.Running, force: true);

            foreach (var requested in request.Items)
            {
                if (Stopping) break;

                if (OnDisk(requested) is not { } item)
                {
                    switch (request.Kind)
                    {
                        case TransferKind.Copy:   await CopyThroughAsync(requested);   break;
                        case TransferKind.Move:   await MoveThroughAsync(requested);   break;
                        case TransferKind.Delete: await DeleteThroughAsync(requested); break;
                    }
                    continue;
                }

                switch (request.Kind)
                {
                    case TransferKind.Copy:   await CopyItemAsync(item, forMove: false); break;
                    case TransferKind.Move:   await MoveItemAsync(item);                 break;
                    case TransferKind.Delete: DeleteItem(item.Source); DeleteCompanions(item); break;
                }
            }

            Report(TransferPhase.Finished, force: true);

            return new TransferResult(
                Completed:            !Stopping,
                BytesTransferred:     _bytesDone,
                ItemsTransferred:     _itemsDone,
                Failures:             _failures,
                SkippedReparsePoints: _skippedReparsePoints,
                RenamedDestinations:  _renamed,
                    PartialDestinations:  _partials)
                {
                    Verified = _verified,
                };
        }

        // ── Move ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Tries the rename first and lets Windows decide whether it is possible. Drive letters are
        /// not compared: a mounted volume folder shares a letter with a different volume, so
        /// <c>ERROR_NOT_SAME_DEVICE</c> is the only reliable oracle. When it renames, a 200 GB folder
        /// move is instant and no bytes are read.
        /// </summary>
        private async Task MoveItemAsync(TransferItem item)
        {
            bool isDir  = Directory.Exists(item.Source);
            bool isFile = File.Exists(item.Source);
            if (!isDir && !isFile) return;   // vanished between the gesture and the run — not a complaint

            var (destination, outcome) = ResolveDestination(item.Destination, isDir, request.Conflicts);
            if (outcome == Resolution.Skip) { _itemsDone++; return; }
            if (outcome == Resolution.Clash)
            {
                Fail("move", item.Source, 183, $"\"{Name(item.Source)}\" already exists at the destination.");
                return;
            }

            _currentItem = item.Source;

            // The rename only applies to a destination that is not there yet: Directory.Move cannot merge
            // into an existing folder, and dropping onto a folder you have already dropped onto is ordinary.
            // Everything else takes the copy route, which does know how to merge.
            bool vacant = isDir ? !Directory.Exists(destination) : !File.Exists(destination);

            if (vacant)
            {
                try
                {
                    if (isDir)
                        Directory.Move(LongPath.Prefix(item.Source), LongPath.Prefix(destination));
                    else
                        File.Move(LongPath.Prefix(item.Source), LongPath.Prefix(destination), overwrite: false);

                    _itemsDone++;
                    if (isFile) CarryCompanions(item, destination, move: true);
                    Report(TransferPhase.Running, force: true);
                    return;
                }
                catch (Exception ex) when (CanCopyInstead(ex))
                {
                    // Falls through to copy-then-delete below.
                }
                catch (Exception ex)
                {
                    Fail("move", item.Source, ex);
                    return;
                }
            }

            await CopyItemAsync(item with { Destination = destination }, forMove: true, alreadyResolved: true);
        }

        // ── Copy ──────────────────────────────────────────────────────────────

        private async Task CopyItemAsync(TransferItem item, bool forMove, bool alreadyResolved = false)
        {
            bool isDir  = Directory.Exists(item.Source);
            bool isFile = File.Exists(item.Source);
            if (!isDir && !isFile) return;

            string destination = item.Destination;
            if (!alreadyResolved)
            {
                var (resolved, outcome) = ResolveDestination(item.Destination, isDir, request.Conflicts);
                if (outcome == Resolution.Skip) { _itemsDone++; return; }
                if (outcome == Resolution.Clash)
                {
                    Fail("copy", item.Source, 183, $"\"{Name(item.Source)}\" already exists at the destination.");
                    return;
                }
                destination = resolved;
            }

            if (isDir) await CopyDirectoryAsync(item.Source, destination, forMove);
            else if (await CopyFileAsync(item.Source, destination, forMove, resolved: true))
                CarryCompanions(item, destination, move: forMove);
        }

        /// <summary>
        /// Returns true only when every descendant landed. That is the gate on removing the source
        /// directory during a move — a single unreadable child keeps the original.
        /// </summary>
        private async Task<bool> CopyDirectoryAsync(string source, string destination, bool forMove)
        {
            if (Stopping) return false;

            if (IsReparsePoint(source))
            {
                // Recreated empty rather than followed: a junction pointing at an ancestor would
                // otherwise recurse until the path gives out.
                _skippedReparsePoints.Add(source);
                try { Directory.CreateDirectory(LongPath.Prefix(destination)); } catch { }
                return false;   // never let a move delete a junction it did not copy through
            }

            try { Directory.CreateDirectory(LongPath.Prefix(destination)); }
            catch (Exception ex) { Fail("create", destination, ex); return false; }

            // Enumeration is the fault the recursion this replaces left outside its try — an
            // access-denied subfolder escaped as an unhandled exception and abandoned everything after it.
            string[] files;
            try { files = Directory.GetFiles(LongPath.Prefix(source)); }
            catch (Exception ex) { Fail("read", source, ex); return false; }

            bool complete = true;

            foreach (var file in files)
            {
                if (Stopping) return false;
                string plain = LongPath.Display(file);
                complete &= await CopyFileAsync(plain, Path.Combine(destination, Path.GetFileName(plain)), forMove, resolved: false);
            }

            string[] subs;
            try { subs = Directory.GetDirectories(LongPath.Prefix(source)); }
            catch (Exception ex) { Fail("read", source, ex); return false; }

            foreach (var sub in subs)
            {
                if (Stopping) return false;
                string plain = LongPath.Display(sub);
                complete &= await CopyDirectoryAsync(plain, Path.Combine(destination, Path.GetFileName(plain)), forMove);
            }

            CopyDirectoryMetadata(source, destination);

            if (forMove && complete && !Stopping)
            {
                // Non-recursive on purpose: it can only succeed once every child has already gone,
                // so an incomplete copy physically cannot take the original with it.
                try { Directory.Delete(LongPath.Prefix(source)); }
                catch (Exception ex) { Fail("remove", source, ex); complete = false; }
            }

            return complete;
        }

        private async Task<bool> CopyFileAsync(string source, string destination, bool forMove, bool resolved)
        {
            if (Stopping) return false;

            if (!resolved)
            {
                var (path, outcome) = ResolveDestination(destination, isDirectory: false, request.Conflicts);
                if (outcome == Resolution.Skip) { _itemsDone++; return true; }
                if (outcome == Resolution.Clash)
                {
                    Fail("copy", source, 183, $"\"{Name(source)}\" already exists at the destination.");
                    return false;
                }
                destination = path;
            }

            long length;
            try { length = new FileInfo(LongPath.Prefix(source)).Length; }
            catch (Exception ex) { Fail("read", source, ex); return false; }

            _currentItem = source;
            string partial = destination + PartialSuffix;

            while (true)
            {
                if (Stopping) return false;
                if (!await EnsureSpaceAsync(destination, length, source)) return false;

                string? sourceHash;
                try
                {
                    sourceHash = await CopyBytesAsync(source, partial);
                }
                catch (Exception ex) when (IsDiskFull(ex))
                {
                    TryDelete(partial);
                    if (await PauseAsync(PauseReason.OutOfSpace, DescribeSpace(destination, length))) continue;
                    return false;
                }
                catch (OperationCanceledException)
                {
                    TryDelete(partial);
                    return false;
                }
                catch (Exception ex)
                {
                    TryDelete(partial);
                    Fail("copy", source, ex);
                    return false;
                }

                // Verified: the destination only takes the real name, and the source is only allowed
                // to disappear, once the bytes are known to have arrived.
                try
                {
                    long actual = new FileInfo(LongPath.Prefix(partial)).Length;
                    if (length > 0 && actual != length)
                    {
                        TryDelete(partial);
                        Fail("copy", source, 0, $"\"{Name(source)}\" did not copy completely and was left alone.");
                        return false;
                    }

                    if (sourceHash is not null)
                    {
                        AfterWriteProbe?.Invoke(partial);
                        string landed = await ReadBackAsync(partial);
                        if (!string.Equals(landed, sourceHash, StringComparison.Ordinal))
                        {
                            TryDelete(partial);
                            Fail("verify", source, 0, $"\"{Name(source)}\" read back different from what was written, so the copy was removed.");
                            return false;
                        }
                    }

                    CopyFileMetadata(source, partial);
                    File.Move(LongPath.Prefix(partial), LongPath.Prefix(destination), overwrite: true);
                    if (sourceHash is not null) _verified.Add(new VerifiedFile(destination, actual, sourceHash));
                }
                catch (OperationCanceledException)
                {
                    TryDelete(partial);
                    return false;
                }
                catch (Exception ex)
                {
                    _partials.Add(partial);
                    Fail("copy", source, ex);
                    return false;
                }

                if (forMove)
                {
                    try { File.Delete(LongPath.Prefix(source)); }
                    catch (Exception ex) { Fail("remove", source, ex); return false; }
                }

                _itemsDone++;
                Report(TransferPhase.Running, force: true);
                return true;
            }
        }

        /// <summary>
        /// Streams one file, returning the source's SHA-256 when the run verifies (null otherwise). A failed attempt rewinds
        /// the running total before it propagates, so retrying the file cannot count its bytes twice. A verified copy is
        /// flushed through to the media before it is read back.
        /// </summary>
        private async Task<string?> CopyBytesAsync(string source, string partial)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            long before = _bytesDone;
            using var hash = request.Verify ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;

            try
            {
                // FileShare.ReadWrite | Delete so a file held open elsewhere still copies — the
                // guarantee DirectoryMover documented and its callers depend on.
                await using var from = new FileStream(LongPath.Prefix(source), FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var to = new FileStream(LongPath.Prefix(partial), FileMode.Create, FileAccess.Write,
                    FileShare.None, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    int read = await from.ReadAsync(buffer.AsMemory(0, BufferSize), ct);
                    if (read == 0) break;

                    await to.WriteAsync(buffer.AsMemory(0, read), ct);
                    hash?.AppendData(buffer, 0, read);
                    _bytesDone += read;
                    Report(TransferPhase.Running);
                }

                if (hash is not null)
                {
                    await to.FlushAsync(ct);
                    to.Flush(flushToDisk: true);
                }
            }
            catch
            {
                _bytesDone = before;   // rewind so a retry of this file does not count its bytes twice
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return hash is null ? null : Convert.ToHexString(hash.GetHashAndReset());
        }

        /// <summary>
        /// The SHA-256 of what is actually on the media at <paramref name="path"/>. Opened unbuffered, so the read goes to
        /// the device rather than to the cache the write just filled; that needs sector-aligned reads into aligned memory,
        /// which a pinned buffer sliced to a 4 KiB boundary gives. A volume that refuses unbuffered reads is read normally.
        /// </summary>
        private async Task<string> ReadBackAsync(string path)
        {
            const FileOptions NoBuffering = (FileOptions)0x20000000;
            const int Alignment = 4096;

            _verifying = true;
            Report(TransferPhase.Running, force: true);
            try
            {
                SafeFileHandle handle;
                try { handle = File.OpenHandle(LongPath.Prefix(path), FileMode.Open, FileAccess.Read, FileShare.Read, NoBuffering | FileOptions.Asynchronous); }
                catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
                {
                    handle = File.OpenHandle(LongPath.Prefix(path), FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous);
                }

                using (handle)
                {
                    var pinned = GC.AllocateUninitializedArray<byte>(BufferSize + Alignment, pinned: true);
                    long address = Marshal.UnsafeAddrOfPinnedArrayElement(pinned, 0).ToInt64();
                    int offset = (int)((Alignment - address % Alignment) % Alignment);
                    var window = pinned.AsMemory(offset, BufferSize);

                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    for (long at = 0; ; )
                    {
                        ct.ThrowIfCancellationRequested();
                        int read = await RandomAccess.ReadAsync(handle, window, at, ct);
                        if (read == 0) break;
                        hash.AppendData(window.Span[..read]);
                        at += read;
                        if (read < BufferSize) break;
                    }
                    return Convert.ToHexString(hash.GetHashAndReset());
                }
            }
            finally
            {
                _verifying = false;
            }
        }

        // ── Delete ────────────────────────────────────────────────────────────

        private void DeleteItem(string path)
        {
            if (Stopping) return;
            _currentItem = path;

            if (File.Exists(path))
            {
                try { File.Delete(LongPath.Prefix(path)); _itemsDone++; }
                catch (Exception ex) { Fail("delete", path, ex); }
                Report(TransferPhase.Running, force: true);
                return;
            }

            if (!Directory.Exists(path)) return;
            DeleteDirectory(path);
            Report(TransferPhase.Running, force: true);
        }

        // ── Companions ────────────────────────────────────────────────────────

        /// <summary>
        /// A file's companions follow it to wherever it landed — its resolved name, so they pair with a <c>(2)</c>
        /// rather than keep the old one (<see cref="CompanionFiles.Follow"/>). A companion that cannot follow is reported;
        /// the primary stays where it landed.
        /// </summary>
        private void CarryCompanions(TransferItem item, string destination, bool move)
        {
            foreach (var (path, error) in CompanionFiles.Follow(item.Source, destination, item.CompanionSuffixes, move))
                Fail(move ? "move" : "copy", path, error);
        }

        /// <summary>A deleted file's companions go with it — only once it has gone.</summary>
        private void DeleteCompanions(TransferItem item)
        {
            foreach (var (path, error) in CompanionFiles.DeleteWith(item.Source, item.CompanionSuffixes))
                Fail("delete", path, error);
        }

        private bool DeleteDirectory(string path)
        {
            if (Stopping) return false;

            if (IsReparsePoint(path))
            {
                // Remove the link, never what it points at.
                try { Directory.Delete(LongPath.Prefix(path)); return true; }
                catch (Exception ex) { Fail("delete", path, ex); return false; }
            }

            string[] files;
            try { files = Directory.GetFiles(LongPath.Prefix(path)); }
            catch (Exception ex) { Fail("read", path, ex); return false; }

            bool complete = true;

            foreach (var file in files)
            {
                if (Stopping) return false;
                try { File.Delete(file); _itemsDone++; Report(TransferPhase.Running); }
                catch (Exception ex) { Fail("delete", LongPath.Display(file), ex); complete = false; }
            }

            string[] subs;
            try { subs = Directory.GetDirectories(LongPath.Prefix(path)); }
            catch (Exception ex) { Fail("read", path, ex); return false; }

            foreach (var sub in subs)
            {
                if (Stopping) return false;
                complete &= DeleteDirectory(LongPath.Display(sub));
            }

            if (!complete) return false;

            try { Directory.Delete(LongPath.Prefix(path)); return true; }
            catch (Exception ex) { Fail("delete", path, ex); return false; }
        }

        // ── Through the file system ───────────────────────────────────────────
        // An item with an end the disk cannot reach — inside an archive, on a location — goes through the runtime's file
        // system. The same promises hold: nothing throws, a move removes each source only once its copy has committed, a link
        // is never followed, and a verified run reads every file back from where it landed. The write commits whole or not at
        // all, which is what the partial name gives a copy on disk. Free-space parking applies where the destination is on this
        // disk; the unbuffered read-back and the attribute copy need a Windows volume at both ends, so they stay the disk's.

        private IVirtualFileSystem FileSystem => request.FileSystem!;

        /// <summary>The item with both ends as real paths on this disk, or null when either end is somewhere only the file
        /// system reaches.</summary>
        private TransferItem? OnDisk(TransferItem item)
        {
            if (request.FileSystem is not { } fileSystem) return item;
            return fileSystem.ResolveOnDisk(item.Source) is { } source && fileSystem.ResolveOnDisk(item.Destination) is { } destination
                ? item with { Source = source, Destination = destination }
                : null;
        }

        /// <summary>The entry at <paramref name="path"/>; null when it is gone — not a complaint — or could not be asked
        /// about, which is recorded.</summary>
        private async Task<LocationEntry?> EntryThroughAsync(string path, string verb)
        {
            try { return await FileSystem.GetEntryAsync(path, ct); }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex) { Fail(verb, path, ex); return null; }
        }

        private async Task MoveThroughAsync(TransferItem item)
        {
            if (await EntryThroughAsync(item.Source, "move") is not { } source) return;

            var (destination, outcome) = await ResolveThroughAsync(item.Destination, source.IsDirectory, request.Conflicts);
            if (outcome == Resolution.Skip) { _itemsDone++; return; }
            if (outcome == Resolution.Clash)
            {
                Fail("move", item.Source, 183, $"\"{Name(item.Source)}\" already exists at the destination.");
                return;
            }

            _currentItem = item.Source;

            // A rename within one location moves no bytes. Between locations, or out of an archive, the file system says it
            // cannot — and the copy route below, which also merges into an existing folder, does it instead.
            if (await TakenThroughAsync(destination) is null)
            {
                try
                {
                    await FileSystem.MoveAsync(item.Source, destination, replace: false, ct);
                    _itemsDone++;
                    if (!source.IsDirectory) await CarryThroughAsync(item, destination, move: true);
                    Report(TransferPhase.Running, force: true);
                    return;
                }
                catch (NotSupportedException) { /* falls through to copy-then-remove */ }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    Fail("move", item.Source, ex);
                    return;
                }
            }

            await CopyThroughAsync(item with { Destination = destination }, source, forMove: true);
        }

        private async Task CopyThroughAsync(TransferItem item, LocationEntry? known = null, bool forMove = false)
        {
            if ((known ?? await EntryThroughAsync(item.Source, "copy")) is not { } source) return;

            string destination = item.Destination;
            if (known is null)
            {
                var (resolved, outcome) = await ResolveThroughAsync(item.Destination, source.IsDirectory, request.Conflicts);
                if (outcome == Resolution.Skip) { _itemsDone++; return; }
                if (outcome == Resolution.Clash)
                {
                    Fail("copy", item.Source, 183, $"\"{Name(item.Source)}\" already exists at the destination.");
                    return;
                }
                destination = resolved;
            }

            if (source.IsDirectory) await CopyDirectoryThroughAsync(item.Source, destination, forMove);
            else if (await CopyFileThroughAsync(item.Source, source.Size, destination, forMove, resolved: true))
                await CarryThroughAsync(item, destination, move: forMove);
        }

        /// <summary>As <see cref="CopyDirectoryAsync"/>: true only when every descendant landed, which is the gate on
        /// removing the source folder during a move.</summary>
        private async Task<bool> CopyDirectoryThroughAsync(string source, string destination, bool forMove)
        {
            if (Stopping) return false;

            try { await FileSystem.CreateDirectoryAsync(destination, ct); }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex) { Fail("create", destination, ex); return false; }

            var children = new List<LocationEntry>();
            try
            {
                await foreach (var child in FileSystem.EnumerateAsync(source, ct)) children.Add(child);
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex) { Fail("read", source, ex); return false; }

            bool complete = true;

            foreach (var file in children.Where(c => !c.IsDirectory))
            {
                if (Stopping) return false;
                complete &= await CopyFileThroughAsync(Path.Combine(source, file.Name), file.Size,
                                                       Path.Combine(destination, file.Name), forMove, resolved: false);
            }

            foreach (var folder in children.Where(c => c.IsDirectory))
            {
                if (Stopping) return false;
                string from = Path.Combine(source, folder.Name), to = Path.Combine(destination, folder.Name);
                if (folder.IsSymlink)
                {
                    // Recreated empty, never followed — as a junction is on disk.
                    _skippedReparsePoints.Add(from);
                    try { await FileSystem.CreateDirectoryAsync(to, ct); } catch { }
                    complete = false;
                    continue;
                }
                complete &= await CopyDirectoryThroughAsync(from, to, forMove);
            }

            if (forMove && complete && !Stopping)
            {
                // Non-recursive on purpose, as on disk: it only succeeds once every child has already gone.
                try { await FileSystem.DeleteAsync(source, recursive: false, ct); }
                catch (OperationCanceledException) { return false; }
                catch (Exception ex) { Fail("remove", source, ex); complete = false; }
            }

            return complete;
        }

        private async Task<bool> CopyFileThroughAsync(string source, long length, string destination, bool forMove, bool resolved)
        {
            if (Stopping) return false;

            if (!resolved)
            {
                var (path, outcome) = await ResolveThroughAsync(destination, isDirectory: false, request.Conflicts);
                if (outcome == Resolution.Skip) { _itemsDone++; return true; }
                if (outcome == Resolution.Clash)
                {
                    Fail("copy", source, 183, $"\"{Name(source)}\" already exists at the destination.");
                    return false;
                }
                destination = path;
            }

            _currentItem = source;
            // The destination was settled by the conflict policy; a name taken since is a clash at the commit, not a write over it.
            var mode = request.Conflicts is ConflictPolicy.Overwrite or ConflictPolicy.Resume ? WriteMode.Replace : WriteMode.CreateNew;

            // Within one location that copies on its server, the bytes need not cross the connection twice. A verified run
            // streams instead, because it hashes what it sent.
            if (!request.Verify && CopiesOnServer(source, destination))
            {
                try
                {
                    await FileSystem.CopyWithinAsync(source, destination, replace: mode == WriteMode.Replace, ct);
                    _bytesDone += length;
                    return await FinishThroughAsync(source, forMove);
                }
                catch (NotSupportedException) { /* streams below */ }
                catch (OperationCanceledException) { return false; }
                catch (Exception ex) { Fail("copy", source, ex); return false; }
            }

            string? onDisk = FileSystem.ResolveOnDisk(destination);

            while (true)
            {
                if (Stopping) return false;
                if (onDisk is not null && !await EnsureSpaceAsync(onDisk, length, source)) return false;

                long before = _bytesDone;
                string? sourceHash;
                try
                {
                    sourceHash = await StreamThroughAsync(source, destination, mode);
                }
                catch (Exception ex) when (IsDiskFull(ex))
                {
                    _bytesDone = before;
                    if (await PauseAsync(PauseReason.OutOfSpace, DescribeSpace(destination, length))) continue;
                    return false;
                }
                catch (OperationCanceledException)
                {
                    _bytesDone = before;
                    return false;
                }
                catch (Exception ex)
                {
                    _bytesDone = before;
                    Fail("copy", source, ex);
                    return false;
                }

                // The write committed whole, so the copy can only be wrong if the destination keeps different bytes than it was
                // sent; a verified run reads it back to find out, and takes the copy away again when it does.
                if (sourceHash is not null)
                {
                    string landed;
                    try { landed = await HashThroughAsync(destination); }
                    catch (OperationCanceledException) { return false; }
                    catch (Exception ex) { Fail("verify", source, ex); return false; }

                    if (!string.Equals(landed, sourceHash, StringComparison.Ordinal))
                    {
                        try { await FileSystem.DeleteAsync(destination, recursive: false, CancellationToken.None); } catch { }
                        Fail("verify", source, 0, $"\"{Name(source)}\" read back different from what was written, so the copy was removed.");
                        return false;
                    }
                    _verified.Add(new VerifiedFile(destination, length, sourceHash));
                }

                return await FinishThroughAsync(source, forMove);
            }
        }

        /// <summary>A file that has landed: a move takes the source away now, and only now; the item is counted.</summary>
        private async Task<bool> FinishThroughAsync(string source, bool forMove)
        {
            if (forMove)
            {
                try { await FileSystem.DeleteAsync(source, recursive: false, ct); }
                catch (OperationCanceledException) { return false; }
                catch (Exception ex) { Fail("remove", source, ex); return false; }
            }

            _itemsDone++;
            Report(TransferPhase.Running, force: true);
            return true;
        }

        /// <summary>Whether both ends are on one location that copies on its server — the same <c>::{id}</c> root, and the
        /// capability declared — so asking it to costs no exception per file when it cannot.</summary>
        private bool CopiesOnServer(string source, string destination)
        {
            static string RootOf(string path) => path.Split('\\', 2)[0];
            return source.StartsWith("::", StringComparison.Ordinal)
                   && string.Equals(RootOf(source), RootOf(destination), StringComparison.OrdinalIgnoreCase)
                   && FileSystem.GetCapabilities(source).HasFlag(LocationCapabilities.ServerSideCopy);
        }

        /// <summary>Streams one file through the file system, returning the source's SHA-256 when the run verifies. The write
        /// is invisible at <paramref name="destination"/> until it commits, so a failure or a stop leaves nothing there.</summary>
        private async Task<string?> StreamThroughAsync(string source, string destination, WriteMode mode)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            using var hash = request.Verify ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
            try
            {
                await using var from  = await FileSystem.OpenReadAsync(source, ReadIntent.Sequential, ct);
                await using var write = await FileSystem.BeginWriteAsync(destination, mode, expected: null, ct);
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    int read = await from.ReadAsync(buffer.AsMemory(0, BufferSize), ct);
                    if (read == 0) break;

                    await write.Content.WriteAsync(buffer.AsMemory(0, read), ct);
                    hash?.AppendData(buffer, 0, read);
                    _bytesDone += read;
                    Report(TransferPhase.Running);
                }
                await write.CommitAsync(ct);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return hash is null ? null : Convert.ToHexString(hash.GetHashAndReset());
        }

        /// <summary>The SHA-256 of what is at <paramref name="path"/>, read back through the file system.</summary>
        private async Task<string> HashThroughAsync(string path)
        {
            _verifying = true;
            Report(TransferPhase.Running, force: true);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                await using var stream = await FileSystem.OpenReadAsync(path, ReadIntent.Sequential, ct);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
                    hash.AppendData(buffer, 0, read);
                return Convert.ToHexString(hash.GetHashAndReset());
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                _verifying = false;
            }
        }

        private async Task DeleteThroughAsync(TransferItem item)
        {
            if (Stopping) return;
            _currentItem = item.Source;
            if (await EntryThroughAsync(item.Source, "delete") is not { } entry) return;

            if (entry.IsDirectory && !entry.IsSymlink)
            {
                await DeleteDirectoryThroughAsync(item.Source);
            }
            else
            {
                // A link goes, never what it points at.
                try { await FileSystem.DeleteAsync(item.Source, recursive: false, ct); _itemsDone++; }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { Fail("delete", item.Source, ex); return; }

                foreach (var (path, error) in await CompanionFiles.DeleteWithAsync(FileSystem, item.Source, item.CompanionSuffixes, ct))
                    Fail("delete", path, error);
            }
            Report(TransferPhase.Running, force: true);
        }

        /// <summary>As <see cref="DeleteDirectory"/>: each file counted as it goes, and the folder itself only once it is
        /// empty.</summary>
        private async Task<bool> DeleteDirectoryThroughAsync(string path)
        {
            if (Stopping) return false;

            var children = new List<LocationEntry>();
            try
            {
                await foreach (var child in FileSystem.EnumerateAsync(path, ct)) children.Add(child);
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex) { Fail("read", path, ex); return false; }

            bool complete = true;
            foreach (var child in children)
            {
                if (Stopping) return false;
                string childPath = Path.Combine(path, child.Name);
                if (child.IsDirectory && !child.IsSymlink)
                {
                    complete &= await DeleteDirectoryThroughAsync(childPath);
                    continue;
                }
                try { await FileSystem.DeleteAsync(childPath, recursive: false, ct); _itemsDone++; Report(TransferPhase.Running); }
                catch (OperationCanceledException) { return false; }
                catch (Exception ex) { Fail("delete", childPath, ex); complete = false; }
            }

            if (!complete) return false;

            try { await FileSystem.DeleteAsync(path, recursive: false, ct); return true; }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex) { Fail("delete", path, ex); return false; }
        }

        /// <summary>A file's companions follow it to where it landed, through the file system.</summary>
        private async Task CarryThroughAsync(TransferItem item, string destination, bool move)
        {
            foreach (var (path, error) in await CompanionFiles.FollowAsync(FileSystem, item.Source, destination, item.CompanionSuffixes, move, ct))
                Fail(move ? "move" : "copy", path, error);
        }

        /// <summary>What is at <paramref name="path"/>, or null when nothing is or nobody can say — a write there then finds
        /// out.</summary>
        private async Task<LocationEntry?> TakenThroughAsync(string path)
        {
            try { return await FileSystem.GetEntryAsync(path, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
        }

        /// <summary><see cref="ResolveDestination"/> through the file system.</summary>
        private async Task<(string Path, Resolution Outcome)> ResolveThroughAsync(string desired, bool isDirectory, ConflictPolicy policy)
        {
            if (await TakenThroughAsync(desired) is not { } there || there.IsDirectory != isDirectory)
                return (desired, Resolution.Proceed);

            switch (policy)
            {
                case ConflictPolicy.Overwrite:
                    return (desired, Resolution.Proceed);

                case ConflictPolicy.Skip:
                    return (desired, Resolution.Skip);

                case ConflictPolicy.Fail:
                    return (desired, Resolution.Clash);

                case ConflictPolicy.Resume:
                    return (desired, isDirectory ? Resolution.Proceed : Resolution.Skip);

                default:
                    string dir  = Path.GetDirectoryName(desired) ?? desired;
                    string stem = isDirectory ? Path.GetFileName(desired) : Path.GetFileNameWithoutExtension(desired);
                    string ext  = isDirectory ? string.Empty : Path.GetExtension(desired);
                    for (int i = 2; ; i++)
                    {
                        string candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
                        if (await TakenThroughAsync(candidate) is null)
                        {
                            _renamed.Add(candidate);
                            return (candidate, Resolution.Proceed);
                        }
                    }
            }
        }

        // ── Space ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Parks the run before writing when the volume cannot hold <paramref name="bytes"/>. Checking
        /// up front is what turns "half the folder arrived and the rest vanished" into "nothing was
        /// touched and you were told why".
        /// </summary>
        private async Task<bool> EnsureSpaceAsync(string destination, long bytes, string source)
        {
            while (true)
            {
                long? free = FreeSpaceFor(destination);
                if (free is null || free >= bytes) return true;

                if (prompt is null)
                {
                    Fail("copy", source, ErrorDiskFull, DescribeSpace(destination, bytes));
                    return false;
                }

                if (!await PauseAsync(PauseReason.OutOfSpace, DescribeSpace(destination, bytes))) return false;
            }
        }

        /// <summary>Returns true to retry, false to give up. A refusal marks the whole run cancelled —
        /// nothing has been deleted at that point, which is the entire value of pausing.</summary>
        private async Task<bool> PauseAsync(PauseReason reason, string detail)
        {
            if (prompt is null) { _cancelled = true; return false; }

            Report(TransferPhase.Paused, force: true, paused: reason);

            PauseDecision decision;
            try { decision = await prompt.OnPausedAsync(reason, detail, ct); }
            catch { decision = PauseDecision.Cancel; }

            if (decision == PauseDecision.Retry)
            {
                Report(TransferPhase.Running, force: true);
                return true;
            }

            _cancelled = true;
            return false;
        }

        // ── Destination resolution ────────────────────────────────────────────

        private enum Resolution { Proceed, Skip, Clash }

        private (string Path, Resolution Outcome) ResolveDestination(string desired, bool isDirectory, ConflictPolicy policy)
        {
            bool exists = isDirectory ? Directory.Exists(desired) : File.Exists(desired);
            if (!exists) return (desired, Resolution.Proceed);

            switch (policy)
            {
                case ConflictPolicy.Overwrite:
                    return (desired, Resolution.Proceed);

                case ConflictPolicy.Skip:
                    return (desired, Resolution.Skip);

                case ConflictPolicy.Fail:
                    return (desired, Resolution.Clash);

                case ConflictPolicy.Resume:
                    // A directory is walked back into; a file already there is taken as done.
                    return (desired, isDirectory ? Resolution.Proceed : Resolution.Skip);

                default:
                    string unique = UniquePath(desired, isDirectory);
                    _renamed.Add(unique);
                    return (unique, Resolution.Proceed);
            }
        }

        private static string UniquePath(string path, bool isDirectory)
        {
            string dir  = Path.GetDirectoryName(path) ?? path;
            string stem = isDirectory ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
            string ext  = isDirectory ? string.Empty : Path.GetExtension(path);

            for (int i = 2; ; i++)
            {
                string candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
                bool taken = isDirectory ? Directory.Exists(candidate) : File.Exists(candidate);
                if (!taken) return candidate;
            }
        }

        // ── Bookkeeping ───────────────────────────────────────────────────────

        private static string Name(string path)
        {
            string trimmed = LongPath.Display(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name    = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }

        private void Fail(string verb, string path, Exception ex) => Fail(verb, path, CodeOf(ex), ex.Message);

        private void Fail(string verb, string path, int code, string message)
            => _failures.Add(new TransferItemFailure(LongPath.Display(path), verb, code, message));

        private static void TryDelete(string path)
        {
            try { if (File.Exists(LongPath.Prefix(path))) File.Delete(LongPath.Prefix(path)); } catch { }
        }

        private static void CopyFileMetadata(string source, string destination)
        {
            // Best-effort — never fail a copy over an attribute that would not set.
            try { File.SetLastWriteTimeUtc(LongPath.Prefix(destination), File.GetLastWriteTimeUtc(LongPath.Prefix(source))); } catch { }
            try { File.SetAttributes(LongPath.Prefix(destination), File.GetAttributes(LongPath.Prefix(source))); } catch { }
        }

        private static void CopyDirectoryMetadata(string source, string destination)
        {
            try { Directory.SetLastWriteTimeUtc(LongPath.Prefix(destination), Directory.GetLastWriteTimeUtc(LongPath.Prefix(source))); } catch { }
        }

        /// <summary>
        /// Rate-limited inside the engine so no sink needs a throttle of its own: at most one report
        /// every <see cref="ReportInterval"/> while bytes move, plus one on every item boundary.
        /// <para>
        /// Throughput is the average over the whole run, not the rate of the last moment. A recent-window
        /// average reads the write cache rather than the disk: the first seconds report several GB/s while
        /// Windows buffers, and then the copy appears to hang at that impossible number while the cache
        /// drains to a mechanical disk at a fraction of it. Averaging over the run converges on the speed
        /// the copy is actually achieving, and — because it keeps falling while nothing is being written —
        /// a stall shows up as a number going down rather than a frozen one.
        /// </para>
        /// </summary>
        private void Report(TransferPhase phase, bool force = false, PauseReason? paused = null)
        {
            if (progress is null) return;

            TimeSpan now = _clock.Elapsed;
            if (!force && _lastReportAt is { } gate && now - gate < ReportInterval) return;

            _lastReportAt = now;

            double elapsed = now.TotalSeconds;
            double perSecond = elapsed > 0.25 ? _bytesDone / elapsed : 0;

            long total = BytesTotal;
            TimeSpan? left = perSecond > 1 && total > _bytesDone
                ? TimeSpan.FromSeconds(Math.Min((total - _bytesDone) / perSecond, TimeSpan.MaxValue.TotalSeconds - 1))
                : null;

            progress.Report(new TransferProgress(
                phase, _bytesDone, total, _itemsDone, ItemsTotal,
                _currentItem is null ? null : Name(_currentItem),
                (long)Math.Max(0, perSecond), left, paused, _verifying));
        }
    }
}
