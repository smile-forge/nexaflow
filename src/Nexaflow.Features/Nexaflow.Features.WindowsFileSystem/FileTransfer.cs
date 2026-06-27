using System;
using System.IO;

namespace Nexaflow.Features.WindowsFileSystem
{
    /// <summary>
    /// The single place file/folder copy &amp; move work happens for the file browser (clipboard paste
    /// and drag-drop). Every fault is translated into a <see cref="FileOperationException"/> whose
    /// message names the actual offending item — so a failure deep inside a folder copy reports the
    /// child file, not just the top folder. Callers catch <see cref="FileOperationException"/> and
    /// surface the message; anything they don't catch is a genuine bug, not a user-facing condition.
    /// </summary>
    internal static class FileTransfer
    {
        public static void CopyFile(string source, string dest)
        {
            try { File.Copy(source, dest, overwrite: false); }
            catch (Exception ex) { throw Fail("copy", source, dest, ex); }
        }

        public static void MoveFile(string source, string dest)
        {
            try { File.Move(source, dest, overwrite: false); }
            catch (Exception ex) { throw Fail("move", source, dest, ex); }
        }

        public static void CopyDirectory(string source, string dest)
        {
            try { Directory.CreateDirectory(dest); }
            catch (Exception ex) { throw Fail("copy", source, dest, ex); }

            foreach (var file in Directory.GetFiles(source))
                CopyFile(file, Path.Combine(dest, Path.GetFileName(file)));
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        public static void MoveDirectory(string source, string dest)
        {
            try
            {
                Directory.Move(source, dest);
            }
            catch (FileOperationException) { throw; }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) == FileOperationErrors.NotSameDevice)
            {
                // Directory.Move can't span volumes — fall back to copy-then-delete so a cut/paste
                // (or shift-drag) of a folder across drives works like it does in Explorer.
                CopyDirectory(source, dest);
                try { Directory.Delete(source, recursive: true); }
                catch (Exception delEx) { throw Fail("move", source, dest, delEx); }
            }
            catch (Exception ex) { throw Fail("move", source, dest, ex); }
        }

        /// <summary>Wraps <paramref name="ex"/> in a friendly <see cref="FileOperationException"/>;
        /// already-friendly faults pass straight through.</summary>
        private static FileOperationException Fail(string verb, string source, string dest, Exception ex)
        {
            if (ex is FileOperationException foe) return foe;
            string destFolder = Path.GetDirectoryName(
                dest.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? dest;
            return new FileOperationException(FileOperationErrors.Describe(verb, source, destFolder, ex));
        }
    }
}
