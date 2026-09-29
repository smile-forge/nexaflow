using PureHDF;

namespace Nexaflow.IO.Hdf5.PureHdf;

/// <summary>Maps PureHDF's descriptions onto the backend-neutral model.</summary>
internal static class PureHdfTypes
{
    public static Hdf5Type Map(IH5DataType t)
    {
        var cls = t.Class switch
        {
            H5DataTypeClass.FixedPoint     => Hdf5TypeClass.Integer,
            H5DataTypeClass.FloatingPoint  => Hdf5TypeClass.Float,
            H5DataTypeClass.Time           => Hdf5TypeClass.Time,
            H5DataTypeClass.String         => Hdf5TypeClass.String,
            H5DataTypeClass.BitField       => Hdf5TypeClass.BitField,
            H5DataTypeClass.Opaque         => Hdf5TypeClass.Opaque,
            H5DataTypeClass.Compound       => Hdf5TypeClass.Compound,
            H5DataTypeClass.Reference      => Hdf5TypeClass.Reference,
            H5DataTypeClass.Enumerated     => Hdf5TypeClass.Enum,
            H5DataTypeClass.VariableLength => Hdf5TypeClass.VariableLength,
            H5DataTypeClass.Array          => Hdf5TypeClass.Array,
            _ => throw new NotSupportedException($"Unknown HDF5 datatype class {t.Class}."),
        };

        return cls switch
        {
            Hdf5TypeClass.Integer        => new(cls, t.Size) { IsSigned = t.FixedPoint.IsSigned },
            Hdf5TypeClass.Opaque         => new(cls, t.Size) { Tag = t.Opaque.Tag },
            Hdf5TypeClass.Compound       => new(cls, t.Size)
            {
                Fields = [.. t.Compound.Members.Select(m => new Hdf5Field(m.Name, m.Offset, Map(m.Type)))],
            },
            Hdf5TypeClass.Enum           => EnumOf(t),
            Hdf5TypeClass.VariableLength => new(cls, t.Size) { Base = Map(t.VariableLength.BaseType) },
            Hdf5TypeClass.Array          => new(cls, t.Size) { Base = Map(t.Array.BaseType) },
            _                            => new(cls, t.Size),
        };
    }

    public static Hdf5Dataspace Map(IH5Dataspace s) => s.Type switch
    {
        H5DataspaceType.Scalar => Hdf5Dataspace.Scalar,
        H5DataspaceType.Null   => new(Hdf5SpaceKind.Null, [], []),
        _ => new(Hdf5SpaceKind.Simple, s.Dimensions,
                 [.. s.MaxDimensions.Select(m => m == ulong.MaxValue ? (ulong?)null : m)]),
    };

    public static Hdf5Layout Map(H5DataLayoutClass layout) => layout switch
    {
        H5DataLayoutClass.Compact    => Hdf5Layout.Compact,
        H5DataLayoutClass.Contiguous => Hdf5Layout.Contiguous,
        H5DataLayoutClass.Chunked    => Hdf5Layout.Chunked,
        _                            => Hdf5Layout.Virtual,
    };

    private static Hdf5Type EnumOf(IH5DataType t)
    {
        var baseType = Map(t.Enumeration.BaseType);
        var e = t.Enumeration;
        IEnumerable<KeyValuePair<string, long>> members = (baseType.Size, baseType.IsSigned) switch
        {
            (1, true)  => e.GetMembers<sbyte>().Select(kv => KeyValuePair.Create(kv.Key, (long)kv.Value)),
            (1, false) => e.GetMembers<byte>().Select(kv => KeyValuePair.Create(kv.Key, (long)kv.Value)),
            (2, true)  => e.GetMembers<short>().Select(kv => KeyValuePair.Create(kv.Key, (long)kv.Value)),
            (2, false) => e.GetMembers<ushort>().Select(kv => KeyValuePair.Create(kv.Key, (long)kv.Value)),
            (4, true)  => e.GetMembers<int>().Select(kv => KeyValuePair.Create(kv.Key, (long)kv.Value)),
            (4, false) => e.GetMembers<uint>().Select(kv => KeyValuePair.Create(kv.Key, (long)kv.Value)),
            (8, true)  => e.GetMembers<long>().Select(kv => KeyValuePair.Create(kv.Key, kv.Value)),
            (8, false) => e.GetMembers<ulong>().Select(kv => KeyValuePair.Create(kv.Key, unchecked((long)kv.Value))),
            _          => [],
        };
        return new(Hdf5TypeClass.Enum, t.Size)
        {
            Base    = baseType,
            Members = [.. members.OrderBy(kv => kv.Value).Select(kv => new Hdf5EnumMember(kv.Key, kv.Value))],
        };
    }
}
