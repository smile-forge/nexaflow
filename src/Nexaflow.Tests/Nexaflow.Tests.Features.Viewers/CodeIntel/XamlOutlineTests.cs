using System.Linq;
using Nexaflow.Syntax;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.CodeIntel;

/// <summary>
/// Covers the XAML structure extractor. XAML is XML, so it parses with the <c>xml</c> grammar built from the
/// <c>external/tree-sitter-xml</c> submodule; the <c>xaml</c> id exists so this extractor can read WPF meaning
/// out of the same tree — <c>x:Class</c>, <c>x:Name</c>, <c>x:Key</c>, <c>AutomationProperties.AutomationId</c>
/// and event handlers.
/// </summary>
[TestClass]
public class XamlOutlineTests
{
    private const string X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static CodeOutline Outline(string xaml) => new CodeStructureExtractor().Extract("xaml", xaml);

    private static string View(string body, string prefix = "x") =>
        $"""
         <UserControl x:Class="Demo.Views.MainView"
                      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:{prefix}="{X}">
           {body}
         </UserControl>
         """.Replace("x:Class", prefix + ":Class");

    // ── the grammar itself ───────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void XamlAndXml_ResolveToAGrammar()
    {
        Assert.AreEqual("xaml", TreeSitterLanguages.ForFile("View.xaml"));
        Assert.AreEqual("xml", TreeSitterLanguages.ForFile("data.xml"));
        Assert.IsTrue(TreeSitterLanguages.IsCode("View.xaml"));

        // .csproj stays out: the graph's structured layer owns it, and overlapping the two file sets would
        // put it through both.
        Assert.IsNull(TreeSitterLanguages.ForFile("Thing.csproj"));
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void HighlightQueryCompiles_ForBothIds()
    {
        // TryCreate returns null when the query fails to compile or the native grammar is missing — and that
        // failure is silent, so it is worth asserting rather than discovering as "XAML stopped outlining".
        using var xaml = CodeHighlighter.TryCreate("xaml");
        using var xml = CodeHighlighter.TryCreate("xml");
        Assert.IsNotNull(xaml, "the xaml highlight query must compile against the xml grammar");
        Assert.IsNotNull(xml, "the xml highlight query must compile");
    }

    // ── anchors ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void RootIsNamedByXClass()
    {
        var t = Outline(View("<Grid />")).Types.Single(t => t.AstPath.StartsWith("T:"));
        Assert.AreEqual("MainView", t.Name);
        Assert.AreEqual("T:MainView", t.AstPath);
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void ResourceDictionaryWithoutXClass_IsNamedByItsRootElement()
    {
        var t = Outline($"""<ResourceDictionary xmlns:x="{X}"><Style x:Key="Fancy" /></ResourceDictionary>""")
                .Types.Single(t => t.AstPath.StartsWith("T:"));
        Assert.AreEqual("ResourceDictionary", t.Name);
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void NameKeyAndAutomationId_EachBecomeAnAnchor()
    {
        var o = Outline(View("""
              <Grid x:Name="Root">
                <Grid.Resources><Style x:Key="Fancy" /></Grid.Resources>
                <Button AutomationProperties.AutomationId="Go_Button" />
              </Grid>
            """));
        var paths = o.Types.Select(t => t.AstPath).ToList();
        CollectionAssert.Contains(paths, "N:Root");
        CollectionAssert.Contains(paths, "K:Fancy");
        CollectionAssert.Contains(paths, "A:Go_Button");
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void ElementWithBothNameAndAutomationId_GetsBothHandles()
    {
        // Two real identities, looked up two different ways (code-behind by name, UI journeys by id), so
        // neither may be dropped.
        var paths = Outline(View("""<Button x:Name="Send" AutomationProperties.AutomationId="Send_Button" />"""))
                    .Types.Select(t => t.AstPath).ToList();
        CollectionAssert.Contains(paths, "N:Send");
        CollectionAssert.Contains(paths, "A:Send_Button");
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void BoundAutomationId_IsNotAnAnchor()
    {
        var paths = Outline(View("""<Button AutomationProperties.AutomationId="{Binding AutomationId}" />"""))
                    .Types.Select(t => t.AstPath);
        Assert.IsFalse(paths.Any(p => p.StartsWith("A:")), "a bound id names nothing at author time");
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void DirectivesResolveByNamespace_NotByTheXPrefix()
    {
        // A document may bind the XAML namespace to any prefix; assuming "x:" would silently miss these.
        var paths = Outline(View("""<Grid xaml:Name="Root" />""", prefix: "xaml")).Types.Select(t => t.AstPath);
        CollectionAssert.Contains(paths.ToList(), "N:Root");
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void RepeatedNameInSeparateNamescopes_IsDisambiguated()
    {
        // Two ControlTemplates may each define x:Name="Bd" — both are legal and both must stay addressable.
        var paths = Outline(View("""
              <Grid.Resources>
                <ControlTemplate x:Key="A"><Border x:Name="Bd" /></ControlTemplate>
                <ControlTemplate x:Key="B"><Border x:Name="Bd" /></ControlTemplate>
              </Grid.Resources>
            """)).Types.Select(t => t.AstPath).ToList();
        CollectionAssert.Contains(paths, "N:Bd");
        CollectionAssert.Contains(paths, "N:Bd#1");
    }

    // ── handlers ─────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void EventHandler_BecomesAMethodMember()
    {
        var t = Outline(View("""<Button x:Name="Send" Click="OnSendClick" />"""))
                .Types.Single(t => t.AstPath == "N:Send");
        var m = t.Members.Single();
        Assert.AreEqual("OnSendClick", m.Name);
        Assert.AreEqual("N:Send/M:OnSendClick", m.AstPath);
        Assert.AreEqual(OutlineKind.Method, m.Kind);
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void HandlerOnAnUnnamedElement_AttachesToTheNearestAnchor()
    {
        // Most buttons carry a Click and no x:Name; dropping those would lose the majority of handlers.
        var t = Outline(View("""<Grid x:Name="Root"><Button Click="OnGo" /></Grid>"""))
                .Types.Single(t => t.AstPath == "N:Root");
        Assert.AreEqual("OnGo", t.Members.Single().Name);
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void PropertyValuesAreNotMistakenForHandlers()
    {
        var t = Outline(View("""
              <Button x:Name="Send" Content="Go" Visibility="Collapsed" IsDefault="True"
                      HorizontalAlignment="Stretch" Command="{Binding SendCommand}" />
            """)).Types.Single(t => t.AstPath == "N:Send");
        Assert.AreEqual(0, t.Members.Count, "identifier-valued properties are not events");
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void AttachedAndSetterHandlerForms_AreDetected()
    {
        var o = Outline(View("""
              <Grid x:Name="Root" ScrollViewer.ScrollChanged="OnScrolled">
                <Grid.Resources>
                  <Style x:Key="S"><EventSetter Event="Click" Handler="OnRowClick" /></Style>
                </Grid.Resources>
              </Grid>
            """));
        var names = o.Types.SelectMany(t => t.Members).Select(m => m.Name).ToList();
        CollectionAssert.Contains(names, "OnScrolled");   // attached event
        CollectionAssert.Contains(names, "OnRowClick");   // EventSetter Handler=
    }

    // ── spans, imports, robustness ───────────────────────────────────────────

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void AnchorCarriesTheElementSpan()
    {
        var t = Outline(View("""
              <Grid x:Name="Root">
                <Button />
              </Grid>
            """)).Types.Single(t => t.AstPath == "N:Root");
        Assert.AreEqual(4, t.Line);
        Assert.AreEqual(6, t.EndLine, "the span must cover the element, not just its opening tag");
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void ClrNamespaceXmlns_IsRecordedAsAnImport()
    {
        var o = Outline($"""
            <UserControl xmlns:x="{X}" xmlns:conv="clr-namespace:Demo.Converters" />
            """);
        Assert.IsTrue(o.Imports.Any(i => i.Text.Contains("clr-namespace:Demo.Converters")));
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void MalformedXaml_YieldsAPartialOutline_NotAnException()
    {
        // Error tolerance is the reason a real parser beats an XML DOM here: a half-typed file still outlines.
        var o = Outline($"""
            <UserControl x:Class="Demo.Views.MainView" xmlns:x="{X}">
              <Grid x:Name="Root">
                <Button Click="OnGo"
            """);
        Assert.IsTrue(o.Types.Any(t => t.AstPath == "T:MainView"));
        Assert.IsTrue(o.Types.Any(t => t.AstPath == "N:Root"));
    }

    [TestMethod]
    [CoversNode("syntax-outline")]
    public void AstPathResolvesBackToTheLiveLine()
    {
        var xaml = View("""
              <Grid>
                <Button x:Name="Send" Click="OnSendClick" />
              </Grid>
            """);
        var ex = new CodeStructureExtractor();
        Assert.AreEqual(5, ex.ResolveLine("xaml", xaml, "N:Send"));
        Assert.AreEqual(5, ex.ResolveLine("xaml", xaml, "N:Send/M:OnSendClick"));
    }
}
