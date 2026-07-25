using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Notebook.FileActions;
using Nexaflow.Features.Notebook.Models;
using Nexaflow.Features.Notebook.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Notebook;

/// <summary>
/// How a parsed notebook reaches the page: what each kind of cell exposes to the template that draws it,
/// when the outline column appears at all, when the page is honest enough to be pinned as AI context, and
/// the file action that opens one.
/// <para>
/// A notebook is read-only, so there is no command surface here — what can go wrong instead is a cell being
/// drawn as the wrong kind, a code cell highlighted with the wrong grammar, an execution label that lies,
/// or the page reporting an empty stub before it has read the file.
/// </para>
/// </summary>
[TestClass]
public class NotebookSurfaceTests
{
    private static string WriteNotebook(string json, out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "nexanb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "book.ipynb");
        File.WriteAllText(path, json);
        return path;
    }

    private const string TwoCells = """
        {
          "metadata": { "kernelspec": { "language": "python" } },
          "cells": [
            { "cell_type": "markdown", "source": ["# Title\n", "Some prose.\n"] },
            { "cell_type": "code", "execution_count": 3, "source": "def go():\n    return 1\n" }
          ]
        }
        """;

    // ── Cells ─────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("notebook-markdown-cell")]
    [CoversNode("notebook-code-cell")]
    public void EachCellExposesOnlyWhatItsOwnTemplateNeeds()
    {
        var doc = NotebookDocument.Parse(TwoCells);
        var cells = doc.Cells.Select(c => new NotebookCellViewModel(c, doc.GrammarId)).ToList();

        var markdown = cells[0];
        Assert.IsTrue(markdown.IsMarkdown);
        Assert.IsFalse(markdown.IsCode, "the template selector picks one or the other, never both");
        Assert.IsNull(markdown.GrammarId, "prose has no grammar to highlight it with");
        Assert.AreEqual(string.Empty, markdown.Label, "and no execution gutter");

        var code = cells[1];
        Assert.IsTrue(code.IsCode);
        Assert.AreEqual("python", code.GrammarId);
        StringAssert.Contains(code.Source, "def go():");
    }

    [TestMethod]
    [CoversNode("notebook-code-cell")]
    public void TheExecutionLabelSaysWhetherTheCellHasBeenRun()
    {
        var doc = NotebookDocument.Parse(TwoCells);

        Assert.AreEqual("In [3]", new NotebookCellViewModel(doc.Cells[1], doc.GrammarId).Label);

        var unrun = NotebookDocument.Parse("""
            { "cells": [ { "cell_type": "code", "source": "x = 1" } ] }
            """);
        Assert.AreEqual("In [ ]", new NotebookCellViewModel(unrun.Cells[0], unrun.GrammarId).Label,
                        "a never-executed cell must read as blank, not as In [0]");
    }

    // ── Outline ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("notebook-outline-entries")]
    public async Task TheOutlineColumnIsHidden_WhenTheNotebookDeclaresNothing()
    {
        var path = WriteNotebook("""
            { "cells": [ { "cell_type": "markdown", "source": "just prose" },
                         { "cell_type": "code", "source": "print(1)" } ] }
            """, out var dir);
        try
        {
            var vm = new NotebookViewModel(path);
            await vm.LoadAsync();

            Assert.IsFalse(vm.HasOutline, "an empty column is worse than no column");
            Assert.AreEqual(string.Empty, vm.OutlineMarkdown);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("notebook-outline-entries")]
    [CoversNode("notebook-structure")]
    public async Task TheOutlineListsWhatTheCodeCellsDeclare()
    {
        var path = WriteNotebook(TwoCells, out var dir);
        try
        {
            var vm = new NotebookViewModel(path);
            await vm.LoadAsync();

            Assert.IsTrue(vm.HasOutline);
            StringAssert.Contains(vm.OutlineMarkdown, "go", "the function the notebook declares");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Readiness ─────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("notebook-ai-context")]
    public async Task ANotebookIsNotPinnableAsContextUntilItHasParsed()
    {
        var path = WriteNotebook(TwoCells, out var dir);
        try
        {
            var vm = new NotebookViewModel(path);
            Assert.IsFalse(vm.IsContextReady,
                           "pinned this early it would report the empty python/0/0 stub as if it were the file");

            await vm.LoadAsync();

            Assert.IsTrue(vm.IsContextReady);
            StringAssert.Contains(vm.GetContext(), "1 code cell(s), 1 markdown cell(s)");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Open action ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("notebook-open")]
    public void AsNotebook_OpensTheTabOnTheFileItWasInvokedOn()
    {
        var shell = Substitute.For<IShellServices>();
        var opened = new List<Dictionary<string, string>>();
        shell.When(s => s.OpenTab("Notebook", Arg.Any<Dictionary<string, string>>()))
             .Do(ci => opened.Add(ci.Arg<Dictionary<string, string>>()));
        var action = new ShowNotebookAction(shell);

        Assert.IsTrue(action.PerformAction(@"C:\work\analysis.ipynb"));

        Assert.AreEqual(@"C:\work\analysis.ipynb", opened.Single()["path"]);
        Assert.IsFalse(action.SupportsMultipleFiles, "one notebook per tab — they are long documents");
    }
}
