using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.Diagnostics;

namespace Nexaflow.Features.WinFileSystem.FileActions
{
    /// <summary>
    /// Installs a Windows Installer package (.msi) or an MSIX / MSIXBUNDLE
    /// by handing the file off to the shell, which launches the appropriate
    /// installer UI (msiexec for .msi, AppInstaller for .msix/.msixbundle).
    /// </summary>
    public class InstallPackage : IFileAction
    {
        public bool   IsDestructive        => false;
        public bool   SupportsMultipleFiles => false;
        public string Icon                  => "📦";
        public string DisplayName           => "Install";
        public string SupportedFileTypes    => "*.msi;*.msix;*.msixbundle";
        public bool   AppliesToFolders      => false;
        public string SupportedFolderNames  => "";
        public bool   AppliesToRoot         => false;
        public bool   AppliesToDrives       => false;
        public bool   RequiresRefresh       => false;
        public bool   CanPerformAction      => true;

        public bool PerformAction(string filePath)
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            return true;
        }

        public bool PerformAction(IEnumerable<string> filePaths)
        {
            foreach (var p in filePaths) return PerformAction(p);
            return false;
        }
    }
}
