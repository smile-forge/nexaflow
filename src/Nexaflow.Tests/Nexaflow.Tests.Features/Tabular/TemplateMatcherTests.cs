using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Tabular.Detection;
using Nexaflow.Features.Tabular.Templates;

namespace Nexaflow.Tests.Features.Tabular;

[TestClass]
public class TemplateMatcherTests
{
    private static CsvShape Shape(char? sep, bool header, int fields) => new()
    {
        Separator   = sep,
        Quote       = null,
        HasHeader   = header,
        ColumnTypes = Enumerable.Repeat(CsvDataType.String, fields).ToList(),
        FieldCount  = fields,
        Score       = 1f,
        Label       = "",
        Description = "",
    };

    private static TabularTemplate Tmpl(
        string? sep, bool header, int fields, string[]? headers,
        TemplateScope scope = TemplateScope.Manual, string folder = "", string glob = "") => new()
    {
        Id              = "id",
        Name            = "n",
        Scope           = scope,
        FolderPath      = folder,
        Glob            = glob,
        Separator       = sep,
        HasHeader       = header,
        FieldCount      = fields,
        OriginalHeaders = headers,
        TransformsJson  = "[]",
    };

    // ── IsCompatible ─────────────────────────────────────────────────────────

    [TestMethod]
    public void IsCompatible_SameSeparatorCountAndHeaders_True()
    {
        var t = Tmpl(",", true, 3, ["a", "b", "c"]);
        Assert.IsTrue(TemplateMatcher.IsCompatible(t, Shape(',', true, 3), ["a", "b", "c"]));
    }

    [TestMethod]
    public void IsCompatible_HeaderNamesDiffer_False()
    {
        var t = Tmpl(",", true, 3, ["a", "b", "c"]);
        Assert.IsFalse(TemplateMatcher.IsCompatible(t, Shape(',', true, 3), ["a", "b", "x"]));
    }

    [TestMethod]
    public void IsCompatible_HeaderNames_TrimmedCaseInsensitive_True()
    {
        var t = Tmpl(",", true, 3, ["A", "B", "C"]);
        Assert.IsTrue(TemplateMatcher.IsCompatible(t, Shape(',', true, 3), [" a ", "b", "c"]));
    }

    [TestMethod]
    public void IsCompatible_DifferentColumnCount_False()
    {
        var t = Tmpl(",", true, 3, ["a", "b", "c"]);
        Assert.IsFalse(TemplateMatcher.IsCompatible(t, Shape(',', true, 4), ["a", "b", "c", "d"]));
    }

    [TestMethod]
    public void IsCompatible_DifferentSeparator_False()
    {
        var t = Tmpl(",", true, 3, ["a", "b", "c"]);
        Assert.IsFalse(TemplateMatcher.IsCompatible(t, Shape('\t', true, 3), ["a", "b", "c"]));
    }

    [TestMethod]
    public void IsCompatible_HeaderlessSameSepAndCount_True()
    {
        var t = Tmpl(",", false, 3, null);
        Assert.IsTrue(TemplateMatcher.IsCompatible(t, Shape(',', false, 3), null));
    }

    [TestMethod]
    public void IsCompatible_HeaderVsHeaderless_False()
    {
        var t = Tmpl(",", true, 3, ["a", "b", "c"]);
        Assert.IsFalse(TemplateMatcher.IsCompatible(t, Shape(',', false, 3), null));
    }

    // ── ScopeMatches ─────────────────────────────────────────────────────────

    [TestMethod]
    public void ScopeMatches_Folder_ExactDirectory_True()
    {
        var t = Tmpl(",", true, 3, ["a"], TemplateScope.Folder, folder: @"C:\data");
        Assert.IsTrue(TemplateMatcher.ScopeMatches(t, @"C:\data\file.csv"));
    }

    [TestMethod]
    public void ScopeMatches_Folder_SiblingDirectory_False()
    {
        var t = Tmpl(",", true, 3, ["a"], TemplateScope.Folder, folder: @"C:\data");
        Assert.IsFalse(TemplateMatcher.ScopeMatches(t, @"C:\other\file.csv"));
    }

    [TestMethod]
    public void ScopeMatches_Folder_Subdirectory_False()
    {
        var t = Tmpl(",", true, 3, ["a"], TemplateScope.Folder, folder: @"C:\data");
        Assert.IsFalse(TemplateMatcher.ScopeMatches(t, @"C:\data\sub\file.csv"));
    }

    [TestMethod]
    public void ScopeMatches_Glob_ExtensionPattern()
    {
        var t = Tmpl(",", true, 3, ["a"], TemplateScope.Glob, glob: "*.csv");
        Assert.IsTrue(TemplateMatcher.ScopeMatches(t,  @"C:\any\file.csv"));
        Assert.IsFalse(TemplateMatcher.ScopeMatches(t, @"C:\any\file.tsv"));
    }

    [TestMethod]
    public void ScopeMatches_Glob_PrefixPattern()
    {
        var t = Tmpl(",", true, 3, ["a"], TemplateScope.Glob, glob: "sales-*.csv");
        Assert.IsTrue(TemplateMatcher.ScopeMatches(t,  @"C:\any\sales-q1.csv"));
        Assert.IsFalse(TemplateMatcher.ScopeMatches(t, @"C:\any\orders.csv"));
    }

    [TestMethod]
    public void ScopeMatches_Manual_NeverMatches()
    {
        var t = Tmpl(",", true, 3, ["a"], TemplateScope.Manual, folder: @"C:\data");
        Assert.IsFalse(TemplateMatcher.ScopeMatches(t, @"C:\data\file.csv"));
    }

    // ── FindAutoApply ────────────────────────────────────────────────────────

    [TestMethod]
    public void FindAutoApply_ReturnsCompatibleFolderTemplate_ExcludesManualAndIncompatible()
    {
        var path  = @"C:\data\file.csv";
        var raw   = Shape(',', true, 3);
        string[] headers = ["a", "b", "c"];

        var compatibleFolder = Tmpl(",", true, 3, ["a", "b", "c"], TemplateScope.Folder, folder: @"C:\data");
        var manual           = Tmpl(",", true, 3, ["a", "b", "c"], TemplateScope.Manual, folder: @"C:\data");
        var wrongShape       = Tmpl(",", true, 4, ["a", "b", "c", "d"], TemplateScope.Folder, folder: @"C:\data");

        var result = TemplateMatcher.FindAutoApply(
            new[] { compatibleFolder, manual, wrongShape }, path, raw, headers);

        CollectionAssert.AreEqual(new[] { compatibleFolder }, result.ToList());
    }

    [TestMethod]
    public void FindAutoApply_MultipleMatches_ReturnsAll()
    {
        var path  = @"C:\data\file.csv";
        var raw   = Shape(',', true, 3);
        string[] headers = ["a", "b", "c"];

        var byFolder = Tmpl(",", true, 3, ["a", "b", "c"], TemplateScope.Folder, folder: @"C:\data");
        var byGlob   = Tmpl(",", true, 3, ["a", "b", "c"], TemplateScope.Glob, glob: "*.csv");

        var result = TemplateMatcher.FindAutoApply(new[] { byFolder, byGlob }, path, raw, headers);

        Assert.AreEqual(2, result.Count);
    }
}
