using Nexaflow.Features.Common;
using NSubstitute;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Notebook.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Notebook;

/// <summary>
/// Covers the Notebook AI-integration surface: an enriched get_context (file, kernel, per-cell digest),
/// the file-scoped security context, and the read-only client-tool surface (read_notebook / read_cell)
/// driven exactly as the conversation hub drives it — each returns the parsed cell source + stored outputs.
/// </summary>
[TestClass]
public class NotebookAiTests
{
    private string _dir = string.Empty;
    private string _path = string.Empty;

    /// <summary>Two markdown + two code cells; the second code cell carries a stream output.</summary>
    private const string Ipynb = """
        {
         "cells": [
          {"cell_type": "markdown", "source": ["# Analysis\n", "\n", "Intro paragraph.\n"]},
          {"cell_type": "code", "execution_count": 1, "outputs": [], "source": ["import math\n", "radius = 2\n"]},
          {"cell_type": "markdown", "source": ["## Area\n"]},
          {"cell_type": "code", "execution_count": 2,
           "outputs": [
             {"output_type": "stream", "name": "stdout", "text": ["The area is 12.566\n"]}
           ],
           "source": ["area = math.pi * radius ** 2\n", "print(f'The area is {area:.3f}')\n"]}
         ],
         "metadata": {"kernelspec": {"language": "python", "name": "python3"}},
         "nbformat": 4, "nbformat_minor": 5
        }
        """;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexanb_ai_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "analysis.ipynb");
        File.WriteAllText(_path, Ipynb);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [TestMethod]
    [CoversNode("notebook-ai-context")]
    public async Task Context_And_SecurityScope()
    {
        var vm = new NotebookViewModel(_path, Substitute.For<IShellServices>());
        await vm.LoadAsync();

        // ── security scope is the notebook file path (the tool boundary) ──
        Assert.AreEqual(_path, vm.GetSecurityContext());

        // ── enriched context: names the notebook, the kernel, the cell kinds, and points at the read tools ──
        var ctx = vm.GetContext();
        StringAssert.Contains(ctx, vm.FileName);          // the notebook is named
        StringAssert.Contains(ctx, "python");             // the kernel
        StringAssert.Contains(ctx, "2 code cell(s)");     // code cell count
        StringAssert.Contains(ctx, "2 markdown cell(s)"); // markdown cell count
        StringAssert.Contains(ctx, "markdown");           // per-cell digest names each kind
        StringAssert.Contains(ctx, "code");
        StringAssert.Contains(ctx, "# Analysis");         // first-line snippet of a cell
        StringAssert.Contains(ctx, "read_notebook");      // points the model at the read tools
    }

    [TestMethod]
    [CoversNode("notebook-ai-act")]
    public async Task ReadNotebook_ReturnsEveryCellSourceAndOutputs()
    {
        var vm = new NotebookViewModel(_path, Substitute.For<IShellServices>());
        await vm.LoadAsync();

        // ── the exact tool surface (a change here should force a tree reconcile) ──
        var tools = vm.GetClientTools();
        CollectionAssert.AreEquivalent(
            new[] { "read_notebook", "read_cell" },
            tools.Select(t => t.Name).ToArray(),
            "the Notebook AI act tool surface changed — update the tree's notebook-ai-act leaves to match");
        Assert.IsTrue(tools.All(t => t.Safety == Nexaflow.Features.Common.ClientTools.ToolSafety.SafeOperation),
            "notebook read tools must all be SafeOperation (reads never prompt)");

        var read = tools.Single(t => t.Name == "read_notebook");
        var r = await read.InvokeAsync(new JsonObject(), CancellationToken.None);

        Assert.IsFalse(r.IsError);
        // every cell's source is present
        StringAssert.Contains(r.ModelText, "# Analysis");
        StringAssert.Contains(r.ModelText, "import math");
        StringAssert.Contains(r.ModelText, "## Area");
        StringAssert.Contains(r.ModelText, "area = math.pi");
        // the stored stream output is surfaced
        StringAssert.Contains(r.ModelText, "The area is 12.566");
    }

    [TestMethod]
    [CoversNode("notebook-ai-act")]
    public async Task ReadCell_ReturnsRequestedCell_AndErrorsOutOfRange()
    {
        var vm = new NotebookViewModel(_path, Substitute.For<IShellServices>());
        await vm.LoadAsync();

        var readCell = vm.GetClientTools().Single(t => t.Name == "read_cell");

        // ── cell 1 is the intro markdown ──
        var c1 = await readCell.InvokeAsync(new JsonObject { ["index"] = 1 }, CancellationToken.None);
        Assert.IsFalse(c1.IsError);
        StringAssert.Contains(c1.ModelText, "# Analysis");
        Assert.IsFalse(c1.ModelText.Contains("import math"), "read_cell must return only the requested cell");

        // ── cell 4 is the code cell with the stream output ──
        var c4 = await readCell.InvokeAsync(new JsonObject { ["index"] = 4 }, CancellationToken.None);
        Assert.IsFalse(c4.IsError);
        StringAssert.Contains(c4.ModelText, "area = math.pi");
        StringAssert.Contains(c4.ModelText, "The area is 12.566");

        // ── out of range (and non-positive) index errors rather than throwing ──
        var oob = await readCell.InvokeAsync(new JsonObject { ["index"] = 99 }, CancellationToken.None);
        Assert.IsTrue(oob.IsError, "an out-of-range index should be an error result");
        var zero = await readCell.InvokeAsync(new JsonObject { ["index"] = 0 }, CancellationToken.None);
        Assert.IsTrue(zero.IsError, "a non-positive index should be an error result");
    }
}
