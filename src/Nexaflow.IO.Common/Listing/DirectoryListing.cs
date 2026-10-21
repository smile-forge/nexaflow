using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Nexaflow.IO.Common.Cloud;

namespace Nexaflow.IO.Common.Listing;

/// <summary>One entry of a directory, as its listing gave it — with the cloud sync state the listing carries for free.</summary>
public readonly record struct ListedEntry(string Name, FileAttributes Attributes, long Size, DateTime LastWriteTime, CloudFileState CloudState)
{
    public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;
}

/// <summary>
/// A directory's entries read straight from the file system (<c>FindFirstFileExW</c>, basic information, large fetches).
/// Unlike <see cref="DirectoryInfo"/> it keeps each entry's reparse tag, which is where a cloud sync provider's placeholder
/// state lives, so a synced folder's listing names every file's sync state in the same pass that names the file.
/// </summary>
public static class DirectoryListing
{
    /// <summary>
    /// The entries of <paramref name="directory"/>, lazily. Opening it throws as <see cref="DirectoryInfo"/> would — not
    /// found, access denied — and a failure part way through ends the listing with what was read.
    /// </summary>
    public static IEnumerable<ListedEntry> Enumerate(string directory) => new Listing(directory);

    private sealed class Listing(string directory) : IEnumerable<ListedEntry>
    {
        public IEnumerator<ListedEntry> GetEnumerator() => new Reader(directory);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class Reader(string directory) : IEnumerator<ListedEntry>
    {
        private SafeFindHandle? _handle;
        private bool _finished;

        public ListedEntry Current { get; private set; }
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (!_finished)
            {
                FindData data;
                bool found;
                using (CloudPlaceholders.ExposeOnThisThread())
                {
                    if (_handle is null)
                    {
                        _handle = FindFirstFileExW(Path.Combine(directory, "*"), FindExInfoBasic, out data, FindExSearchNameMatch,
                                                   IntPtr.Zero, FindFirstExLargeFetch);
                        if (_handle.IsInvalid) throw OpenFailure(Marshal.GetLastPInvokeError());
                        found = true;
                    }
                    else found = FindNextFileW(_handle, out data);
                }

                if (!found)
                {
                    _finished = true;
                    break;
                }
                if (data.FileName is "." or "..") continue;

                Current = Read(data);
                return true;
            }
            return false;
        }

        public void Reset() => throw new NotSupportedException();

        public void Dispose()
        {
            _finished = true;
            _handle?.Dispose();
        }

        private static ListedEntry Read(in FindData data)
        {
            var attributes = (FileAttributes)data.Attributes;
            return new ListedEntry(
                data.FileName,
                attributes,
                ((long)data.SizeHigh << 32) | data.SizeLow,
                DateTime.FromFileTime(((long)data.LastWriteHigh << 32) | data.LastWriteLow),
                CloudPlaceholders.Classify(attributes, CloudPlaceholders.FlagsOf(attributes, data.ReparseTag)));
        }

        private Exception OpenFailure(int error) => error switch
        {
            ErrorFileNotFound or ErrorPathNotFound => new DirectoryNotFoundException(directory),
            ErrorAccessDenied                      => new UnauthorizedAccessException(directory),
            _                                      => new IOException(new Win32Exception(error).Message + " " + directory),
        };
    }

    private const int FindExInfoBasic       = 1;
    private const int FindExSearchNameMatch = 0;
    private const int FindFirstExLargeFetch = 2;
    private const int ErrorFileNotFound     = 2;
    private const int ErrorPathNotFound     = 3;
    private const int ErrorAccessDenied     = 5;

    /// <summary>WIN32_FIND_DATAW. The times are pairs of 32-bit halves: a FILETIME is 4-byte aligned, a long would not be.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FindData
    {
        public uint Attributes;
        public uint CreationLow, CreationHigh, LastAccessLow, LastAccessHigh, LastWriteLow, LastWriteHigh;
        public uint SizeHigh, SizeLow;
        public uint ReparseTag;
        public uint Reserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string FileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]  public string AlternateFileName;
    }

    private sealed class SafeFindHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => FindClose(handle);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFindHandle FindFirstFileExW(string fileName, int infoLevel, out FindData data, int searchOp,
                                                          IntPtr searchFilter, int flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextFileW(SafeFindHandle handle, out FindData data);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr handle);
}
