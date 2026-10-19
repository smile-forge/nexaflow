using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Json.Models;
using Nexaflow.Features.Json.Services;
using Nexaflow.Features.Json.ViewModels;
using Nexaflow.Tests.Features.Infrastructure;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Controls;
using NSubstitute;

namespace Nexaflow.Tests.Features.Json;

/// <summary>
/// The JSON page's context preview: the file, its size and the selected node's path, then that node's entries
/// as a short outline — where the user is in the document, not a second tree.
/// </summary>
[TestClass]
[TestCategory("Unit")]
[CoversNode("json-ai-preview")]
public class JsonContextPreviewTests
{
    private string? _path;

    [TestCleanup]
    public void Cleanup()
    {
        try { if (_path is not null) File.Delete(_path); } catch { }
    }

    private async Task<JsonViewModel> LoadedAsync(string content)
    {
        _path = Path.Combine(TestTemp.Root, $"jsonpreview_{Guid.NewGuid():N}.json");
        File.WriteAllText(_path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var vm = new JsonViewModel(_path, new JsonFileLoader(), new JsonPathEvaluator(), Substitute.For<IShellServices>());
        await vm.LoadAsync(CancellationToken.None);
        return vm;
    }

    private static string Document(int extraKeys) =>
        "{\"name\":\"nexaflow\",\"server\":{\"host\":\"localhost\",\"port\":8080},\"tags\":[\"a\",\"b\",\"c\"],"
        + "\"note\":\"" + new string('x', 100) + "\""
        + string.Concat(Enumerable.Range(0, extraKeys).Select(i => $",\"k{i}\":{i}"))
        + "}";

    [TestMethod]
    public async Task TheOutline_ListsEntries_ShowsContainersBySize_AndCountsTheRest()
    {
        var vm = await LoadedAsync(Document(extraKeys: 11));

        var lines = JsonContextOutline.Of(vm.Root).Split('\n');

        Assert.AreEqual("\"name\": \"nexaflow\"", lines[0]);
        Assert.AreEqual("\"server\": {2}", lines[1], "an object is shown by its size, not opened");
        Assert.AreEqual("\"tags\": [3]", lines[2]);
        Assert.IsTrue(lines[3].EndsWith('…') && lines[3].Length < 80, "a long value is cut: " + lines[3]);
        Assert.AreEqual(JsonContextOutline.MaxEntries + 1, lines.Length);
        Assert.AreEqual("… +3", lines[^1], "15 entries: 12 listed, the rest counted");
    }

    [TestMethod]
    public async Task ThePreview_NamesTheFile_AndFollowsTheSelectedNode()
    {
        var vm = await LoadedAsync(Document(extraKeys: 0));

        UiThread.Run(() =>
        {
            var whole = ContextPreviewProbe.VisibleTexts(vm.CreateContextPreview());
            Assert.IsInstanceOfType(vm.CreateContextPreview(), typeof(ReadOnlyTextPreview));
            Assert.AreEqual(vm.FileName, whole[0]);
            Assert.AreEqual(vm.FileSizeText, whole[1], "nothing selected: no path to name");
            StringAssert.StartsWith(whole[2], "\"name\": \"nexaflow\"");

            vm.SelectedDisplayItem = vm.DisplayItems.OfType<JsonTreeDisplayItem>().First(d => d.Node.Key == "server");
            var server = ContextPreviewProbe.VisibleTexts(vm.CreateContextPreview());
            StringAssert.EndsWith(server[1], "$.server");
            Assert.AreEqual("\"host\": \"localhost\"\n\"port\": 8080", server[2]);
        });
    }
}
