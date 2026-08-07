using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using System.Collections.Generic;
using System.Diagnostics;

namespace Nexaflow.Features.WindowsFileSystem.FileActions
{
    /// <summary>
    /// Installs a Windows Installer package (.msi) or an MSIX / MSIXBUNDLE
    /// by handing the file off to the shell, which launches the appropriate
    /// installer UI (msiexec for .msi, AppInstaller for .msix/.msixbundle).
    /// </summary>
    public class InstallPackage : IFileAction, ICacheable
    {
        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => false;
        public string Icon                   => "📦";
        public string DisplayName            => "Install";
        public static string? StaticExperienceId => "/binary/installer";
        public string ExperienceId           => "/binary/installer";
        public string ExperienceDescription  => "Windows installer packages (.msi, .msix, .msixbundle)";
        public bool   RequiresRefresh        => false;
        public bool   CanPerformAction       => true;

        /// <summary>An installer routinely reads a payload sitting next to it (an .msi's cab files, a
        /// bundle's members). Pulled out of an archive on its own it would fail partway through — worse
        /// than not offering the action.</summary>
        public bool   RequiresFullyBackedPath => true;

        public bool PerformAction(string filePath)
        {
            var real = VirtualFileSystem.Instance.TryResolveReal(filePath) ?? filePath;
            Process.Start(new ProcessStartInfo(real) { UseShellExecute = true });
            return true;
        }

        public bool PerformAction(IEnumerable<string> filePaths)
        {
            foreach (var p in filePaths) return PerformAction(p);
            return false;
        }
    }
}
