using PureHDF;
using PureHDF.Selections;
using PureHDF.VOL.Native;

namespace Nexaflow.IO.Hdf5.PureHdf;

/// <summary>
/// <see cref="IHdf5Source"/> over PureHDF. PureHDF reads through one stream position, so every call holds the
/// source's lock for its duration; windowed reads keep each hold short.
/// </summary>
internal sealed class PureHdfSource(NativeFile file) : IHdf5Source
{
    private readonly object _gate = new();
    private bool _disposed;

    public Hdf5Object Root { get; } = new(Hdf5Path.Root, Hdf5Path.Root, Hdf5ObjectKind.Group);

    public Hdf5Listing ListChildren(string groupPath, int skip, int take, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegative(take);
        var path = Hdf5Path.Normalize(groupPath);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Get(path) is not IH5Group group) throw new Hdf5Exception($"{path} is not a group.");

            var items = new List<Hdf5Object>();
            int index = 0;
            bool hasMore = false;
            string? problem = null;
            try
            {
                // PureHDF decodes each member's header as it enumerates, and one it cannot decode ends the
                // enumeration — the members before it are kept and the reason travels with them.
                foreach (var child in group.Children())
                {
                    ct.ThrowIfCancellationRequested();
                    if (index++ < skip) continue;
                    if (items.Count == take) { hasMore = true; break; }
                    items.Add(Describe(Hdf5Path.Combine(path, child.Name), child));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                problem = ex.Message;
            }
            return new Hdf5Listing(items, hasMore, problem);
        }
    }

    public Hdf5Object? Stat(string path)
    {
        var p = Hdf5Path.Normalize(path);
        if (p == Hdf5Path.Root) return Root;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                if (!file.LinkExists(p)) return null;
                return Describe(p, file.Get(p));
            }
            catch (Exception ex)
            {
                return new Hdf5Object(p, Hdf5Path.NameOf(p), Hdf5ObjectKind.Unreadable) { Problem = ex.Message };
            }
        }
    }

    public IReadOnlyList<Hdf5Attribute> Attributes(string path, CancellationToken ct = default)
    {
        var p = Hdf5Path.Normalize(path);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var owner = Get(p);
            if (owner is IH5UnresolvedLink) return [];

            List<IH5Attribute> attributes;
            try { attributes = [.. owner.Attributes()]; }
            catch (Exception ex) { throw new Hdf5Exception(ex.Message, ex); }

            var result = new List<Hdf5Attribute>(attributes.Count);
            foreach (var a in attributes)
            {
                ct.ThrowIfCancellationRequested();
                result.Add(ReadAttribute(a));
            }
            return result;
        }
    }

    public Hdf5Block ReadBlock(string datasetPath, Hdf5Selection selection, CancellationToken ct = default)
    {
        var p = Hdf5Path.Normalize(datasetPath);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ct.ThrowIfCancellationRequested();
            if (Get(p) is not IH5Dataset dataset) throw new Hdf5Exception($"{p} is not a dataset.");

            Hdf5Type type;
            Hdf5Dataspace space;
            try
            {
                type  = PureHdfTypes.Map(dataset.Type);
                space = PureHdfTypes.Map(dataset.Space);
            }
            catch (Exception ex) { throw new Hdf5Exception(ex.Message, ex); }

            selection.Validate(space);
            if (space.Kind == Hdf5SpaceKind.Null || selection.ElementCount == 0)
                return Hdf5Block.Empty(type, selection.Count);

            Selection? fileSelection = space.Kind == Hdf5SpaceKind.Scalar
                ? null
                : new HyperslabSelection(selection.Rank, [.. selection.Start], [.. selection.Count]);
            try
            {
                var data = PureHdfElements.Read(new DatasetElements(dataset, fileSelection), type, ResolveReference);
                return new Hdf5Block(type, selection.Count, data);
            }
            catch (Exception ex) { throw new Hdf5Exception(ex.Message, ex); }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            file.Dispose();
        }
    }

    private IH5Object Get(string path)
    {
        try { return path == Hdf5Path.Root ? file : file.Get(path); }
        catch (Exception ex) { throw new Hdf5Exception(ex.Message, ex); }
    }

    private string ResolveReference(NativeObjectReference1 reference)
    {
        try { return file.Get(reference).Name; }
        catch (Exception ex) { return ex.Message; }
    }

    private static Hdf5Object Describe(string path, IH5Object o)
    {
        var name = Hdf5Path.NameOf(path);
        try
        {
            return o switch
            {
                IH5UnresolvedLink u => new Hdf5Object(path, name, Hdf5ObjectKind.UnresolvedLink) { Problem = u.Reason?.Message },
                IH5Dataset d        => new Hdf5Object(path, name, Hdf5ObjectKind.Dataset) { Dataset = DescribeDataset(d) },
                IH5Group            => new Hdf5Object(path, name, Hdf5ObjectKind.Group),
                IH5CommitedDatatype => new Hdf5Object(path, name, Hdf5ObjectKind.NamedDatatype),
                _                   => new Hdf5Object(path, name, Hdf5ObjectKind.Unreadable) { Problem = o.GetType().Name },
            };
        }
        catch (Exception ex)
        {
            return new Hdf5Object(path, name, Hdf5ObjectKind.Unreadable) { Problem = ex.Message };
        }
    }

    private static Hdf5DatasetInfo DescribeDataset(IH5Dataset d)
    {
        var type   = PureHdfTypes.Map(d.Type);
        var space  = PureHdfTypes.Map(d.Space);
        var layout = PureHdfTypes.Map(d.Layout.Class);
        // PureHDF only answers Chunks for a chunked layout.
        IReadOnlyList<ulong> chunks = layout == Hdf5Layout.Chunked ? d.Layout.Chunks : [];
        var fill = d.FillValue.Value is { } raw && type.IsFixedSize && raw.Length == type.Size
            ? Hdf5ValueCodec.FormatRaw(raw, type)
            : null;
        return new Hdf5DatasetInfo(type, space, layout, chunks, fill);
    }

    private Hdf5Attribute ReadAttribute(IH5Attribute a)
    {
        Hdf5Type? type = null;
        Hdf5Dataspace? space = null;
        try
        {
            type  = PureHdfTypes.Map(a.Type);
            space = PureHdfTypes.Map(a.Space);
            if (space.Kind == Hdf5SpaceKind.Null) return new Hdf5Attribute(a.Name, type, space, string.Empty, false);

            var data  = PureHdfElements.Read(new AttributeElements(a), type, ResolveReference);
            var block = new Hdf5Block(type, space.Kind == Hdf5SpaceKind.Scalar ? [] : space.Shape, data);
            if (space.Kind == Hdf5SpaceKind.Scalar) return new Hdf5Attribute(a.Name, type, space, block.Format(0), false);

            long shown = Math.Min(block.Count, Hdf5Attribute.PreviewElements);
            var values = new string[shown];
            for (long i = 0; i < shown; i++) values[i] = block.Format(i);
            return new Hdf5Attribute(a.Name, type, space, "[" + string.Join(", ", values) + "]", block.Count > shown);
        }
        catch (Exception ex)
        {
            return new Hdf5Attribute(a.Name, type, space, string.Empty, false) { Problem = ex.Message };
        }
    }
}
