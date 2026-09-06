using Nexaflow.Features.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Nexaflow.Features.WindowsFileSystem.FileActions
{
    /// <summary>
    /// Puts the selected item's full path on the clipboard as <b>text</b> — the answer to "where is this?"
    /// that a terminal, a chat message or another app's Open dialog can take. Distinct from
    /// <see cref="CopyFiles"/>, which puts the file itself there for a paste.
    /// <para>
    /// Offered for files, folders and drives, so the directory tree and the file list both carry it. The
    /// path copied is the one on screen: inside an archive that is the virtual path, which is what Nexaflow
    /// itself can reopen — materialising to a temp copy would name a location the user never asked about.
    /// </para>
    /// </summary>
    public class CopyPaths : IFileAction, IFolderAction, ICacheable
    {
        // ── IFileAction ───────────────────────────────────────────────────────

        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => true;
        public string Icon                   => "🏷";
        public string DisplayName            => "Copy path";
        public static string? StaticExperienceId => "/";
        public string ExperienceId           => "/";
        public string ExperienceDescription  => "All files";
        public bool   RequiresRefresh        => false;
        public bool   CanPerformAction       => true;

        // ── IFolderAction ─────────────────────────────────────────────────────

        bool   IFolderAction.IsDestructive         => false;
        bool   IFolderAction.SupportsMultipleFiles => true;
        string IFolderAction.Icon                  => "🏷";
        string IFolderAction.DisplayName           => "Copy path";
        bool   IFolderAction.RequiresRefresh       => false;
        bool   IFolderAction.CanPerformAction      => true;
        /// <summary>The breadcrumb bar already offers the open folder's own path, so an empty selection
        /// has nothing to add here — the action stands for the item you clicked.</summary>
        public bool   AppliesToRoot                => false;
        public bool   AppliesToDrives              => true;

        // ── Actions ───────────────────────────────────────────────────────────

        public bool PerformAction(string filePath) => PerformAction([filePath]);

        public bool PerformAction(IEnumerable<string> filePaths)
        {
            // An empty selection must not reach the clipboard: writing nothing REPLACES whatever the user
            // had copied, so "copy path" on nothing would silently throw their clipboard away — the same
            // trap CopyFiles fell into, reachable here through a pinned ribbon button.
            if (ClipboardText(filePaths) is not { } text) return false;

            try { Clipboard.SetText(text); return true; }
            catch { return false; }   // another process holds the clipboard open
        }

        /// <summary>
        /// The text a selection copies — one path per line — or <c>null</c> when there is nothing to copy.
        /// One line each rather than a single quoted blob: a single path pastes clean into a shell or an
        /// address bar, and a multi-selection pastes as a list you can act on line by line.
        /// </summary>
        internal static string? ClipboardText(IEnumerable<string> paths)
        {
            var lines = (paths ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
        }
    }
}
