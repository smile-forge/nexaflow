using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.VirtualDisk.Services;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.VirtualDisk.Handlers;

/// <summary>
/// Registers virtual-disk images (VHD/VHDX/VDI/VMDK/DMG/IMG/ISO) as browsable VFS containers, so the file
/// explorer navigates <i>into</i> a disk like a folder. Read-only (<see cref="ArchiveCapabilities.List"/> +
/// <see cref="ArchiveCapabilities.Extract"/>). The session reports <see cref="IArchiveSession.SupportsLazyBrowse"/>
/// so the VFS lists one directory at a time via DiscUtils rather than walking the whole volume.
/// <para>
/// Discovered by reflection and registered into <see cref="VirtualFileSystem.Instance"/> by Core's
/// <c>FeatureManager</c> — hence the required public parameterless ctor and stateless design (one instance
/// serves every disk image).
/// </para>
/// </summary>
public sealed class DiskImageArchiveHandler : IArchiveHandler
{
    public string Name => "Disk image";

    public ArchiveCapabilities Capabilities => ArchiveCapabilities.List | ArchiveCapabilities.Extract;

    public bool CanHandle(string fileName) => DiskImageReader.CanRead(fileName);

    public IArchiveSession Open(Stream container, string fileName, ArchiveOpenOptions? options = null)
        => new DiskImageSession(new DiskImageReader(container, fileName));

    /// <summary>A lazy-browsing session over one opened disk image. Directory listings and stats are answered
    /// per-path (<see cref="ListChildren"/>/<see cref="StatEntry"/>); <see cref="Entries"/> — a full recursive
    /// walk — is built once, lazily, and reached only by an explicit whole-image extract.</summary>
    private sealed class DiskImageSession(DiskImageReader reader) : IArchiveSession
    {
        private IReadOnlyList<VirtualEntry>? _entries;

        public bool SupportsLazyBrowse => true;

        public IReadOnlyList<VirtualEntry> Entries => _entries ??= reader.EnumerateAll();

        public IReadOnlyList<VirtualEntry> ListChildren(string dirPath) => reader.ListChildren(dirPath);

        public VirtualEntry? StatEntry(string entryPath) => reader.StatEntry(entryPath);

        public Stream OpenEntry(string entryPath) => reader.OpenFile(entryPath);

        public void Dispose() => reader.Dispose();
    }
}
