using System.Linq;
using Nexaflow.Syntax;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Syntax;

/// <summary>
/// Structural editing of XML: XAML's own ids (x:Class, x:Name, x:Key, AutomationId), any XML-family file
/// through an XPath, and attributes.
/// </summary>
[TestClass]
[CoversNode("syntax-structural-edit")]
public class XmlStructuralEditTests
{
    internal const string View = """
        <UserControl x:Class="Demo.Views.MainView"
                     xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:local="clr-namespace:Demo.Views"
                     Loaded="OnLoaded">
            <UserControl.Resources>
                <ResourceDictionary>
                    <ResourceDictionary.MergedDictionaries>
                        <ResourceDictionary Source="Theme.xaml"/>
                    </ResourceDictionary.MergedDictionaries>
                    <!-- converts a flag -->
                    <BooleanToVisibilityConverter x:Key="BoolToVis"/>
                    <Style x:Key="Cell" TargetType="Border">
                        <EventSetter Event="MouseDown" Handler="OnCellDown"/>
                    </Style>
                </ResourceDictionary>
            </UserControl.Resources>
            <Grid x:Name="Root">
                <Button x:Name="Save" AutomationProperties.AutomationId="Main_Save" Click="OnSave"/>
                <Button AutomationProperties.AutomationId="Main_Cancel" Click="OnCancel">Cancel</Button>
                <StackPanel>
                    <TextBlock x:Name="Status" Text="Ready"/>
                </StackPanel>
                <Border x:Key="Cell"/>
            </Grid>
        </UserControl>
        """;

    // ── The addressing every edit relies on ─────────────────────────────────

    /// <summary>
    /// Every id the outline gives a view, exactly — written against the extractor before the walk that decides
    /// them was shared with the editor, so the two cannot quietly disagree about what <c>K:Cell#1</c> means.
    /// </summary>
    [TestMethod]
    public void OutlinePaths_ArePinned_ForAViewWithDuplicatesHandlersAndDoubleHandles()
    {
        var outline = new CodeStructureExtractor().Extract("xaml", View);
        var actual = outline.Types
            .SelectMany(t => new[] { $"{t.AstPath} {t.Line}-{t.EndLine}" }
                .Concat(t.Members.Select(m => $"  {m.AstPath} {m.Line}")))
            .Concat(outline.Imports.Select(i => $"import {i.Text}"))
            .ToList();

        string[] expected =
        [
            "T:MainView 1-26",
            "  T:MainView/M:OnLoaded 5",
            "K:BoolToVis 12-12",
            "K:Cell 13-15",
            "  K:Cell/M:OnCellDown 14",
            "N:Root 18-25",
            "N:Save 19-19",
            "  N:Save/M:OnSave 19",
            "A:Main_Save 19-19",
            "A:Main_Cancel 20-20",
            "  A:Main_Cancel/M:OnCancel 20",
            "N:Status 22-22",
            "K:Cell#1 24-24",
            "import xmlns:local=clr-namespace:Demo.Views",
            "import Theme.xaml",
        ];

        CollectionAssert.AreEqual(expected, actual, "actual:\n" + string.Join("\n", actual));
    }

    private static string Applied(StructuralEdit.Result r)
    {
        Assert.IsTrue(r.Ok, r.Message);
        Assert.IsTrue(new DeclarationAnchors().ParsesCleanly("xaml", r.NewText!), "and the result still parses");
        return r.NewText!;
    }

    private static void AssertLine(string text, string expected) =>
        Assert.IsTrue(SourceText.Of(text).Lines.Contains(expected),
                      $"expected a line exactly \"{expected}\", got:\n  " + string.Join("\n  ", SourceText.Of(text).Lines));

    private static StructuralEdit.Result Xaml(string path, StructuralEdit.Op op, string? text = null,
                                              StructuralEdit.Options? options = null, string? renameTo = null) =>
        StructuralEdit.Apply("xaml", View, path, op, text, options, renameTo);

