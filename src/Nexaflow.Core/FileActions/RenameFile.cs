using Nexaflow.Features.Common;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Core.FileActions
{
    public class RenameFile : IFileAction
    {
        private readonly IInputPromptService _prompt;

        public RenameFile(IInputPromptService prompt) => _prompt = prompt;

        public bool   IsDestructive         => false;
        public bool   SupportsMultipleFiles  => false;   // rename only makes sense for one item
        public string Icon                   => "✏";
        public string DisplayName            => "Rename";
        public string SupportedFileTypes     => "*.*";
        public bool   AppliesToFolders       => true;
        public string SupportedFolderNames   => "*";
        public bool   AppliesToRoot          => false;
        public bool   AppliesToDrives        => false;
        public bool   RequiresRefresh        => false;   // refresh is triggered by the confirm callback
        public bool   CanPerformAction       => true;

        public bool PerformAction(string path)
        {
            bool isDir      = Directory.Exists(path);
            string dir      = Path.GetDirectoryName(path)!;
            string oldName  = Path.GetFileName(path);
            string title    = isDir ? "Rename Folder" : "Rename File";

            _prompt.Show(
                title:        title,
                label:        "New name:",
                initialValue: oldName,
                onConfirm: newName =>
                {
                    newName = newName.Trim();
                    if (string.IsNullOrEmpty(newName) || newName == oldName) return;

                    string dest = Path.Combine(dir, newName);
                    if (isDir)
                        Directory.Move(path, dest);
                    else
                        File.Move(path, dest);

                    _prompt.RequestRefresh();
                },
                onCancel: () => { });

            return false;   // action is async — refresh handled by confirm callback
        }

        public bool PerformAction(IEnumerable<string> filePaths)
        {
            // SupportsMultipleFiles = false so this overload is never called,
            // but implement it defensively by renaming the first item.
            foreach (var p in filePaths) return PerformAction(p);
            return false;
        }
    }
}
