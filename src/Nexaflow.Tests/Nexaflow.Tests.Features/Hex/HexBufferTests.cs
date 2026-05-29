using System.IO;
using Nexaflow.Features.Hex.Buffer;

namespace Nexaflow.Tests.Features.Hex;

[TestClass]
public class HexBufferTests
{
    private static string WriteTempFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hexbuf_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] ReadAllVirtual(HexBuffer buf)
    {
        var len = (int)buf.VirtualLength;
        var result = new byte[len];
        for (int i = 0; i < len; i++) result[i] = buf.ReadByte(i);
        return result;
    }

    [TestMethod]
    public void EmptyPath_GivesEmptyBuffer()
    {
        using var buf = new HexBuffer(string.Empty);
        Assert.AreEqual(0, buf.FileLength);
        Assert.AreEqual(0, buf.VirtualLength);
        Assert.AreEqual(0, buf.TotalRows);
        Assert.IsFalse(buf.IsModified);
        Assert.IsFalse(buf.CanUndo);
        Assert.IsFalse(buf.CanRedo);
    }

    [TestMethod]
    public void NonexistentPath_GivesEmptyBuffer()
    {
        using var buf = new HexBuffer(Path.Combine(Path.GetTempPath(), $"nope_{Guid.NewGuid():N}.bin"));
        Assert.AreEqual(0, buf.FileLength);
        Assert.AreEqual(0, buf.VirtualLength);
    }

    [TestMethod]
    public void OpenFile_ReadsOriginalBytes()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var path = WriteTempFile(data);
        try
        {
            using var buf = new HexBuffer(path);
            Assert.AreEqual(data.Length, buf.FileLength);
            Assert.AreEqual(data.Length, buf.VirtualLength);
            CollectionAssert.AreEqual(data, ReadAllVirtual(buf));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void TotalRows_RoundsUp()
    {
        var path = WriteTempFile(new byte[17]); // 16 + 1
        try
        {
            using var buf = new HexBuffer(path);
            Assert.AreEqual(2, buf.TotalRows);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ReadByte_OutOfRange_ReturnsZero()
    {
        var path = WriteTempFile(new byte[] { 7, 8, 9 });
        try
        {
            using var buf = new HexBuffer(path);
            Assert.AreEqual(0, buf.ReadByte(-1));
            Assert.AreEqual(0, buf.ReadByte(3));
            Assert.AreEqual(0, buf.ReadByte(100));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ReadRange_StopsAtEnd()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var buf = new HexBuffer(path);
            var range = buf.ReadRange(1, 5);
            Assert.AreEqual(5, range.Length);
            Assert.AreEqual(2, range[0]);
            Assert.AreEqual(3, range[1]);
            Assert.AreEqual(0, range[2]); // beyond end → zero-filled
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Overwrite_ChangesByteAndMarksEdited()
    {
        var path = WriteTempFile(new byte[] { 10, 20, 30, 40 });
        try
        {
            using var buf = new HexBuffer(path);
            buf.Overwrite(1, 0xAB);

            Assert.AreEqual(10, buf.ReadByte(0));
            Assert.AreEqual(0xAB, buf.ReadByte(1));
            Assert.AreEqual(30, buf.ReadByte(2));
            Assert.AreEqual(40, buf.ReadByte(3));
            Assert.AreEqual(4, buf.VirtualLength); // overwrite does not change length
            Assert.IsTrue(buf.IsModified);
            Assert.IsTrue(buf.IsEdited(1));
            Assert.IsFalse(buf.IsEdited(0));
            Assert.IsFalse(buf.IsEdited(2));
            Assert.IsTrue(buf.CanUndo);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Overwrite_AtFirstAndLast_Works()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var buf = new HexBuffer(path);
            buf.Overwrite(0, 0xAA);
            buf.Overwrite(2, 0xCC);
            CollectionAssert.AreEqual(new byte[] { 0xAA, 2, 0xCC }, ReadAllVirtual(buf));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Overwrite_OutOfRange_NoOp()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var buf = new HexBuffer(path);
            buf.Overwrite(-1, 0xFF);
            buf.Overwrite(3, 0xFF);
            Assert.IsFalse(buf.IsModified);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, ReadAllVirtual(buf));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Insert_GrowsBuffer_AndShiftsTrailingBytes()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var buf = new HexBuffer(path);
            buf.Insert(1, 0x99);

            Assert.AreEqual(4, buf.VirtualLength);
            CollectionAssert.AreEqual(new byte[] { 1, 0x99, 2, 3 }, ReadAllVirtual(buf));
            Assert.IsTrue(buf.IsEdited(1));
            Assert.IsTrue(buf.IsModified);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Insert_AtStart_PrependsByte()
    {
        var path = WriteTempFile(new byte[] { 1, 2 });
        try
        {
            using var buf = new HexBuffer(path);
            buf.Insert(0, 0x77);
            CollectionAssert.AreEqual(new byte[] { 0x77, 1, 2 }, ReadAllVirtual(buf));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Insert_AtEnd_AppendsByte()
    {
        var path = WriteTempFile(new byte[] { 1, 2 });
        try
        {
            using var buf = new HexBuffer(path);
            buf.Insert(2, 0x77); // at VirtualLength
            CollectionAssert.AreEqual(new byte[] { 1, 2, 0x77 }, ReadAllVirtual(buf));
            Assert.AreEqual(3, buf.VirtualLength);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Insert_OutOfRange_NoOp()
    {
        var path = WriteTempFile(new byte[] { 1, 2 });
        try
        {
            using var buf = new HexBuffer(path);
            buf.Insert(-1, 0xFF);
            buf.Insert(99, 0xFF);
            Assert.IsFalse(buf.IsModified);
            Assert.AreEqual(2, buf.VirtualLength);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Delete_RemovesByte_AndShortensBuffer()
    {
        var path = WriteTempFile(new byte[] { 10, 20, 30, 40 });
        try
        {
            using var buf = new HexBuffer(path);
            buf.Delete(1);

            Assert.AreEqual(3, buf.VirtualLength);
            CollectionAssert.AreEqual(new byte[] { 10, 30, 40 }, ReadAllVirtual(buf));
            Assert.IsTrue(buf.IsModified);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Delete_OutOfRange_NoOp()
    {
        var path = WriteTempFile(new byte[] { 1, 2 });
        try
        {
            using var buf = new HexBuffer(path);
            buf.Delete(-1);
            buf.Delete(2);
            Assert.IsFalse(buf.IsModified);
            Assert.AreEqual(2, buf.VirtualLength);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Undo_ReversesOverwrite()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var buf = new HexBuffer(path);
            buf.Overwrite(1, 0xFF);
            buf.Undo();

            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, ReadAllVirtual(buf));
            Assert.IsFalse(buf.IsModified);
            Assert.IsTrue(buf.CanRedo);
            Assert.IsFalse(buf.CanUndo);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Undo_ReversesInsertAndDelete()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var buf = new HexBuffer(path);
            buf.Insert(1, 0x99);
            buf.Delete(0);
            Assert.AreEqual(3, buf.VirtualLength);

            buf.Undo(); // undo delete
            CollectionAssert.AreEqual(new byte[] { 1, 0x99, 2, 3 }, ReadAllVirtual(buf));
            buf.Undo(); // undo insert
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, ReadAllVirtual(buf));
            Assert.IsFalse(buf.IsModified);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Redo_ReappliesEdit()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var buf = new HexBuffer(path);
            buf.Overwrite(0, 0xAB);
            buf.Undo();
            buf.Redo();
            Assert.AreEqual(0xAB, buf.ReadByte(0));
            Assert.IsTrue(buf.IsModified);
            Assert.IsFalse(buf.CanRedo);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void NewEdit_ClearsRedoStack()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var buf = new HexBuffer(path);
            buf.Overwrite(0, 0xAA);
            buf.Undo();
            Assert.IsTrue(buf.CanRedo);

            buf.Overwrite(1, 0xBB);
            Assert.IsFalse(buf.CanRedo);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Save_PersistsEdits_AndClearsModified()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3, 4 });
        try
        {
            using (var buf = new HexBuffer(path))
            {
                buf.Overwrite(1, 0x99);
                buf.Insert(0, 0xAA);
                buf.Delete(4); // delete the original "4" (now at index 4 after insert)
                buf.Save();

                Assert.IsFalse(buf.IsModified);
                Assert.IsFalse(buf.CanUndo);
                Assert.IsFalse(buf.CanRedo);
            }
            var saved = File.ReadAllBytes(path);
            CollectionAssert.AreEqual(new byte[] { 0xAA, 1, 0x99, 3 }, saved);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [TestMethod]
    public void SaveAs_WritesToDifferentFile_AndKeepsOriginal()
    {
        var src  = WriteTempFile(new byte[] { 1, 2, 3 });
        var dest = Path.Combine(Path.GetTempPath(), $"hexbuf_dest_{Guid.NewGuid():N}.bin");
        try
        {
            using (var buf = new HexBuffer(src))
            {
                buf.Overwrite(0, 0xEE);
                buf.Save(dest);
            }
            CollectionAssert.AreEqual(new byte[] { 0xEE, 2, 3 }, File.ReadAllBytes(dest));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 },    File.ReadAllBytes(src));
        }
        finally
        {
            if (File.Exists(src))  File.Delete(src);
            if (File.Exists(dest)) File.Delete(dest);
        }
    }

    [TestMethod]
    public void LargeFile_ReadAcrossWindowBoundaries_IsCorrect()
    {
        // > 8 KB so multiple window loads are required during scan
        var data = new byte[20_000];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i & 0xFF);
        var path = WriteTempFile(data);
        try
        {
            using var buf = new HexBuffer(path);
            // Sample at scattered offsets that force window re-loads.
            int[] samples = { 0, 1, 4095, 4096, 8191, 8192, 12_345, 19_999 };
            foreach (var o in samples)
                Assert.AreEqual(data[o], buf.ReadByte(o), $"mismatch at {o}");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void EnsureWindow_DoesNotMutateContent()
    {
        var data = new byte[10_000];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i & 0xFF);
        var path = WriteTempFile(data);
        try
        {
            using var buf = new HexBuffer(path);
            buf.EnsureWindow(topRow: 500, visibleRows: 30);
            Assert.AreEqual(data[8_000], buf.ReadByte(8_000));
            buf.EnsureWindow(topRow: 0, visibleRows: 30);
            Assert.AreEqual(data[5], buf.ReadByte(5));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void IsEdited_FalseForOriginalBytes_TrueForAddBufferBytes()
    {
        var path = WriteTempFile(new byte[] { 1, 2, 3 });
        try
        {
            using var buf = new HexBuffer(path);
            Assert.IsFalse(buf.IsEdited(0));
            buf.Overwrite(0, 0x42);
            Assert.IsTrue(buf.IsEdited(0));
            Assert.IsFalse(buf.IsEdited(1));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void RepeatedEdits_OnSameOffset_AllReversibleInOrder()
    {
        var path = WriteTempFile(new byte[] { 0, 0, 0 });
        try
        {
            using var buf = new HexBuffer(path);
            buf.Overwrite(1, 0xAA);
            buf.Overwrite(1, 0xBB);
            buf.Overwrite(1, 0xCC);
            Assert.AreEqual(0xCC, buf.ReadByte(1));

            buf.Undo(); Assert.AreEqual(0xBB, buf.ReadByte(1));
            buf.Undo(); Assert.AreEqual(0xAA, buf.ReadByte(1));
            buf.Undo(); Assert.AreEqual(0,    buf.ReadByte(1));
            Assert.IsFalse(buf.CanUndo);
        }
        finally { File.Delete(path); }
    }
}