    /// <summary>Every id the outline lists is one an edit can address: replacing each element with its own text
    /// changes nothing, which is the edit and the outline agreeing about what every id means.</summary>
    [TestMethod]
    public void EveryAnchorPath_IsEditable_AndSelfReplaceIsANoOp()
    {
        var elementIds = StructuralEdit.Declarations("xaml", View).Where(d => !d.AstPath.Contains("/M:")).ToList();
        Assert.IsTrue(elementIds.Count >= 9);

        foreach (var id in elementIds)
            Assert.AreEqual(View, Applied(Xaml(id.AstPath, StructuralEdit.Op.Replace, XmlSelf(id.AstPath))), id.AstPath);
    }

    /// <summary>The lines an id's element occupies, taken out of the file as a caller would copy them — the
    /// indentation they share removed, the shape between them kept.</summary>
    private static string XmlSelf(string astPath)
    {
        var d = StructuralEdit.Declarations("xaml", View).First(x => x.AstPath == astPath);
        var lines = SourceText.Of(View).Lines.Skip(d.Line - 1).Take(d.EndLine - d.Line + 1).ToList();
        var common = SourceText.IndentOf(lines[0]);
        return string.Join("\n", lines.Select(l => l.StartsWith(common, StringComparison.Ordinal) ? l[common.Length..] : l));
    }

    // ── By id ────────────────────────────────────────────────────────────────

    /// <summary>The reported failure: the outline listed K:BoolToVis, and every edit to it said "no declaration named
    /// BoolToVis", because the XML grammar has no name field for the finder to look in.</summary>
    [TestMethod]
    public void XamlKeyId_Replace_Succeeds_AndKeepsTheCommentAbove()
    {
        var text = Applied(Xaml("K:BoolToVis", StructuralEdit.Op.Replace, "<local:FlagConverter x:Key=\"BoolToVis\"/>"));

        AssertLine(text, "            <!-- converts a flag -->");
        AssertLine(text, "            <local:FlagConverter x:Key=\"BoolToVis\"/>");
    }

    [TestMethod]
    public void DuplicateKey_HashOneAddressesTheSecondOccurrence()
    {
        var text = Applied(Xaml("K:Cell#1", StructuralEdit.Op.SetAttribute, "4",
                                new StructuralEdit.Options(Attribute: "Padding")));

        AssertLine(text, "        <Border x:Key=\"Cell\" Padding=\"4\"/>");
        AssertLine(text, "            <Style x:Key=\"Cell\" TargetType=\"Border\">");
    }

