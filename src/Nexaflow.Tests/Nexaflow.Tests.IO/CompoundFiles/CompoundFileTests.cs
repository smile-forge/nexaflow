using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

using Nexaflow.IO.CompoundFile;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.CompoundFiles;

/// <summary>
/// A compound file, read and written: what is written reads back, Windows' own structured storage reads it too, what
/// Windows writes — in both sector sizes — is read, and a damaged file is refused rather than followed.
/// </summary>
[TestClass]
[CoversNode("compound-file")]
public class CompoundFileTests
{
    private static readonly int[] Sizes = [0, 1, 63, 64, 65, 4095, 4096, 10_000];

    [TestMethod]
    public void WhatIsWrittenReadsBack()
    {
        var root = new CompoundFolder("Root Entry");
        var inner = new CompoundFolder("inner") { Class = Guid.Parse("00020D0B-0000-0000-C000-000000000046") };

        foreach (var size in Sizes) root.Add($"stream {size}", Bytes(size, seed: size));
        inner.Add("deeper", Bytes(5000, seed: 7));
        inner.Add(new CompoundFolder("empty"));
        root.Add(inner);

        var file = Open(CompoundFileWriter.Write(root));

        foreach (var size in Sizes)
            CollectionAssert.AreEqual(Bytes(size, seed: size), file.Root.Stream($"stream {size}")!.Read(), $"{size} bytes");

        var read = file.Root.Storage("inner")!;
        Assert.AreEqual(inner.Class, read.Class);
        CollectionAssert.AreEqual(Bytes(5000, seed: 7), read.Stream("deeper")!.Read());
        Assert.AreEqual(0, read.Storage("empty")!.Streams.Count);
    }

    [TestMethod]
    public void ANameIsFoundWhateverItsCase()
    {
        var root = new CompoundFolder("Root Entry");
        root.Add("__substg1.0_0037001F", [1, 2, 3]);

        Assert.IsNotNull(Open(CompoundFileWriter.Write(root)).Root.Stream("__SUBSTG1.0_0037001f"));
    }

    [TestMethod]
    public void EntriesComeBackInTheOrderTheFormatSortsThem()
    {
        var root = new CompoundFolder("Root Entry");
        foreach (var name in new[] { "bb", "a", "C", "aaa", "B", "d" }) root.Add(name, [0]);

        var names = Open(CompoundFileWriter.Write(root)).Root.Streams.Select(stream => stream.Name).ToArray();

        CollectionAssert.AreEqual(new[] { "a", "B", "C", "d", "bb", "aaa" }, names, "shorter first, then by capitals");
    }

    [TestMethod]
    public void ManyEntriesInOneStorageAreAllFound()
    {
        var root = new CompoundFolder("Root Entry");
        for (var at = 0; at < 300; at++) root.Add($"s{at:D4}", [(byte)at]);

        var file = Open(CompoundFileWriter.Write(root));

        Assert.AreEqual(300, file.Root.Streams.Count);
        for (var at = 0; at < 300; at++) Assert.AreEqual((byte)at, file.Root.Stream($"s{at:D4}")!.Read()[0]);
    }

