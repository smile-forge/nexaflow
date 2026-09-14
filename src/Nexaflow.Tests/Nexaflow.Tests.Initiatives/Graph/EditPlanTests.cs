using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Syntax;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Graph;

/// <summary>
/// Several edits planned as one, over files held in memory. What is on trial is the promise that makes a script worth
/// having: a later step sees what the earlier ones did, a failure anywhere leaves nothing to write, and a file touched
/// twice comes out as one change.
/// </summary>
[TestClass]
public class EditPlanTests
{
    private const string Calculator =
        "using System;\n" +
        "\n" +
        "namespace Maths;\n" +
        "\n" +
        "public class Calculator\n" +
        "{\n" +
        "    public int Add(int a, int b) => a + b;\n" +
        "\n" +
        "    public int Sub(int a, int b) => a - b;\n" +
        "}\n" +
        "\n" +
        "/// <summary>Formats results.</summary>\n" +
        "public class Printer\n" +
        "{\n" +
        "    public string Show(int n) => n.ToString();\n" +
        "}\n";

    private const string Store =
        "using System.Collections.Generic;\n" +
        "\n" +
        "namespace Storage;\n" +
        "\n" +
        "public class Store\n" +
        "{\n" +
        "    public void Keep(int n) { }\n" +
        "}\n";

    private static Dictionary<string, string?> Files() => new(StringComparer.Ordinal)
    {
        ["src/Calculator.cs"] = Calculator,
        ["src/Store.cs"]      = Store,
    };

    private static EditPlan.Outcome Run(Dictionary<string, string?> files, params EditPlan.Step[] steps) =>
        EditPlan.Run(new KnowledgeGraph(), steps, rel => files.GetValueOrDefault(rel), _ => "\n");

    private static string After(EditPlan.Outcome outcome, string rel) =>
        outcome.Files.Single(f => f.RelativePath == rel).After ?? throw new AssertFailedException($"{rel} was removed");

    [TestMethod]
    [CoversNode("graph-edit-plan")]
    public void ALaterStepSeesAnEarlierOne_AndAFileTouchedTwiceIsOneWrite()
    {
        var outcome = Run(Files(),
            new EditPlan.Edit("rename", "code:src/Calculator.cs#T:Calculator/M:Add", StructuralEdit.Op.Rename, null,
                              RenameTo: "Plus"),
            new EditPlan.Edit("body", "code:src/Calculator.cs#T:Calculator/M:Plus", StructuralEdit.Op.Substitute,
                              "checked(a + b)", new StructuralEdit.Options(Find: "a + b")));

        Assert.IsTrue(outcome.Ok, outcome.Message);
        Assert.AreEqual(1, outcome.Files.Count, "one file, however many steps touched it");
        StringAssert.Contains(After(outcome, "src/Calculator.cs"), "public int Plus(int a, int b) => checked(a + b);");
        Assert.AreEqual(Calculator, outcome.Files[0].Before, "before is the file as it was ahead of every step");
    }

    [TestMethod]
    [CoversNode("graph-edit-plan")]
    public void AFailingStep_LeavesNothingToWrite_AndSaysWhichStepItWas()
    {
        var outcome = Run(Files(),
            new EditPlan.Edit("line 1 (rename)", "code:src/Calculator.cs#T:Calculator/M:Add", StructuralEdit.Op.Rename,
                              null, RenameTo: "Plus"),
            new EditPlan.Edit("line 2 (delete)", "code:src/Store.cs#T:Store/M:Missing", StructuralEdit.Op.Delete, null));

        Assert.IsFalse(outcome.Ok);
        Assert.AreEqual(0, outcome.Files.Count, "the rename planned cleanly, and still is not written");
        StringAssert.StartsWith(outcome.Message, "line 2 (delete):");
    }

    [TestMethod]
    [CoversNode("graph-edit-plan")]
    public void ACreatedFile_IsEditableByTheNextStep()
    {
        var outcome = Run(Files(),
            new EditPlan.Create("create", "src/New.cs", "namespace N;\n\npublic class New\n{\n}\n"),
            new EditPlan.Edit("append", "code:src/New.cs#T:New", StructuralEdit.Op.Append, "public int Zero() => 0;"));

        Assert.IsTrue(outcome.Ok, outcome.Message);
        var created = outcome.Files.Single();
        Assert.IsNull(created.Before, "it did not exist before the plan");
        StringAssert.Contains(created.After, "    public int Zero() => 0;");
    }

    [TestMethod]
    [CoversNode("graph-edit-plan")]
    public void CreatingAFileThatExists_IsRefused()
    {
        var outcome = Run(Files(), new EditPlan.Create("create", "src/Store.cs", "namespace X;\n"));

        Assert.IsFalse(outcome.Ok);
        StringAssert.Contains(outcome.Message, "already exists");
    }