    [TestMethod]
    public void HandlerMemberId_IsRefused_PointingAtAttributeOps()
    {
        var result = Xaml("N:Save/M:OnSave", StructuralEdit.Op.Delete);

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "set-attribute");
    }

    [TestMethod]
    public void Delete_TakesTheAttachedComment_AndOnlyThatElement()
    {
        var text = Applied(Xaml("K:BoolToVis", StructuralEdit.Op.Delete));

        Assert.IsFalse(text.Contains("converts a flag"), "the comment belonged to it");
        Assert.IsFalse(text.Contains("BoolToVis"));
        AssertLine(text, "            <Style x:Key=\"Cell\" TargetType=\"Border\">");
    }

    [TestMethod]
    public void Delete_OfTheRoot_IsRefused() =>
        Assert.IsFalse(Xaml("T:MainView", StructuralEdit.Op.Delete).Ok);

    [TestMethod]
    public void InsertAfter_AddsNoBlankLine_WhenSiblingsHaveNone()
    {
        var text = Applied(Xaml("N:Save", StructuralEdit.Op.InsertAfter, "<Button x:Name=\"Load\"/>"));

        var lines = SourceText.Of(text).Lines.ToList();
        var save = lines.FindIndex(l => l.Contains("x:Name=\"Save\""));
        Assert.AreEqual("        <Button x:Name=\"Load\"/>", lines[save + 1]);
    }

    [TestMethod]
    public void InsertBefore_GoesAboveTheAttachedComment()
    {
        var text = Applied(Xaml("K:BoolToVis", StructuralEdit.Op.InsertBefore, "<Style x:Key=\"First\"/>"));

        var lines = SourceText.Of(text).Lines.ToList();
        var comment = lines.FindIndex(l => l.Contains("converts a flag"));
        Assert.AreEqual("            <Style x:Key=\"First\"/>", lines[comment - 1]);
    }

    // ── Content ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Append_IntoSelfClosing_ExpandsWithOneIndentUnit()
    {
        var text = Applied(Xaml("K:BoolToVis", StructuralEdit.Op.Append, "<Setter Property=\"A\" Value=\"1\"/>"));

        AssertLine(text, "            <BooleanToVisibilityConverter x:Key=\"BoolToVis\">");
        AssertLine(text, "                <Setter Property=\"A\" Value=\"1\"/>");
        AssertLine(text, "            </BooleanToVisibilityConverter>");
    }

    [TestMethod]
    public void Append_AfterLastChild_UsesTheChildrensIndent()
    {
        var text = Applied(Xaml("N:Root", StructuralEdit.Op.Append, "<Label x:Name=\"Footer\"/>"));

        var lines = SourceText.Of(text).Lines.ToList();
        var footer = lines.IndexOf("        <Label x:Name=\"Footer\"/>");
        Assert.IsTrue(footer > 0, "indented as the Grid's other children are");
        Assert.AreEqual("    </Grid>", lines[footer + 1]);
    }

    [TestMethod]
    public void Append_KeepsCrlf()
    {
        var crlf = View.Replace("\r\n", "\n").Replace("\n", "\r\n");
        var result = StructuralEdit.Apply("xaml", crlf, "N:Root", StructuralEdit.Op.Append, "<Label/>\n<Label/>");

        var text = Applied(result);
        Assert.AreEqual(text.Split("\r\n").Length - 1, text.Count(c => c == '\n'), "every line break is CRLF");
    }

    [TestMethod]
    public void Body_ReplacesContent_StartTagByteForByte()
    {
        var text = Applied(Xaml("A:Main_Cancel", StructuralEdit.Op.Body, "Close"));

        AssertLine(text, "        <Button AutomationProperties.AutomationId=\"Main_Cancel\" Click=\"OnCancel\">Close</Button>");
    }

    [TestMethod]
    public void Body_OfSelfClosing_Expands()
    {
        var text = Applied(Xaml("N:Status", StructuralEdit.Op.Body, "<Run Text=\"x\"/>"));

        AssertLine(text, "            <TextBlock x:Name=\"Status\" Text=\"Ready\">");
        AssertLine(text, "                <Run Text=\"x\"/>");
        AssertLine(text, "            </TextBlock>");
    }

    [TestMethod]
    public void Signature_NewTagName_RewritesTheEndTag_WithNote()
    {
        var result = Xaml("N:Root", StructuralEdit.Op.Signature, "<DockPanel x:Name=\"Root\">");

        var text = Applied(result);
        AssertLine(text, "    <DockPanel x:Name=\"Root\">");
        AssertLine(text, "    </DockPanel>");
        Assert.IsTrue(result.Notes.Any(n => n.Contains("end tag")), string.Join(" | ", result.Notes));
    }

    [TestMethod]
    public void Signature_OnSelfClosing_RequiresSelfClosingPayload()
    {
        var result = Xaml("N:Save", StructuralEdit.Op.Signature, "<Button x:Name=\"Save\">");

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "closes itself");
    }

    // ── Names ────────────────────────────────────────────────────────────────

    /// <summary>A XAML element is known by its id — the generated field, the resource lookup, the journey's click —
    /// so renaming what an id addressed changes the id. The tag is signature's.</summary>
    [TestMethod]
    public void Rename_OnXNameId_ChangesTheValueNotTheTag()
    {
        var result = Xaml("N:Save", StructuralEdit.Op.Rename, renameTo: "Store");

        var text = Applied(result);
        AssertLine(text, "        <Button x:Name=\"Store\" AutomationProperties.AutomationId=\"Main_Save\" Click=\"OnSave\"/>");
        Assert.IsTrue(StructuralEdit.Declarations("xaml", text).Any(d => d.AstPath == "N:Store"));
    }

    [TestMethod]
    public void Rename_ToAnExistingXName_IsRefused()
    {
        var result = Xaml("N:Save", StructuralEdit.Op.Rename, renameTo: "Status");

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "unique");
    }

    [TestMethod]
    public void Rename_OnRootWithXClass_ChangesLastSegment_WithCodeBehindNote()
    {
        var result = Xaml("T:MainView", StructuralEdit.Op.Rename, renameTo: "HomeView");

        StringAssert.Contains(Applied(result), "x:Class=\"Demo.Views.HomeView\"");
        Assert.IsTrue(result.Notes.Any(n => n.Contains("code-behind")));
    }

    // ── Comments and substitution ────────────────────────────────────────────

    [TestMethod]
    public void Doc_WrapsBareText_AndReplacesAnExistingComment()
    {
        var text = Applied(Xaml("K:BoolToVis", StructuralEdit.Op.Doc, "hides what is off"));

        AssertLine(text, "            <!-- hides what is off -->");
        Assert.IsFalse(text.Contains("converts a flag"));
    }

    [TestMethod]
    public void Doc_RefusesDoubleHyphen() =>
        Assert.IsFalse(Xaml("K:BoolToVis", StructuralEdit.Op.Doc, "a -- b").Ok);

    [TestMethod]
    public void Substitute_IsBoundedToTheElement_AndSaysNothingAboutStrings()
    {
        var result = Xaml("N:Status", StructuralEdit.Op.Substitute, "Waiting", new StructuralEdit.Options(Find: "Ready"));

        AssertLine(Applied(result), "            <TextBlock x:Name=\"Status\" Text=\"Waiting\"/>");
        Assert.IsFalse(result.Notes.Any(n => n.Contains("string literal")), "an attribute value is not a literal");
    }

    // ── Attributes ───────────────────────────────────────────────────────────

    [TestMethod]
    public void SetAttribute_ChangesExistingValue_KeepingSingleQuotes()
    {
        const string single = "<Grid xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>\n    <Border x:Name='B' Padding='1'/>\n</Grid>\n";

        var text = Applied(StructuralEdit.Apply("xaml", single, "N:B", StructuralEdit.Op.SetAttribute, "2",
                                                new StructuralEdit.Options(Attribute: "Padding")));
        AssertLine(text, "    <Border x:Name='B' Padding='2'/>");
    }

    [TestMethod]
    public void SetAttribute_EscapesAmpLtAndQuote()
    {
        var text = Applied(Xaml("N:Status", StructuralEdit.Op.SetAttribute, "a & <b> \"c\"",
                                new StructuralEdit.Options(Attribute: "Text")));

        AssertLine(text, "            <TextBlock x:Name=\"Status\" Text=\"a &amp; &lt;b> &quot;c&quot;\"/>");
    }

    [TestMethod]
    public void SetAttribute_AddsOnSameLine_WhenAttributesShareALine()
    {
        var text = Applied(Xaml("N:Status", StructuralEdit.Op.SetAttribute, "Wrap",
                                new StructuralEdit.Options(Attribute: "TextWrapping")));

        AssertLine(text, "            <TextBlock x:Name=\"Status\" Text=\"Ready\" TextWrapping=\"Wrap\"/>");
    }

    [TestMethod]
    public void SetAttribute_AddsOnAlignedOwnLine_WhenOnePerLine()
    {
        var text = Applied(Xaml("T:MainView", StructuralEdit.Op.SetAttribute, "450",
                                new StructuralEdit.Options(Attribute: "Width")));

        AssertLine(text, "             Loaded=\"OnLoaded\"");
        AssertLine(text, "             Width=\"450\">");
    }

    [TestMethod]
    public void SetAttribute_WithNoAttributes_GoesAfterTheName()
    {
        var text = Applied(StructuralEdit.ApplyAt("xaml", View, StructuralEdit.Op.SetAttribute, "Vertical",
            new StructuralEdit.Options(At: "//StackPanel", Attribute: "Orientation")));

        AssertLine(text, "        <StackPanel Orientation=\"Vertical\">");
    }

    [TestMethod]
    public void RemoveAttribute_TakesItsLineWhenAlone()
    {
        var text = Applied(Xaml("T:MainView", StructuralEdit.Op.RemoveAttribute,
                                options: new StructuralEdit.Options(Attribute: "Loaded")));

        AssertLine(text, "             xmlns:local=\"clr-namespace:Demo.Views\">");
        Assert.IsFalse(text.Contains("OnLoaded"));
    }

    [TestMethod]
    public void RemoveAttribute_FirstOnTagLine_PullsNextUp()
    {
        const string aligned = "<Grid xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n    <Border x:Name=\"B\" Width=\"1\"\n            Height=\"2\"/>\n</Grid>\n";

        var text = Applied(StructuralEdit.ApplyAt("xaml", aligned, StructuralEdit.Op.RemoveAttribute, null,
            new StructuralEdit.Options(At: "//Border", Attribute: "x:Name")));
        AssertLine(text, "    <Border Width=\"1\"");
    }

    [TestMethod]
    public void RemoveAttribute_Missing_ListsExisting()
    {
        var result = Xaml("N:Status", StructuralEdit.Op.RemoveAttribute, options: new StructuralEdit.Options(Attribute: "Margin"));

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "x:Name, Text");
    }

    [TestMethod]
    public void AttributeOps_AreRefusedForCSharp()
    {
        var result = StructuralEdit.Apply("c-sharp", "class C { void M() { } }", "T:C", StructuralEdit.Op.SetAttribute, "x",
                                          new StructuralEdit.Options(Attribute: "Y"));
        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "XML");
    }

    // ── Refusals ─────────────────────────────────────────────────────────────

    /// <summary>A XAML element declaring clr-namespace xmlns is an element, not a whole file — the code check
    /// that counts imports would have called it one.</summary>
    [TestMethod]
    public void ElementPayloadWithClrXmlns_IsNotMistakenForAWholeFile() =>
        Applied(Xaml("N:Status", StructuralEdit.Op.Replace,
                     "<local:Badge xmlns:local=\"clr-namespace:Demo\" x:Name=\"Status\"/>"));

    [TestMethod]
    public void WholeXmlFilePayload_IsRefused()
    {
        var result = Xaml("N:Status", StructuralEdit.Op.Replace, "<?xml version=\"1.0\"?>\n<TextBlock/>");

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "whole file");
    }

    [TestMethod]
    public void XmlEditThatBreaksTheDocument_IsRefused() =>
        Assert.IsFalse(Xaml("N:Status", StructuralEdit.Op.Replace, "<TextBlock x:Name=\"Status\">").Ok);

    [TestMethod]
    public void ImportOnXml_IsRefused() =>
        Assert.IsFalse(Xaml("T:MainView", StructuralEdit.Op.Import, "using X;").Ok);
}