    [TestMethod]
    public void AFileLargeEnoughToNeedADifatReadsBack()
    {
        // 109 allocation sectors map 109 × 128 sectors of 512 bytes, just under seven megabytes.
        var big = Bytes(8 * 1024 * 1024, seed: 3);
        var root = new CompoundFolder("Root Entry");
        root.Add("big", big);

        var written = CompoundFileWriter.Write(root);

        Assert.AreNotEqual(0u, BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(72)), "it has DIFAT sectors");
        CollectionAssert.AreEqual(big, Open(written).Root.Stream("big")!.Read());
    }

    // ── Windows agrees ──────────────────────────────────────────────────────

    [TestMethod]
    public void WindowsReadsWhatIsWritten()
    {
        var root = new CompoundFolder("Root Entry");
        root.Add("small", Bytes(100, seed: 1));
        root.Add("large", Bytes(9000, seed: 2));

        var inner = new CompoundFolder("inner");
        inner.Add("deeper", Bytes(10, seed: 3));
        root.Add(inner);

        var path = Scratch(CompoundFileWriter.Write(root));

        try
        {
            var storage = Native.Opened(path);

            try
            {
                CollectionAssert.AreEqual(Bytes(100, seed: 1), Native.Read(storage, "small"));
                CollectionAssert.AreEqual(Bytes(9000, seed: 2), Native.Read(storage, "large"));

                storage.OpenStorage("inner", null, Native.Reading | Native.ShareExclusive, IntPtr.Zero, 0, out var opened);
                try
                {
                    CollectionAssert.AreEqual(Bytes(10, seed: 3), Native.Read(opened, "deeper"));
                }
                finally
                {
                    Marshal.ReleaseComObject(opened);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(storage);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void WhatWindowsWritesIsRead_InEitherSectorSize()
    {
        foreach (var sector in new[] { 512, 4096 })
        {
            var path = Path.Combine(TestTemp.Root, $"nexaflow-cfb-{Guid.NewGuid():N}.stg");

            try
            {
                Native.Written(path, sector, storage =>
                {
                    Native.Write(storage, "small", Bytes(100, seed: 1));
                    Native.Write(storage, "large", Bytes(20_000, seed: 2));

                    storage.CreateStorage("inner", Native.ReadWrite | Native.ShareExclusive | Native.Create, 0, 0, out var inner);
                    Native.Write(inner, "deeper", Bytes(10, seed: 3));
                    inner.Commit(0);
                    Marshal.ReleaseComObject(inner);
                });

                using var stream = File.OpenRead(path);
                var file = CompoundFile.Open(stream);

                Assert.AreEqual(sector == 512 ? 3 : 4, BinaryPrimitives.ReadUInt16LittleEndian(File.ReadAllBytes(path).AsSpan(26)), "the version written");
                CollectionAssert.AreEqual(Bytes(100, seed: 1), file.Root.Stream("small")!.Read(), $"{sector}-byte sectors");
                CollectionAssert.AreEqual(Bytes(20_000, seed: 2), file.Root.Stream("large")!.Read(), $"{sector}-byte sectors");
                CollectionAssert.AreEqual(Bytes(10, seed: 3), file.Root.Storage("inner")!.Stream("deeper")!.Read(), $"{sector}-byte sectors");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    // ── A damaged file is refused ───────────────────────────────────────────

    [TestMethod]
    public void WhatIsNotACompoundFileIsRefused()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Open(new byte[1024]));
        Assert.ThrowsExactly<InvalidDataException>(() => Open([0xD0, 0xCF]));
    }

    [TestMethod]
    public void AChainRunningInACircleIsRefused()
    {
        var written = OneLargeStream();

        // The stream's chain starts at sector 2, after the allocation table and the directory; point it back at itself.
        BinaryPrimitives.WriteUInt32LittleEndian(written.AsSpan(512 + (2 * 4)), 2);

        var file = Open(written);
        Assert.ThrowsExactly<InvalidDataException>(() => file.Root.Stream("large")!.Read());
    }

    [TestMethod]
    public void AChainLeavingTheFileIsRefused()
    {
        var written = OneLargeStream();
        BinaryPrimitives.WriteUInt32LittleEndian(written.AsSpan(512 + (2 * 4)), 5_000_000);

        var file = Open(written);
        Assert.ThrowsExactly<InvalidDataException>(() => file.Root.Stream("large")!.Read());
    }

    [TestMethod]
    public void ACutShortFileIsRefused()
    {
        var written = OneLargeStream();
        var cut = written[..(written.Length - 2048)];

        // Refused as soon as it is opened: the stream claims more than what is left of the file could hold.
        Assert.ThrowsExactly<InvalidDataException>(() => Open(cut).Root.Stream("large")!.Read());
    }

    [TestMethod]
    public void ADirectoryNamingAnEntryItDoesNotHaveIsRefused()
    {
        var written = OneLargeStream();

        // The root's child, in the first directory entry of sector 1.
        BinaryPrimitives.WriteUInt32LittleEndian(written.AsSpan(1024 + 76), 99);

        Assert.ThrowsExactly<InvalidDataException>(() => Open(written));
    }

    [TestMethod]
    public void AStreamClaimingMoreThanTheFileHoldsIsRefused()
    {
        var written = OneLargeStream();

        // The stream's size, in the second directory entry.
        BinaryPrimitives.WriteUInt64LittleEndian(written.AsSpan(1024 + 128 + 120), 1L << 40);

        Assert.ThrowsExactly<InvalidDataException>(() => Open(written));
    }

    // ── Making and reading files ────────────────────────────────────────────

    private static CompoundFile Open(byte[] bytes) => CompoundFile.Open(new MemoryStream(bytes));

    private static byte[] OneLargeStream()
    {
        var root = new CompoundFolder("Root Entry");
        root.Add("large", Bytes(5000, seed: 9));
        return CompoundFileWriter.Write(root);
    }

    private static byte[] Bytes(int size, int seed)
    {
        var bytes = new byte[size];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static string Scratch(byte[] bytes)
    {
        var path = Path.Combine(TestTemp.Root, $"nexaflow-cfb-{Guid.NewGuid():N}.stg");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Windows' own structured storage (ole32), the reference every compound file is read by.</summary>
    private static class Native
    {
        public const int Reading = 0x0;
        public const int Writing = 0x1;
        public const int ReadWrite = 0x2;
        public const int ShareExclusive = 0x10;
        public const int Create = 0x1000;

        private const int DocFile = 5;

        private static readonly Guid StorageInterface = new("0000000B-0000-0000-C000-000000000046");

        public static IStorage Opened(string path)
        {
            Marshal.ThrowExceptionForHR(StgOpenStorage(path, null, Reading | ShareExclusive, IntPtr.Zero, 0, out var storage));
            return storage;
        }

        public static void Written(string path, int sectorSize, Action<IStorage> fill)
        {
            var options = new StorageOptions { Version = 2, SectorSize = (uint)sectorSize };
            var iid = StorageInterface;

            Marshal.ThrowExceptionForHR(StgCreateStorageEx(path, ReadWrite | ShareExclusive | Create, DocFile, 0, ref options,
                                                           IntPtr.Zero, ref iid, out var made));
            var storage = (IStorage)made;

            try
            {
                fill(storage);
                storage.Commit(0);
            }
            finally
            {
                Marshal.ReleaseComObject(storage);
            }
        }

        public static byte[] Read(IStorage storage, string name)
        {
            storage.OpenStream(name, IntPtr.Zero, Native.Reading | ShareExclusive, 0, out var stream);

            try
            {
                stream.Stat(out var stat, 1);
                var bytes = new byte[stat.cbSize];
                stream.Read(bytes, bytes.Length, IntPtr.Zero);
                return bytes;
            }
            finally
            {
                Marshal.ReleaseComObject(stream);
            }
        }

        public static void Write(IStorage storage, string name, byte[] bytes)
        {
            storage.CreateStream(name, Native.Writing | ShareExclusive | Create, 0, 0, out var stream);

            try
            {
                stream.Write(bytes, bytes.Length, IntPtr.Zero);
                stream.Commit(0);
            }
            finally
            {
                Marshal.ReleaseComObject(stream);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StorageOptions
        {
            public ushort Version;
            public ushort Reserved;
            public uint SectorSize;
            public IntPtr TemplateFile;
        }

        [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
        private static extern int StgOpenStorage(string name, IStorage? priority, int mode, IntPtr exclude, int reserved, out IStorage storage);

        [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
        private static extern int StgCreateStorageEx(string name, int mode, int format, int attributes, ref StorageOptions options,
                                                     IntPtr securityDescriptor, ref Guid iid,
                                                     [MarshalAs(UnmanagedType.IUnknown)] out object storage);
    }

    [ComImport]
    [Guid("0000000B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IStorage
    {
        void CreateStream([MarshalAs(UnmanagedType.LPWStr)] string name, int mode, int reserved1, int reserved2, out IStream stream);
        void OpenStream([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr reserved1, int mode, int reserved2, out IStream stream);
        void CreateStorage([MarshalAs(UnmanagedType.LPWStr)] string name, int mode, int reserved1, int reserved2, out IStorage storage);
        void OpenStorage([MarshalAs(UnmanagedType.LPWStr)] string name, IStorage? priority, int mode, IntPtr exclude, int reserved, out IStorage storage);
        void CopyTo(int exclude, IntPtr excluded, IntPtr snbExclude, IStorage destination);
        void MoveElementTo([MarshalAs(UnmanagedType.LPWStr)] string name, IStorage destination, [MarshalAs(UnmanagedType.LPWStr)] string newName, int flags);
        void Commit(int flags);
        void Revert();
        void EnumElements(int reserved1, IntPtr reserved2, int reserved3, out IntPtr enumerator);
        void DestroyElement([MarshalAs(UnmanagedType.LPWStr)] string name);
        void RenameElement([MarshalAs(UnmanagedType.LPWStr)] string oldName, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void SetElementTimes([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr created, IntPtr accessed, IntPtr modified);
        void SetClass(ref Guid clsid);
        void SetStateBits(int bits, int mask);
        void Stat(out STATSTG stat, int flags);
    }
}
