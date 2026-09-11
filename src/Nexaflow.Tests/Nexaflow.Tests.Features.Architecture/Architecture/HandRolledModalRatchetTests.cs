using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// A modal a view builds for itself — a scrim wrapping a centred card — is the scaffold arch review §E1 found
/// copied fourteen times, drifting in width, border, button order and even the scrim's own colour. A
/// <i>question</i> belongs to the shell (<c>IShellServices.ConfirmAsync</c> / <c>ShowPrompt</c>); a <i>form</i>
/// belongs in <c>Visuals.Common</c>'s <c>ModalCard</c>. This is what stops a new one being hand-rolled beside
/// them — "migrate on touch" never fired because nothing told an agent editing a view that its overlay was debt.
///
/// <para>
/// <b>A ratchet, like <see cref="AutomationIdJourneyCoverageTests"/>.</b> The scaffolds that predate the rule
/// are listed in <see cref="BaselineFile"/>: a new one fails, and a listed one that has since moved onto
/// <c>ModalCard</c> fails until its line is deleted, so the list can only shrink. Each is keyed by its view and
/// the property its visibility binds (<c>…/ProductView.xaml#SettingsVisible</c>), which survives the view being
/// edited around it where a line number would not.
/// </para>
/// <para>
/// What counts: an element whose <c>Background</c> is a translucent literal (<c>#AARRGGBB</c> with alpha below
/// <c>FF</c>) or the <c>ScrimBrush</c> token, with a <c>Border</c> child centred both ways — the card. A veil
/// over a centred <i>message</i> (a loading overlay) has no card and is not counted. <c>Visuals.Common</c> is
/// skipped: <c>ModalCard</c>'s own template is the one sanctioned copy.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("whole-repo architecture guard; maps to no single product node")]
public class HandRolledModalRatchetTests
{
    private static readonly string Root = RepoRoot.Locate();

    /// <summary>The ratchet. One key per line; <c>#</c> starts a comment.</summary>
    private static string BaselineFile => Path.Combine(
        Root, "src", "Nexaflow.Tests", "Nexaflow.Tests.Features.Architecture", "Architecture",
        "hand-rolled-modals.txt");

    private static readonly Regex TranslucentLiteral =
        new(@"^#([0-9A-Fa-f]{2})[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    private static readonly Regex BoundPath =
        new(@"^\{Binding\s+(?:Path=)?([\w.\[\]]+)", RegexOptions.Compiled);

    [TestMethod]
    [TestCategory("Unit")]
    public void No_view_hand_rolls_a_modal()
    {
        var found = Scaffolds();
        var baseline = Baseline();

        var fresh = found.Keys
            .Where(key => !baseline.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.AreEqual(0, fresh.Count,
            $"{fresh.Count} view(s) build their own modal — a scrim around a centred card:\n"
          + string.Join("\n", fresh.Select(key => $"  {key}    (line {found[key]})"))
          + "\nA yes/no question or one line of text goes through IShellServices.ConfirmAsync / ShowPrompt;\n"
          + "a form goes in Visuals.Common's ModalCard (docs/Architecture.md, IShellServices section).");
    }

    /// <summary>
    /// The other half of the ratchet. A listed scaffold that has moved onto <c>ModalCard</c> has to leave the
    /// file — otherwise the list stops describing anything and a genuinely new scaffold can hide behind a stale
    /// line that happens to match.
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void The_baseline_has_no_stale_entries()
    {
        var found = Scaffolds();

        var gone = Baseline()
            .Where(key => !found.ContainsKey(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.AreEqual(0, gone.Count,
            $"{Path.GetFileName(BaselineFile)} is out of date — these no longer hand-roll a modal; delete the lines:\n"
          + string.Join("\n", gone.Select(key => $"  {key}")));
    }

    /// <summary>Every hand-rolled modal in a hand-authored view, keyed as the baseline keys it, with its line.</summary>
    private static Dictionary<string, int> Scaffolds()
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.xaml", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file)) continue;
            var relative = Path.GetRelativePath(Root, file).Replace('\\', '/');
            if (relative.StartsWith("src/Nexaflow.Visuals.Common/", StringComparison.Ordinal)) continue;

            var doc = TryLoad(file);
            if (doc is null) continue;

            foreach (var scrim in doc.Descendants().Where(IsScrimAroundACard))
                found.TryAdd(relative + "#" + KeyOf(scrim), LineOf(scrim));
        }
        return found;
    }

    /// <summary>Malformed XAML is not this test's to judge — the build reports it.</summary>
    private static XDocument? TryLoad(string file)
    {
        try { return XDocument.Load(file, LoadOptions.SetLineInfo); }
        catch (XmlException) { return null; }
    }

    private static bool IsScrimAroundACard(XElement element) =>
        IsScrim(element.Attribute("Background")?.Value) && element.Elements().Any(IsCentredCard);

    private static bool IsScrim(string? background)
    {
        if (background is null) return false;
        if (background.Contains("ScrimBrush", StringComparison.Ordinal)) return true;
        var literal = TranslucentLiteral.Match(background.Trim());
        return literal.Success && !literal.Groups[1].Value.Equals("FF", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCentredCard(XElement element) =>
        element.Name.LocalName == "Border"
        && element.Attribute("HorizontalAlignment")?.Value == "Center"
        && element.Attribute("VerticalAlignment")?.Value == "Center";

    /// <summary>
    /// The property the scaffold's visibility binds — stable across edits — else its x:Name, else <c>root</c>
    /// when it is its control's whole content (the host decides when it shows, as <c>RibbonEditor</c>'s does),
    /// else its line.
    /// </summary>
    private static string KeyOf(XElement scrim)
    {
        var bound = BoundPath.Match(scrim.Attribute("Visibility")?.Value ?? string.Empty);
        if (bound.Success) return bound.Groups[1].Value;
        var name = scrim.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name");
        if (name is not null) return name.Value;
        return scrim.Parent is not null && scrim.Parent == scrim.Document?.Root ? "root" : "L" + LineOf(scrim);
    }

    private static int LineOf(XElement element) => ((IXmlLineInfo)element).LineNumber;

    private static HashSet<string> Baseline()
    {
        if (!File.Exists(BaselineFile)) return new HashSet<string>(StringComparer.Ordinal);
        return File.ReadAllLines(BaselineFile)
                   .Select(l => l.Trim())
                   .Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal))
                   .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");
}
