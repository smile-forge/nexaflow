using System.Buffers;
using System.Diagnostics;

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

    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Measures <paramref name="sources"/> so a run can report a determinate percentage. Unreadable
    /// parts of the tree are skipped and flagged through <see cref="TransferScan.Partial"/> — a scan
    /// never fails, because refusing to start a copy over a folder you cannot list helps nobody.
    /// </summary>
    public static Task<TransferScan> ScanAsync(IReadOnlyList<string> sources, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var tally = new ScanTally();

            foreach (var source in sources)
            {
                ct.ThrowIfCancellationRequested();

                if (Directory.Exists(source)) Measure(source, tally, ct);
                else if (File.Exists(source)) tally.AddFile(LongPath.Prefix(source));
            }

            return tally.ToScan();
        }, ct);

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

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private TimeSpan? _lastReportAt;
        private long     _lastReportBytes;
        private double   _bytesPerSecond;

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

            foreach (var item in request.Items)
            {
                if (Stopping) break;

                switch (request.Kind)
                {
                    case TransferKind.Copy:   await CopyItemAsync(item, forMove: false); break;
                    case TransferKind.Move:   await MoveItemAsync(item);                 break;
                    case TransferKind.Delete: DeleteItem(item.Source);                   break;
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
                PartialDestinations:  _partials);
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

            await CopyItemAsync(new TransferItem(item.Source, destination), forMove: true, alreadyResolved: true);
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
            else       await CopyFileAsync(item.Source, destination, forMove, resolved: true);
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

                try
                {
                    await CopyBytesAsync(source, partial);
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

                    CopyFileMetadata(source, partial);
                    File.Move(LongPath.Prefix(partial), LongPath.Prefix(destination), overwrite: true);
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
        /// Streams one file, returning the bytes written. A failed attempt rewinds the running total
        /// before it propagates, so retrying the file cannot count its bytes twice.
        /// </summary>
        private async Task<long> CopyBytesAsync(string source, string partial)
        {
            long written = 0;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            long before = _bytesDone;

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
                    written    += read;
                    _bytesDone += read;
                    Report(TransferPhase.Running);
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

            return written;
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
        /// </summary>
        private void Report(TransferPhase phase, bool force = false, PauseReason? paused = null)
        {
            if (progress is null) return;

            TimeSpan now = _clock.Elapsed;
            if (!force && _lastReportAt is { } gate && now - gate < ReportInterval) return;

            if (_lastReportAt is { } previous)
            {
                double seconds = (now - previous).TotalSeconds;
                if (seconds > 0)
                {
                    double sample = (_bytesDone - _lastReportBytes) / seconds;
                    _bytesPerSecond = _bytesPerSecond <= 0 ? sample : (_bytesPerSecond * 0.7) + (sample * 0.3);
                }
            }

            _lastReportAt    = now;
            _lastReportBytes = _bytesDone;

            long total = BytesTotal;
            TimeSpan? left = _bytesPerSecond > 1 && total > _bytesDone
                ? TimeSpan.FromSeconds(Math.Min((total - _bytesDone) / _bytesPerSecond, TimeSpan.MaxValue.TotalSeconds - 1))
                : null;

            progress.Report(new TransferProgress(
                phase, _bytesDone, total, _itemsDone, ItemsTotal,
                _currentItem is null ? null : Name(_currentItem),
                (long)Math.Max(0, _bytesPerSecond), left, paused));
        }
    }
}
