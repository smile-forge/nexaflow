using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// A feature's <c>Model</c> folder holds what its pages are, named in no UI toolkit: where a page is, what it lists, what it
/// does and what the assistant is given (docs/Architecture.md, Views and view-models). Every toolkit's view-model shows the
/// same model, so the model names none of them, and none of the view-models that sit over it.
/// <para>
/// No baseline: a model folder starts toolkit-free and stays so. What counts as the model: a C# file in a <c>Model</c>
/// folder directly inside a feature project under <c>src/Nexaflow.Features</c>. What counts as naming a toolkit:
/// <c>System.Windows</c> or <c>Avalonia</c> in its code once comments are taken out, a type written out in full as much as a
/// <c>using</c>; what counts as naming a view-model: a <c>ViewModels</c> namespace.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("whole-repo architecture guard; maps to no single product node")]
public class FeatureModelRulesTests
{
    private static readonly string Root = RepoRoot.Locate();

    private static readonly Regex ToolkitOrViewModel = new(@"\bSystem\.Windows\b|\bAvalonia\b|\.ViewModels\b", RegexOptions.Compiled);

    [TestMethod]
    [TestCategory("Unit")]
    public void A_feature_model_names_no_UI_toolkit_and_no_view_model()
    {
        var found = new List<string>();
        foreach (var file in ModelFiles())
        {
            var code = CodeWithoutComments.Read(file);
            foreach (Match match in ToolkitOrViewModel.Matches(code))
                found.Add($"  {Relative(file)}    (line {CodeWithoutComments.LineOf(code, match.Index)}: {match.Value})");
        }

        Assert.AreEqual(0, found.Count,
            $"{found.Count} use(s) of a UI toolkit or a view-model in a feature's Model folder:\n"
          + string.Join("\n", found)
          + "\nThe model is what every toolkit's view-model shows: brushes, bitmaps, controls and view-models belong to the\n"
          + "view-model and the view over it (docs/Architecture.md).");
    }

    /// <summary>The rule reads what it says it reads: the file system's model is one it finds.</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void The_rule_reads_the_file_systems_model()
    {
        var files = ModelFiles().Select(Relative).ToList();

        CollectionAssert.Contains(files, "src/Nexaflow.Features/Nexaflow.Features.WindowsFileSystem/Model/FileSystemModel.cs");
    }

    /// <summary>Every C# file in a <c>Model</c> folder directly inside a feature project.</summary>
    private static IEnumerable<string> ModelFiles()
    {
        var features = Path.Combine(Root, "src", "Nexaflow.Features");
        return Directory.EnumerateFiles(features, "*.cs", SearchOption.AllDirectories)
                        .Where(file => !CodeWithoutComments.IsBuildOutput(file))
                        .Where(file => Path.GetRelativePath(features, file).Split(Path.DirectorySeparatorChar) is [_, "Model", ..]);
    }

    private static string Relative(string file) => Path.GetRelativePath(Root, file).Replace('\\', '/');
}
