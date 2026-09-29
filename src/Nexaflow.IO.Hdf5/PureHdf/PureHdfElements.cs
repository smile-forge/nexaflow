using System.Numerics;
using System.Runtime.ExceptionServices;
using PureHDF;
using PureHDF.Selections;
using PureHDF.VOL.Native;

namespace Nexaflow.IO.Hdf5.PureHdf;

/// <summary>Something PureHDF can decode elements from: a dataset selection or a whole attribute.</summary>
internal interface IElementSource
{
    T Read<T>();
}

internal sealed class DatasetElements(IH5Dataset dataset, Selection? selection) : IElementSource
{
    public T Read<T>() => dataset.Read<T>(selection);
}

internal sealed class AttributeElements(IH5Attribute attribute) : IElementSource
{
    public T Read<T>() => attribute.Read<T>();
}

/// <summary>
/// Decodes elements of a mapped type into block data. PureHDF only decodes into a CLR type of exactly the
/// stored element size, so each family is read as the type that matches it.
/// </summary>
internal static class PureHdfElements
{
    public static IBlockData Read(IElementSource source, Hdf5Type type, Func<NativeObjectReference1, string> resolve) =>
        type.Class switch
        {
            Hdf5TypeClass.Integer        => Numeric(source, type) ?? Raw(source, type),
            Hdf5TypeClass.Float          => Numeric(source, type) ?? Raw(source, type),
            Hdf5TypeClass.Enum           => Enum(source, type) ?? Raw(source, type),
            Hdf5TypeClass.Compound       => new CompoundBlockData(source.Read<Dictionary<string, object>[]>(), type),
            Hdf5TypeClass.VariableLength => VariableLength(source, type),
            Hdf5TypeClass.Reference when type.Size == 8 =>
                new ReferenceBlockData([.. source.Read<NativeObjectReference1[]>().Select(resolve)]),
            _                            => Raw(source, type),
        };

    private static IBlockData Raw(IElementSource source, Hdf5Type type) => new RawBlockData(source.Read<byte[]>(), type);

    private static IBlockData? Numeric(IElementSource s, Hdf5Type t) => (t.Class, t.Size, t.IsSigned) switch
    {
        (Hdf5TypeClass.Integer, 1, true)  => Of<sbyte>(s),
        (Hdf5TypeClass.Integer, 1, false) => Of<byte>(s),
        (Hdf5TypeClass.Integer, 2, true)  => Of<short>(s),
        (Hdf5TypeClass.Integer, 2, false) => Of<ushort>(s),
        (Hdf5TypeClass.Integer, 4, true)  => Of<int>(s),
        (Hdf5TypeClass.Integer, 4, false) => Of<uint>(s),
        (Hdf5TypeClass.Integer, 8, true)  => Of<long>(s),
        (Hdf5TypeClass.Integer, 8, false) => Of<ulong>(s),
        (Hdf5TypeClass.Float, 2, _)       => Of<Half>(s),
        (Hdf5TypeClass.Float, 4, _)       => Of<float>(s),
        (Hdf5TypeClass.Float, 8, _)       => Of<double>(s),
        _                                 => null,
    };

    private static NumericBlockData<T> Of<T>(IElementSource s) where T : unmanaged, INumberBase<T> => new(s.Read<T[]>());

    private static IBlockData? Enum(IElementSource s, Hdf5Type t) => t.Base is not { Class: Hdf5TypeClass.Integer } b ? null : (b.Size, b.IsSigned) switch
    {
        (1, true)  => new EnumBlockData<sbyte>(s.Read<sbyte[]>(), t),
        (1, false) => new EnumBlockData<byte>(s.Read<byte[]>(), t),
        (2, true)  => new EnumBlockData<short>(s.Read<short[]>(), t),
        (2, false) => new EnumBlockData<ushort>(s.Read<ushort[]>(), t),
        (4, true)  => new EnumBlockData<int>(s.Read<int[]>(), t),
        (4, false) => new EnumBlockData<uint>(s.Read<uint[]>(), t),
        (8, true)  => new EnumBlockData<long>(s.Read<long[]>(), t),
        (8, false) => new EnumBlockData<ulong>(s.Read<ulong[]>(), t),
        _          => null,
    };

    private static IBlockData VariableLength(IElementSource s, Hdf5Type t)
    {
        if (!t.IsVariableLengthString) return Sequence(s, t);
        try
        {
            return new TextBlockData(s.Read<string?[]>());
        }
        catch (Exception asText)
        {
            // A one-byte base is text unless the file says it is a byte sequence, which PureHDF reports only
            // by refusing the string read.
            try { return Sequence(s, t); }
            catch { ExceptionDispatchInfo.Throw(asText); throw; }
        }
    }

    private static IBlockData Sequence(IElementSource s, Hdf5Type t) => (t.Base?.Class, t.Base?.Size, t.Base?.IsSigned) switch
    {
        (Hdf5TypeClass.Integer, 1, true)  => new SequenceBlockData(s.Read<sbyte[]?[]>(), t),
        (Hdf5TypeClass.Integer, 1, false) => new SequenceBlockData(s.Read<byte[]?[]>(), t),
        (Hdf5TypeClass.Integer, 2, true)  => new SequenceBlockData(s.Read<short[]?[]>(), t),
        (Hdf5TypeClass.Integer, 2, false) => new SequenceBlockData(s.Read<ushort[]?[]>(), t),
        (Hdf5TypeClass.Integer, 4, true)  => new SequenceBlockData(s.Read<int[]?[]>(), t),
        (Hdf5TypeClass.Integer, 4, false) => new SequenceBlockData(s.Read<uint[]?[]>(), t),
        (Hdf5TypeClass.Integer, 8, true)  => new SequenceBlockData(s.Read<long[]?[]>(), t),
        (Hdf5TypeClass.Integer, 8, false) => new SequenceBlockData(s.Read<ulong[]?[]>(), t),
        (Hdf5TypeClass.Float, 4, _)       => new SequenceBlockData(s.Read<float[]?[]>(), t),
        (Hdf5TypeClass.Float, 8, _)       => new SequenceBlockData(s.Read<double[]?[]>(), t),
        _ => throw new NotSupportedException($"Variable-length sequences of {t.Base?.DisplayName} cannot be read."),
    };
}
