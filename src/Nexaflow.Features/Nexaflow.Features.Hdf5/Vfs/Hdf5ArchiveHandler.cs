using System.IO;
using Nexaflow.IO.Common;
using Nexaflow.IO.Hdf5;
using Nexaflow.IO.Hdf5.Projection;

namespace Nexaflow.Features.Hdf5.Vfs;

/// <summary>
/// Makes an HDF5 file a read-only VFS container: groups are folders and datasets are files — <c>.npy</c> when their
/// elements have a fixed size, <c>.txt</c> otherwise (<see cref="Hdf5Projection"/>). The explorer lists one group at
/// a time, and the file stays open across the VFS's per-call sessions through <see cref="Hdf5SourceCache"/>.
/// <para>
/// Discovered by reflection and registered into <see cref="VirtualFileSystem.Instance"/> by Core's
/// <c>FeatureManager</c> — hence the public parameterless constructor and no state of its own.
/// </para>
/// </summary>
public sealed class Hdf5ArchiveHandler : IArchiveHandler
{
    public string Name => "HDF5";

    public ArchiveCapabilities Capabilities => ArchiveCapabilities.List | ArchiveCapabilities.Extract;

    public bool CanHandle(string fileName) => Hdf5Location.HasHdf5Extension(fileName);

    public IArchiveSession Open(Stream container, string fileName, ArchiveOpenOptions? options = null)
    {
        var modified = container is FileStream fs && File.Exists(fs.Name) ? File.GetLastWriteTime(fs.Name) : DateTime.Now;
        try
        {
            return new Session(Hdf5SourceCache.Shared.Acquire(container), modified);
        }
        catch (Hdf5Exception ex)
        {
            throw new InvalidDataException($"{fileName} is not a readable HDF5 file: {ex.Message}", ex);
        }
    }

    private sealed class Session(Hdf5Lease lease, DateTime modified) : IArchiveSession
    {
        /// <summary>
        /// Links can make a group its own descendant, and the reader exposes no object identity to recognise the
        /// cycle by, so the whole-file walk behind extract-all stops descending at this depth.
        /// </summary>
        private const int MaxWalkDepth = 64;

        private IReadOnlyList<VirtualEntry>? _entries;

        private IHdf5Source Source => lease.Source;

        public bool SupportsLazyBrowse => true;

        public IReadOnlyList<VirtualEntry> Entries => _entries ??= Walk();

        public IReadOnlyList<VirtualEntry> ListChildren(string dirPath)
        {
            if (Resolve(dirPath) is not { Kind: Hdf5ObjectKind.Group } group) return [];
            return [.. Source.ListChildren(group.Path, 0, int.MaxValue).Items.Select(Entry).OfType<VirtualEntry>()];
        }

        public VirtualEntry? StatEntry(string entryPath) => Resolve(entryPath) is { } o ? Entry(o) : null;

        public Stream OpenEntry(string entryPath)
        {
            if (Resolve(entryPath) is not { Kind: Hdf5ObjectKind.Dataset } dataset)
                throw new FileNotFoundException("No dataset in this HDF5 file has that name.", entryPath);
            return Hdf5Projection.Open(Source, dataset);
        }

        public void Dispose() => lease.Dispose();

        /// <summary>A group by its own path; a dataset by its path plus the extension of its projection.</summary>
        private Hdf5Object? Resolve(string entryPath)
        {
            var path = Hdf5Path.Normalize(entryPath);
            if (path == Hdf5Path.Root) return Source.Root;
            if (Source.Stat(path) is { Kind: Hdf5ObjectKind.Group } group) return group;
            if (Hdf5Projection.DatasetPathOf(path, out var format) is { } datasetPath
                && Source.Stat(datasetPath) is { Kind: Hdf5ObjectKind.Dataset, Dataset: { } info } dataset
                && Hdf5Projection.FormatOf(info.Type) == format)
                return dataset;
            return null;
        }

        /// <summary>A group is a folder and a dataset a file; named datatypes and unresolved links hold nothing to read.</summary>
        private VirtualEntry? Entry(Hdf5Object o) => o.Kind switch
        {
            Hdf5ObjectKind.Group => new VirtualEntry(o.Path == Hdf5Path.Root ? string.Empty : o.Name, true, 0, 0, modified),
            Hdf5ObjectKind.Dataset when o.Dataset is { } info => new VirtualEntry(
                Hdf5Projection.EntryName(o.Name, info.Type), false, LengthOf(o), LengthOf(o), modified),
            _ => null,
        };

        /// <summary>
        /// A dataset whose text cannot be produced (an unavailable filter, say) still lists — at no length — so it is
        /// visible; opening it reports why.
        /// </summary>
        private long LengthOf(Hdf5Object dataset)
        {
            try { return Hdf5Projection.Length(Source, dataset); }
            catch (Hdf5Exception) { return 0; }
        }

        private IReadOnlyList<VirtualEntry> Walk()
        {
            var all = new List<VirtualEntry>();
            Visit(Hdf5Path.Root, string.Empty, 0);
            return all;

            void Visit(string group, string prefix, int depth)
            {
                if (depth > MaxWalkDepth) return;
                foreach (var o in Source.ListChildren(group, 0, int.MaxValue).Items)
                {
                    if (Entry(o) is not { } e) continue;
                    var name = prefix + e.Name;
                    all.Add(new VirtualEntry(name, e.IsDirectory, e.Size, e.CompressedSize, e.Modified));
                    if (o.Kind == Hdf5ObjectKind.Group) Visit(o.Path, name + "/", depth + 1);
                }
            }
        }
    }
}