/// <summary>
/// Elements nothing names, found by XPath — a project file's references above all, where no element has an id
/// and only an attribute tells one from another.
/// </summary>
[TestClass]
[CoversNode("syntax-structural-edit")]
public class XmlPathTests
{
    private const string Project = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="NAudio" Version="2.2.1" />
            <PackageReference Include="Markdig" Version="0.40.0" />
          </ItemGroup>
          <ItemGroup Label="Projects">
            <ProjectReference Include="..\Common\Common.csproj" />
          </ItemGroup>
        </Project>
        """;

    private static StructuralEdit.Result At(string path, StructuralEdit.Op op, string? text = null,
                                            string? attribute = null, bool all = false, string source = Project) =>
        StructuralEdit.ApplyAt("xml", source, op, text, new StructuralEdit.Options(At: path, Attribute: attribute, AllOccurrences: all));

    private static string Applied(StructuralEdit.Result r)
    {
        Assert.IsTrue(r.Ok, r.Message);
        return r.NewText!;
    }

    private static void AssertLine(string text, string expected) =>
        Assert.IsTrue(SourceText.Of(text).Lines.Contains(expected),
                      $"expected a line exactly \"{expected}\", got:\n  " + string.Join("\n  ", SourceText.Of(text).Lines));

    [TestMethod]
    public void AbsolutePath_AddressesOneElement() =>
        AssertLine(Applied(At("/Project/PropertyGroup/TargetFramework", StructuralEdit.Op.Body, "net11.0")),
                   "    <TargetFramework>net11.0</TargetFramework>");

    [TestMethod]
    public void AttributePredicate_SetsTheVersionOfOnePackage()
    {
        var text = Applied(At("//PackageReference[@Include='NAudio']", StructuralEdit.Op.SetAttribute, "3.0.0", "Version"));

        AssertLine(text, "    <PackageReference Include=\"NAudio\" Version=\"3.0.0\" />");
        AssertLine(text, "    <PackageReference Include=\"Markdig\" Version=\"0.40.0\" />");
    }

    [TestMethod]
    public void Positional_IsPerParent_AsInXPath() =>
        AssertLine(Applied(At("/Project/ItemGroup[2]/ProjectReference", StructuralEdit.Op.SetAttribute, "all", "PrivateAssets")),
                   "    <ProjectReference Include=\"..\\Common\\Common.csproj\" PrivateAssets=\"all\" />");

    [TestMethod]
    public void AttributeExistenceAndChildPredicates_Filter()
    {
        AssertLine(Applied(At("//ItemGroup[@Label]", StructuralEdit.Op.SetAttribute, "Refs", "Label")),
                   "  <ItemGroup Label=\"Refs\">");
        Assert.IsTrue(At("//ItemGroup[PackageReference]", StructuralEdit.Op.Append, "<PackageReference Include=\"X\" />").Ok,
                      "only the first ItemGroup holds packages, so this is one element");
    }

    [TestMethod]
    public void Append_AddsAReference_AtTheChildrensIndent()
    {
        var text = Applied(At("//ItemGroup[PackageReference]", StructuralEdit.Op.Append, "<PackageReference Include=\"Serilog\" Version=\"4.0.0\" />"));

        var lines = SourceText.Of(text).Lines.ToList();
        var added = lines.IndexOf("    <PackageReference Include=\"Serilog\" Version=\"4.0.0\" />");
        Assert.IsTrue(added > 0, text);
        Assert.AreEqual("  </ItemGroup>", lines[added + 1]);
    }

    [TestMethod]
    public void EntityInAttributeValue_IsDecodedForComparison()
    {
        const string source = "<Root>\n  <Item Name=\"a &amp; b\" />\n</Root>\n";
        Assert.IsTrue(At("//Item[@Name='a & b']", StructuralEdit.Op.Delete, source: source).Ok);
    }

    [TestMethod]
    public void UnprefixedStepMatchesAnyPrefix_PrefixedMatchesExactly()
    {
        const string source = "<Root xmlns:w=\"urn:w\">\n  <w:Item />\n</Root>\n";
        Assert.IsTrue(At("//Item", StructuralEdit.Op.Delete, source: source).Ok);
        Assert.IsTrue(At("//w:Item", StructuralEdit.Op.Delete, source: source).Ok);
        Assert.IsFalse(At("//v:Item", StructuralEdit.Op.Delete, source: source).Ok);
    }

    [TestMethod]
    public void NoMatch_NamesTheCandidatesThePredicateRejected()
    {
        var result = At("//PackageReference[@Include='Serilog']", StructuralEdit.Op.Delete);

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "NAudio");
        StringAssert.Contains(result.Message, "Markdig");
    }

    [TestMethod]
    public void NoMatch_SaysHowFarThePathGot()
    {
        var result = At("/Project/ItemGroup/Compile", StructuralEdit.Op.Delete);

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "'/Project/ItemGroup' matches 2 element(s)");
        StringAssert.Contains(result.Message, "PackageReference ×2");
    }

    [TestMethod]
    public void SeveralMatches_ListCanonicalPaths_AndSuggestADistinguishingAttribute()
    {
        var result = At("//PackageReference", StructuralEdit.Op.Replace, "<PackageReference Include=\"X\" />");

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "/Project/ItemGroup[1]/PackageReference[1]");
        StringAssert.Contains(result.Message, "/Project/ItemGroup[1]/PackageReference[2]");
        StringAssert.Contains(result.Message, "[@Include='…']");
    }

    [TestMethod]
    public void All_AppliesToDeleteAndSetAttribute_ButReplaceIsRefused()
    {
        var text = Applied(At("//PackageReference", StructuralEdit.Op.SetAttribute, "all", "PrivateAssets", all: true));
        Assert.AreEqual(2, text.Split("PrivateAssets=\"all\"").Length - 1);

        var gone = Applied(At("//ItemGroup", StructuralEdit.Op.Delete, all: true));
        Assert.IsFalse(gone.Contains("ItemGroup"));

        Assert.IsFalse(At("//PackageReference", StructuralEdit.Op.Replace, "<X/>", all: true).Ok);
    }

    [DataTestMethod]
    [DataRow("Project")]
    [DataRow("/Project[")]
    [DataRow("/Project[@Sdk=x]")]
    [DataRow("/Project[0]")]
    [DataRow("(//Project)[1]")]
    [DataRow("//a|//b")]
    public void SyntaxErrors_PointAtTheColumn(string path)
    {
        var result = At(path, StructuralEdit.Op.Delete);

        Assert.IsFalse(result.Ok, path);
        StringAssert.Contains(result.Message, "^");
    }

    [TestMethod]
    public void At_OnNonXmlGrammar_IsRefused() =>
        Assert.IsFalse(StructuralEdit.ApplyAt("c-sharp", "class C {}", StructuralEdit.Op.Delete, null,
                                              new StructuralEdit.Options(At: "//C")).Ok);

    [TestMethod]
    public void At_TheLineAndColumnOfABuildWarning_IsTheElementThere()
    {
        // Where NXUI001 puts it: the second button's name.
        var result = StructuralEdit.ApplyAt("xaml", XmlStructuralEditTests.View, StructuralEdit.Op.SetAttribute, "Main_Other",
            new StructuralEdit.Options(At: "(20,10)", Attribute: "AutomationProperties.AutomationId"));

        StringAssert.Contains(Applied(result), "<Button AutomationProperties.AutomationId=\"Main_Other\" Click=\"OnCancel\">Cancel</Button>");
        Assert.IsTrue(result.Notes.Any(n => n.Contains("/UserControl/Grid/Button[2]", StringComparison.Ordinal)),
                      "the answer names the path, for whatever comes next");
    }

    [TestMethod]
    public void At_ABareLine_IsTheElementOpeningOnIt_AndSeveralAreNamedRatherThanGuessed()
    {
        StringAssert.Contains(Applied(StructuralEdit.ApplyAt("xaml", XmlStructuralEditTests.View, StructuralEdit.Op.RemoveAttribute, null,
                                  new StructuralEdit.Options(At: "22", Attribute: "Text"))),
                              "<TextBlock x:Name=\"Status\"/>");

        var both = StructuralEdit.ApplyAt("xml", "<Grid><Button/></Grid>\n", StructuralEdit.Op.Delete, null,
                                          new StructuralEdit.Options(At: "1"));
        Assert.IsFalse(both.Ok);
        StringAssert.Contains(both.Message, "/Grid/Button");
    }

    [TestMethod]
    public void At_WorksOnProjectFileEditedAsXml()
    {
        Assert.AreEqual("xml", TreeSitterLanguages.ForEdit("src/App/App.csproj"));
        Assert.IsTrue(StructuralEdit.ApplyAt(TreeSitterLanguages.ForEdit("App.csproj")!, Project,
                          StructuralEdit.Op.RemoveAttribute, null,
                          new StructuralEdit.Options(At: "//PackageReference[@Include='Markdig']", Attribute: "Version")).Ok);
    }
}