    [TestMethod]
    [CoversNode("graph-edit-move")]
    public void MovingATypeToANewFile_CarriesItsImportsAndNamespace()
    {
        var outcome = Run(Files(),
            new EditPlan.Move("move", "code:src/Calculator.cs#T:Printer", "file:src/Printing/Printer.cs"));

        Assert.IsTrue(outcome.Ok, outcome.Message);
        Assert.AreEqual(
            "using System;\n\nnamespace Maths;\n\n/// <summary>Formats results.</summary>\npublic class Printer\n{\n"
          + "    public string Show(int n) => n.ToString();\n}\n",
            After(outcome, "src/Printing/Printer.cs"));

        var left = After(outcome, "src/Calculator.cs");
        Assert.IsFalse(left.Contains("Printer", StringComparison.Ordinal), left);
        StringAssert.EndsWith(left, "    public int Sub(int a, int b) => a - b;\n}\n", "no gap left where it was");
    }

    [TestMethod]
    [CoversNode("graph-edit-move")]
    public void MovingTheOnlyType_RemovesTheFileItLeavesEmpty()
    {
        var outcome = Run(Files(), new EditPlan.Move("move", "code:src/Store.cs#T:Store", "file:src/Storage/Store.cs"));

        Assert.IsTrue(outcome.Ok, outcome.Message);
        Assert.IsNull(outcome.Files.Single(f => f.RelativePath == "src/Store.cs").After,
                      "a file holding nothing but its usings and namespace is not worth keeping");
        Assert.IsTrue(outcome.Steps.Single().Notes.Any(n => n.Contains("remap", StringComparison.Ordinal)),
                      "a moved type's snaplinks point at the old file, and the note says how to fix them");
    }

    [TestMethod]
    [CoversNode("graph-edit-move")]
    public void MovingAMemberIntoATypeInAnotherFile_BringsTheImportsItLacks()
    {
        var outcome = Run(Files(),
            new EditPlan.Move("move", "code:src/Calculator.cs#T:Calculator/M:Sub", "code:src/Store.cs#T:Store"));

        Assert.IsTrue(outcome.Ok, outcome.Message);
        var store = After(outcome, "src/Store.cs");
        StringAssert.Contains(store, "using System;\n");
        StringAssert.Contains(store, "    public void Keep(int n) { }\n\n    public int Sub(int a, int b) => a - b;\n}");
        Assert.IsTrue(outcome.Steps.Single().Notes.Any(n => n.Contains("namespace Maths to Storage", StringComparison.Ordinal)),
                      string.Join("\n", outcome.Steps.Single().Notes));
    }

    [TestMethod]
    [CoversNode("graph-edit-move")]
    public void MovingWithinOneFile_IsOneChange()
    {
        var outcome = Run(Files(),
            new EditPlan.Move("move", "code:src/Calculator.cs#T:Calculator/M:Sub", "code:src/Calculator.cs#T:Printer"));

        Assert.IsTrue(outcome.Ok, outcome.Message);
        var text = After(outcome, "src/Calculator.cs");
        Assert.IsTrue(text.IndexOf("Sub(", StringComparison.Ordinal) > text.IndexOf("class Printer", StringComparison.Ordinal), text);
    }

    [TestMethod]
    [CoversNode("graph-edit-move")]
    public void ACSharpMemberWithNoTypeToGoInto_IsRefused()
    {
        var outcome = Run(Files(), new EditPlan.Move("move", "code:src/Calculator.cs#T:Calculator/M:Add", "file:src/Loose.cs"));

        Assert.IsFalse(outcome.Ok);
        StringAssert.Contains(outcome.Message, "needs a type");
    }

    [TestMethod]
    [CoversNode("graph-edit-move")]
    public void MovingATypeIntoItself_IsRefused()
    {
        var outcome = Run(Files(), new EditPlan.Move("move", "code:src/Calculator.cs#T:Calculator", "code:src/Calculator.cs#T:Calculator"));

        Assert.IsFalse(outcome.Ok);
        StringAssert.Contains(outcome.Message, "into itself");
    }

    [TestMethod]
    [CoversNode("graph-edit-plan")]
    public void ARewrite_IsRefusedWhenAnEarlierStepChangedItsFile()
    {
        var renamed = Calculator.Replace("Add(", "Plus(");
        var outcome = Run(Files(),
            new EditPlan.Edit("first", "code:src/Calculator.cs#T:Calculator/M:Sub", StructuralEdit.Op.Rename, null, RenameTo: "Minus"),
            new EditPlan.Rewrite("second", "src/Calculator.cs", Calculator, renamed, "rename Add to Plus"));

        Assert.IsFalse(outcome.Ok, "worked out against text an earlier step has since changed");
        StringAssert.Contains(outcome.Message, "Put this step first");
    }

    [TestMethod]
    [CoversNode("graph-edit-plan")]
    public void ARewrite_ThatWouldNotParse_IsRefused()
    {
        var outcome = Run(Files(), new EditPlan.Rewrite("rewrite", "src/Store.cs", Store, Store.Replace("{ }", "{"), "break it"));

        Assert.IsFalse(outcome.Ok);
        StringAssert.Contains(outcome.Message, "unparseable");
    }
}
