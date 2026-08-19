using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Json.Models;
using Nexaflow.Features.Json.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Json;

/// <summary>
/// Exercises the seek-by-item windowed loader: the small-file fast path, the large-file streaming
/// path (front batch + a virtual tail node), virtual-chunk loading, BOM skipping and estimation.
/// </summary>
[TestClass]
[CoversNode("json-windowing")]
public class JsonFileLoaderTests
{
    private const long LargeFileThreshold = 1 * 1024 * 1024;

    private static string WriteText(string content, bool utf8Bom = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"jsonload_{Guid.NewGuid():N}.json");
        using var fs = File.Create(path);
        if (utf8Bom) fs.Write([0xEF, 0xBB, 0xBF]);
        var bytes = Encoding.UTF8.GetBytes(content);
        fs.Write(bytes, 0, bytes.Length);
        return path;
    }

    /// <summary>A homogeneous array that comfortably exceeds the 1 MB large-file threshold.</summary>
    private static string WriteLargeArray(int count = 30_000, bool utf8Bom = false)
    {
        var sb = new StringBuilder("[\n");
        for (var i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(",\n");
            sb.Append('{').Append("\"id\":").Append(i)
              .Append(",\"name\":\"item_").Append(i).Append('"')
              .Append(",\"value\":").Append(i * 3).Append('}');
        }
        sb.Append("\n]");
        var path = WriteText(sb.ToString(), utf8Bom);
        Assert.IsTrue(new FileInfo(path).Length > LargeFileThreshold, "fixture must exceed the large-file threshold");
        return path;
    }

    // ── Small-file fast path ─────────────────────────────────────────────────

    [TestMethod]
    public async Task LoadAsync_SmallObject_BuildsObjectRoot()
    {
        var path = WriteText("""{ "a": 1, "b": "two", "c": [1, 2, 3] }""");
        try
        {
            var result = await new JsonFileLoader().LoadAsync(path, CancellationToken.None);

            Assert.IsNull(result.ErrorMessage);
            Assert.IsFalse(result.IsLargeFile);
            var obj = result.Root as JsonObjectNodeModel;
            Assert.IsNotNull(obj);
            Assert.AreEqual(3, obj!.Children.Count);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task LoadAsync_SmallArray_BuildsArrayRoot()
    {
        var path = WriteText("[10, 20, 30, 40, 50]");
        try
        {
            var result = await new JsonFileLoader().LoadAsync(path, CancellationToken.None);

            Assert.IsNull(result.ErrorMessage);
            var arr = result.Root as JsonArrayNodeModel;
            Assert.IsNotNull(arr);
            Assert.AreEqual(5, arr!.Children.Count);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task LoadAsync_ScalarRoot_BuildsValueNode()
    {
        var path = WriteText("42");
        try
        {
            var result = await new JsonFileLoader().LoadAsync(path, CancellationToken.None);

            Assert.IsNull(result.ErrorMessage);
            var val = result.Root as JsonValueNodeModel;
            Assert.IsNotNull(val);
            Assert.AreEqual(42L, val!.Value);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task LoadAsync_InvalidJson_ReturnsError()
    {
        var path = WriteText("{ not valid json ");
        try
        {
            var result = await new JsonFileLoader().LoadAsync(path, CancellationToken.None);

            Assert.IsNull(result.Root);
            Assert.IsNotNull(result.ErrorMessage);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task LoadAsync_MissingFile_ReturnsNotFound()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"does_not_exist_{Guid.NewGuid():N}.json");

        var result = await new JsonFileLoader().LoadAsync(missing, CancellationToken.None);

        Assert.IsNull(result.Root);
        StringAssert.Contains(result.ErrorMessage, "not found");
    }

    // ── Large-file streaming path ────────────────────────────────────────────

    [TestMethod]
    public async Task LoadAsync_LargeArray_StreamsFrontBatchPlusVirtualTail()
    {
        var path = WriteLargeArray();
        try
        {
            var result = await new JsonFileLoader().LoadAsync(path, CancellationToken.None);

            Assert.IsNull(result.ErrorMessage);
            Assert.IsTrue(result.IsLargeFile);
            Assert.IsTrue(result.RootContentOffset > 0);

            var arr = result.Root as JsonArrayNodeModel;
            Assert.IsNotNull(arr);

            var real    = arr!.Children.Where(c => c is not VirtualJsonNodeModel).ToList();
            var virtual_ = arr.Children.OfType<VirtualJsonNodeModel>().ToList();
            Assert.AreEqual(50, real.Count, "front batch is one BatchItemCount of real items");
            Assert.AreEqual(1, virtual_.Count, "a single virtual node stands in for the unread tail");
            Assert.IsTrue(virtual_[0].ByteOffset > 0);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task LoadVirtualChunkAsync_FromTail_ReturnsNextBatch()
    {
        var path = WriteLargeArray();
        try
        {
            var loader = new JsonFileLoader();
            var result = await loader.LoadAsync(path, CancellationToken.None);
            var arr    = (JsonArrayNodeModel)result.Root!;
            var tail   = arr.Children.OfType<VirtualJsonNodeModel>().First();

            var (items, nextOffset) = await loader.LoadVirtualChunkAsync(
                path, tail.ByteOffset, result.FileSizeBytes, isArray: true, CancellationToken.None);

            Assert.AreEqual(50, items.Count, "one batch past the front 50");
            // Front batch covered ids 0..49, so the next batch starts at id 50.
            var first = items[0].node as JsonObject;
            Assert.IsNotNull(first);
            Assert.AreEqual(50, first!["id"]!.GetValue<int>());
            Assert.IsTrue(nextOffset > tail.ByteOffset);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task LoadAsync_LargeArray_WithUtf8Bom_SkipsBomAndStreams()
    {
        var path = WriteLargeArray(utf8Bom: true);
        try
        {
            var result = await new JsonFileLoader().LoadAsync(path, CancellationToken.None);

            Assert.IsNull(result.ErrorMessage);
            Assert.IsTrue(result.IsLargeFile);
            Assert.IsInstanceOfType(result.Root, typeof(JsonArrayNodeModel));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task EstimateAsync_HomogeneousLargeArray_EstimatesCountAndHomogeneity()
    {
        var path = WriteLargeArray();
        try
        {
            var fileSize = new FileInfo(path).Length;
            var estimate = await new JsonFileLoader().EstimateAsync(path, fileSize, isArray: true, CancellationToken.None);

            Assert.IsNotNull(estimate);
            Assert.IsTrue(estimate!.EstimatedCount > 0);
            Assert.IsTrue(estimate.AvgItemBytes > 0);
            Assert.IsTrue(estimate.IsLikelyHomogeneous, "every item shares the same keys");
        }
        finally { File.Delete(path); }
    }
}
