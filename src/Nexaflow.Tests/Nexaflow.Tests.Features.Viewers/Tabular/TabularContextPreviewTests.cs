using System;
using System.IO;
using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.Features.Tabular.Templates;
using Nexaflow.Features.Tabular.ViewModels;
using Nexaflow.Tests.Features.Infrastructure;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Tabular;

/// <summary>
/// The Tabular page's context preview: the file and its size in rows and columns, then a few of its first
/// columns as aligned text over the selected rows, or the rows where the grid stands — what the table holds,
/// not a second grid.
/// </summary>
[TestClass]
[TestCategory("Unit")]
[CoversNode("tabular-ai-preview")]
public class TabularContextPreviewTests
{
    [TestMethod]
    public void TheTable_IsAligned_OverItsFirstColumns_WithLongCellsCut()
    {
        var text = TabularPreviewTable.Format(
            ["id", "name", "city", "country", "zip"],
            [["1", "Ada", "London", "UK", "N1"], ["22", "A name far too long to fit", "Paris", "FR", "75"]]);

        var lines = text.Split('\n');
        Assert.AreEqual("id  name            city    country  …", lines[0], "the columns beyond the first four are marked, not shown");
        Assert.AreEqual("──  ──────────────  ──────  ───────", lines[1]);
        Assert.AreEqual("1   Ada             London  UK", lines[2]);
        Assert.AreEqual("22  A name far to…  Paris   FR", lines[3]);
        Assert.AreEqual(string.Empty, TabularPreviewTable.Format([], [["1"]]), "no columns, nothing to show");
    }

    [TestMethod]
    public void ThePreview_NamesTheFile_ItsSize_AndShowsTheRows()
    {
        var dir = Path.Combine(TestTemp.Root, "nexatab_preview_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csv = Path.Combine(dir, "people.csv");
        File.WriteAllText(csv, "name,age\nAda,36\nAlan,41\nGrace,85\n");
        try
        {
            var vm = new TabularViewModel(csv, Substitute.For<IShellServices>(), Substitute.For<IAIService>(),
                                          new TabularTemplatesConfig());
            vm.Ready.GetAwaiter().GetResult();

            UiThread.Run(() =>
            {
                var shown = ContextPreviewProbe.VisibleTexts(vm.CreateContextPreview());
                Assert.AreEqual("people.csv", shown[0]);
                StringAssert.StartsWith(shown[1], "3 rows × 2 columns");
                var lines = shown[2].Split('\n');
                Assert.AreEqual("name   age", lines[0]);
                CollectionAssert.AreEqual(new[] { "Ada    36", "Alan   41", "Grace  85" }, lines[2..]);
            });
            vm.Dispose();
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
