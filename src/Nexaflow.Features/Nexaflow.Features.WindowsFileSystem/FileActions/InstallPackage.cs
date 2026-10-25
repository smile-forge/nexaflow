using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.FileActions
{
    /// <summary>
    /// Installs a Windows Installer package (.msi) or an MSIX / MSIXBUNDLE
    /// by handing the file off to the shell, which launches the appropriate
    /// installer UI (msiexec for .msi, AppInstaller for .msix/.msixbundle).
    /// </summary>
    public class InstallPackage : IFileAction, ICacheable
    {
        private readonly IVirtualFileSystem _vfs;

        public InstallPackage(IVirtualFileSystem vfs) => _vfs = vfs;
        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => false;
        public string Icon                   => "📦";
        public string DisplayName            => Str.Get("WindowsFileSystem.Actions.Install");
        public static string? StaticExperienceId => "/binary/installer";
        public string ExperienceId           => "/binary/installer";
        public string ExperienceDescription  => Str.Get("WindowsFileSystem.Experiences.Installers");
        public bool   RequiresRefresh        => false;
        public bool   CanPerformAction       => true;

        /// <summary>An installer routinely reads a payload sitting next to it (an .msi's cab files, a
        /// bundle's members). Pulled out of an archive on its own it would fail partway through — worse
        /// than not offering the action.</summary>
        public bool   RequiresFullyBackedPath => true;

        public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
        {
            var real = _vfs.TryResolveReal(filePath) ?? filePath;
            Process.Start(new ProcessStartInfo(real) { UseShellExecute = true });
            return Task.FromResult(true);
        }

        public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
        {
            foreach (var p in filePaths) return PerformActionAsync(p, ct);
            return Task.FromResult(false);
        }
    }
}
