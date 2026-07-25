using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Notebook.Models;
using Nexaflow.Features.Notebook.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Notebook;

/// <summary>
/// The Notebook feature parses an <c>.ipynb</c> into ordered cells (decoding the JSON-escaped, line-array
/// source), picks the kernel grammar, and builds a per-cell code outline.
/// </summary>
[TestClass]
public class NotebookTests
{
    private const string SampleJson = """
        {
         "cells": [
          {"cell_type": "markdown", "source": ["# Title\n", "\n", "Intro text.\n"]},
          {"cell_type": "code", "execution_count": 3, "source": [
             "import math\n",
             "\n",
             "class Circle:\n",
             "    def __init__(self, r):\n",
             "        self.r = r\n",
             "    def area(self):\n",
             "        return math.pi * self.r ** 2\n"
          ]}
         ],
         "metadata": {"kernelspec": {"language": "python", "name": "python3"}},
         "nbformat": 4, "nbformat_minor": 5
        }
        """;

    [TestMethod]
    [CoversNode("notebook-parse")]
    public void Parse_ReadsKernelCellsAndDecodesSource()
    {
        var nb = NotebookDocument.Parse(SampleJson);

        Assert.AreEqual("python", nb.GrammarId);
        Assert.AreEqual(2, nb.Cells.Count);

        var md = nb.Cells[0];
        Assert.AreEqual(NotebookCellKind.Markdown, md.Kind);
        StringAssert.Contains(md.Source, "# Title");

        var code = nb.Cells[1];
        Assert.AreEqual(NotebookCellKind.Code, code.Kind);
        Assert.AreEqual(3, code.ExecutionCount);
        // The array-of-lines source is decoded + joined into real multi-line text.
        StringAssert.Contains(code.Source, "class Circle:\n");
        StringAssert.Contains(code.Source, "    def area(self):");
    }

    [TestMethod]
    [CoversNode("notebook-parse")]
    [CoversNode("notebook-kernel-grammar")]
    public void Parse_DefaultsToPythonAndIsRobustToGarbage()
    {
        Assert.AreEqual("python", NotebookDocument.Parse("not json").GrammarId);
        Assert.AreEqual(0, NotebookDocument.Parse("not json").Cells.Count);
    }

    [TestMethod]
    [CoversNode("notebook-outline")]
    public async Task ViewModel_LoadsCells_AndBuildsCodeOutline()
    {
        var path = Path.Combine(TestSampleData.Path("notebook"), "notebook.ipynb");
        var vm = new NotebookViewModel(path);

        await vm.LoadAsync();

        Assert.IsTrue(vm.IsLoaded);
        Assert.IsTrue(vm.Cells.Any(c => c.IsMarkdown), "has a markdown cell");
        var codeCell = vm.Cells.First(c => c.IsCode);
        Assert.AreEqual("python", codeCell.GrammarId);
        StringAssert.StartsWith(codeCell.Label, "In [");

        Assert.IsTrue(vm.HasOutline, "code structure outline built");
        StringAssert.Contains(vm.OutlineMarkdown, "Circle");      // the class from the decoded code cell
        StringAssert.Contains(vm.OutlineMarkdown, "total_area");  // a top-level function from another cell
    }
}
