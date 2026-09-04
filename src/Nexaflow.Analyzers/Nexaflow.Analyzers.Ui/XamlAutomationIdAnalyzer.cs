using System;
using System.Collections.Immutable;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Nexaflow.Analyzers.Ui;

/// <summary>
/// <b>NXUI001</b> — a button in a view has to carry an <c>AutomationProperties.AutomationId</c>.
/// <para>
/// A button is the thing a journey test clicks, and an id is the only reliable way to find one. Name is the
/// visible label, so it changes when the copy changes and is absent entirely on the icon-only buttons that
/// make up most of this shell's chrome; position is whatever the layout happens to be that week. An id is a
/// contract with the test, and the failure mode without one is not a red test but a journey that can never be
/// written — so the gap is invisible until somebody tries.
/// </para>
/// <para>
/// XAML is not something Roslyn compiles, so this reads the views as <see cref="AdditionalText"/>: the wiring
/// in <c>Directory.Build.targets</c> hands every <c>Page</c>/<c>ApplicationDefinition</c> item to the compiler
/// for exactly this. It is a warning, deliberately — there is a real backlog of un-idded buttons and turning
/// it into an error would stop the build rather than shrink the backlog.
/// </para>
/// <para>
/// Two exemptions, both because the id would be meaningless rather than merely inconvenient. A button inside
/// a <c>ControlTemplate</c> is another control's chrome — the id belongs on the templated control, which UIA
/// surfaces instead. And a property element (<c>&lt;Button.Content&gt;</c>) is not a button at all.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class XamlAutomationIdAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "Ui";

    /// <summary>The attached property, written in XAML exactly as it is read here — dot and all.</summary>
    private const string AutomationIdAttribute = "AutomationProperties.AutomationId";

    private static readonly DiagnosticDescriptor MissingAutomationId = new(
        "NXUI001",
        "Button has no AutomationProperties.AutomationId",
        "<{0}> has no AutomationProperties.AutomationId — a journey test cannot find it. Give it a stable id (Feature_Action), or move it inside a ControlTemplate if it is another control's chrome.",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "Every button in a view needs a stable AutomationProperties.AutomationId: it is the only handle a UI journey can use to click it, since the visible name is copy that changes and icon-only buttons have none.",
        // The subject is an AdditionalFile, so the only place to look at it is compilation end. The tag is
        // how Roslyn is told that: without it the IDE assumes a per-symbol rule and drops the report.
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(MissingAutomationId);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        // Compilation-level, because the subject is a file the compilation carries rather than anything in a
        // syntax tree. Reported against the .xaml path itself, so the squiggle lands where the fix goes.
        context.RegisterCompilationAction(Analyze);
    }

    private static void Analyze(CompilationAnalysisContext ctx)
    {
        foreach (var file in ctx.Options.AdditionalFiles)
        {
            if (!IsView(file.Path)) continue;
            ctx.CancellationToken.ThrowIfCancellationRequested();

            var text = file.GetText(ctx.CancellationToken);
            if (text is null) continue;

            XDocument doc;
            try
            {
                doc = XDocument.Parse(text.ToString(), LoadOptions.SetLineInfo);
            }
            catch (XmlException)
            {
                // A view that does not parse is the XAML compiler's complaint to make, not ours. Reporting it
                // twice, in a rule about automation ids, would only bury the real message.
                continue;
            }

            foreach (var element in doc.Descendants())
            {
                if (!IsButton(element) || HasAutomationId(element) || IsControlChrome(element)) continue;
                ctx.ReportDiagnostic(Diagnostic.Create(
                    MissingAutomationId, LocationOf(file.Path, text, element), element.Name.LocalName));
            }
        }
    }

    /// <summary>
    /// A hand-authored view. The obj/ filter matters: the WPF targets generate a copy of every page under
    /// obj\ during compilation, and flagging both would double every diagnostic and point half of them at a
    /// file nobody can edit.
    /// </summary>
    private static bool IsView(string path) =>
        path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
        && !path.Replace('\\', '/').Contains("/obj/")
        && !path.Replace('\\', '/').Contains("/bin/");

    /// <summary>
    /// Button, ToggleButton, RepeatButton, RadioButton — and any control the repo names <c>…Button</c>, which
    /// is the point of matching on the suffix rather than a fixed list: a custom button is still a button, and
    /// a rule that only knew the framework's four would quietly stop applying the moment one was wrapped.
    /// A local name carrying a dot is a property element (<c>&lt;Button.Style&gt;</c>), never an instance.
    /// </summary>
    private static bool IsButton(XElement element) =>
        element.Name.LocalName.EndsWith("Button", StringComparison.Ordinal)
        && element.Name.LocalName.IndexOf('.') < 0;

    private static bool HasAutomationId(XElement element) =>
        element.Attributes().Any(a =>
            string.Equals(a.Name.LocalName, AutomationIdAttribute, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(a.Value));

    /// <summary>
    /// Inside a <c>ControlTemplate</c> the button is part of some other control's visual tree. UIA reports the
    /// templated control, so an id here would name a part the test never addresses.
    /// </summary>
    private static bool IsControlChrome(XElement element) =>
        element.Ancestors().Any(a => a.Name.LocalName == "ControlTemplate");

    /// <summary>
    /// The element's opening tag name, as a location in a file the compilation only carries — an external
    /// location, which is what an AdditionalFile diagnostic gets. Falls back to the whole file if the parser
    /// gave no line info, because a diagnostic in the wrong place is worse than one without a precise caret.
    /// </summary>
    private static Location LocationOf(string path, SourceText text, XElement element)
    {
        var info = (IXmlLineInfo)element;
        if (!info.HasLineInfo() || info.LineNumber <= 0 || info.LineNumber > text.Lines.Count)
            return Location.Create(path, new TextSpan(0, 0), new LinePositionSpan());

        var line = text.Lines[info.LineNumber - 1];
        var column = Math.Max(0, info.LinePosition - 1);
        var start = Math.Min(line.Start + column, line.End);
        var length = Math.Min(element.Name.LocalName.Length, line.End - start);
        var startPosition = new LinePosition(info.LineNumber - 1, column);
        return Location.Create(
            path,
            new TextSpan(start, length),
            new LinePositionSpan(startPosition, new LinePosition(info.LineNumber - 1, column + length)));
    }
}
