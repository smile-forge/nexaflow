using System;
using System.IO;

namespace Nexaflow.Features.WindowsFileSystem
{
    /// <summary>
    /// Translates the Win32/CLR faults that surface when copying or moving a file or folder into a
    /// short, specific, user-facing sentence — so the shell can tell the user <em>why</em> an
    /// operation failed (file in use, no permission, disk full, …) rather than a generic error.
    /// </summary>
    internal static class FileOperationErrors
    {
        // Win32 error codes — the low word of an IOException / UnauthorizedAccessException HRESULT.
        private const int ERROR_FILE_NOT_FOUND       = 2;
        private const int ERROR_PATH_NOT_FOUND       = 3;
        private const int ERROR_ACCESS_DENIED        = 5;
        private const int ERROR_NOT_SAME_DEVICE      = 17;
        private const int ERROR_WRITE_PROTECT        = 19;
        private const int ERROR_SHARING_VIOLATION    = 32;
        private const int ERROR_LOCK_VIOLATION       = 33;
        private const int ERROR_HANDLE_DISK_FULL     = 39;
        private const int ERROR_FILE_EXISTS          = 80;
        private const int ERROR_DISK_FULL            = 112;
        private const int ERROR_INVALID_NAME         = 123;
        private const int ERROR_DIR_NOT_EMPTY        = 145;
        private const int ERROR_ALREADY_EXISTS       = 183;
        private const int ERROR_FILENAME_EXCED_RANGE = 206;

        internal const int NotSameDevice = ERROR_NOT_SAME_DEVICE;

        /// <summary>
        /// Returns the Win32 code carried by <paramref name="ex"/> (mapping a few CLR exception
        /// types whose HRESULT is unreliable to their equivalent code), or 0 when unknown.
        /// </summary>
        internal static int CodeOf(Exception ex) => ex switch
        {
            PathTooLongException       => ERROR_FILENAME_EXCED_RANGE,
            FileNotFoundException      => ERROR_FILE_NOT_FOUND,
            DirectoryNotFoundException => ERROR_PATH_NOT_FOUND,
            _                          => ex.HResult & 0xFFFF,
        };

        /// <summary>
        /// Builds a friendly explanation for <paramref name="ex"/> raised while
        /// <paramref name="verb"/>-ing (e.g. "move" / "copy") <paramref name="source"/> into the
        /// folder <paramref name="destFolder"/>.
        /// </summary>
        public static string Describe(string verb, string source, string destFolder, Exception ex)
        {
            string name = FriendlyName(source);
            string dest = string.IsNullOrEmpty(destFolder)
                ? "the destination"
                : $"\"{FriendlyName(destFolder)}\"";

            return CodeOf(ex) switch
            {
                ERROR_SHARING_VIOLATION =>
                    $"Can't {verb} \"{name}\" because it's open in another program. Close it and try again.",

                ERROR_LOCK_VIOLATION =>
                    $"Can't {verb} \"{name}\" because part of it is locked by another program.",

                ERROR_ACCESS_DENIED =>
                    $"Can't {verb} \"{name}\" — access denied. You may not have permission to write to {dest}, " +
                    "the file may be read-only, or it needs administrator rights.",

                ERROR_WRITE_PROTECT =>
                    $"Can't {verb} \"{name}\" — {dest} is write-protected.",

                ERROR_DISK_FULL or ERROR_HANDLE_DISK_FULL =>
                    $"There isn't enough free space in {dest} to {verb} \"{name}\".",

                ERROR_FILE_EXISTS or ERROR_ALREADY_EXISTS =>
                    $"\"{name}\" already exists in {dest}.",

                ERROR_NOT_SAME_DEVICE =>
                    $"Can't {verb} \"{name}\" to {dest} because it's on a different drive.",

                ERROR_FILENAME_EXCED_RANGE =>
                    $"Can't {verb} \"{name}\" — the resulting path in {dest} is too long.",

                ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND =>
                    $"\"{name}\" no longer exists — it may have been moved or deleted.",

                ERROR_INVALID_NAME =>
                    $"Can't {verb} \"{name}\" — {dest} won't accept that name.",

                ERROR_DIR_NOT_EMPTY =>
                    $"Can't {verb} \"{name}\" because a folder with that name already exists in {dest} and isn't empty.",

                // Fall back to the CLR message, which is still more specific than "something went wrong".
                _ => $"Couldn't {verb} \"{name}\": {ex.Message}",
            };
        }

        /// <summary>Last path segment (file or folder name), or the whole path when it has none.</summary>
        private static string FriendlyName(string path)
        {
            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name    = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? path : name;
        }
    }
}
