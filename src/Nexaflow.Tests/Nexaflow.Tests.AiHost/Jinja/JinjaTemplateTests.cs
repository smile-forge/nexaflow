using System.Text.Json.Nodes;
using Nexaflow.AiHost.Jinja;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost.Jinja;

/// <summary>
/// The host's Jinja engine against transformers' — the Python environment chat templates are written for. Each shipped
/// model family's template renders a set of conversations exactly as Python rendered them, and short templates check
/// one feature each. The references come from <c>tools/ai-models/chat-templates/render_references.py</c>.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class JinjaTemplateTests
{
    private static JsonObject Resource(string name)
    {
        using var stream = typeof(JinjaTemplateTests).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"{name} is not embedded.");
        return JsonNode.Parse(stream)!.AsObject();
    }

    private static readonly Lazy<JsonObject> Families = new(() => Resource("chat-templates.json"));
    private static readonly Lazy<JsonObject> Snippets = new(() => Resource("jinja-snippets.json"));

    public static IEnumerable<object[]> FamilyNames => Families.Value.Select(f => new object[] { f.Key });

    [TestMethod]
    [DynamicData(nameof(FamilyNames))]
    public void AFamilysOwnTemplate_RendersEveryConversation_AsTransformersDoes(string family)
    {
        var data = Families.Value[family]!;
        var template = JinjaTemplate.Parse((string)data["template"]!);
        var mismatches = new List<string>();
        foreach (var render in data["renders"]!.AsArray())
        {
            var variables = new Dictionary<string, object?>
            {
                ["messages"]              = render!["messages"]!.DeepClone(),
                ["add_generation_prompt"] = (bool)render["add_generation_prompt"]!,
                ["bos_token"]             = (string?)render["bos_token"],
                ["eos_token"]             = (string?)render["eos_token"],
            };
            if (render["tools"] is JsonArray tools) variables["tools"] = tools.DeepClone();
            foreach (var (name, value) in render["variables"]!.AsObject()) variables[name] = value?.DeepClone();

            string expected = (string)render["rendered"]!, actual = template.Render(variables);
            if (actual != expected) mismatches.Add($"{render["case"]} {render["variables"]!.ToJsonString()}: {Difference(expected, actual)}");
        }
        Assert.AreEqual(0, mismatches.Count, string.Join("\n", mismatches));
    }

    [TestMethod]
    public void EverySnippet_RendersAsPythonsJinjaDoes()
    {
        var data = Snippets.Value["data"]!.AsObject().ToDictionary(p => p.Key, p => (object?)p.Value?.DeepClone());
        var mismatches = new List<string>();
        foreach (var snippet in Snippets.Value["snippets"]!.AsArray())
        {
            string source = (string)snippet!["source"]!, expected = (string)snippet["output"]!;
            try
            {
                string actual = JinjaTemplate.Parse(source).Render(data);
                if (actual != expected) mismatches.Add($"{source}\n  {Difference(expected, actual)}");
            }
            catch (JinjaException ex)
            {
                mismatches.Add($"{source}\n  threw {ex.Message}");
            }
        }
        Assert.AreEqual(0, mismatches.Count, string.Join("\n", mismatches));
    }

    [TestMethod]
    public void WhatPythonRefuses_IsRefused_AndWhatItAllows_Renders()
    {
        var data = Snippets.Value["data"]!.AsObject().ToDictionary(p => p.Key, p => (object?)p.Value?.DeepClone());
        foreach (var snippet in Snippets.Value["refused"]!.AsArray())
        {
            string source = (string)snippet!["source"]!;
            bool raised;
            try
            {
                JinjaTemplate.Parse(source).Render(data);
                raised = false;
            }
            catch (JinjaException)
            {
                raised = true;
            }
            Assert.AreEqual((bool)snippet["raised"]!, raised, source);
        }
    }

    [TestMethod]
    public void ATemplatesOwnException_CarriesItsMessage()
    {
        var ex = Assert.ThrowsExactly<JinjaException>(() =>
            JinjaTemplate.Parse("{% if not messages %}{{ raise_exception('No messages provided.') }}{% endif %}")
                         .Render(new Dictionary<string, object?> { ["messages"] = new List<object?>() }));

        Assert.AreEqual("No messages provided.", ex.Message);
    }

    [TestMethod]
    public void AMacroThatNeverStops_IsRefused_RatherThanOverflowingTheStack()
    {
        var template = JinjaTemplate.Parse("{% macro forever(n) %}{{ forever(n + 1) }}{% endmacro %}{{ forever(0) }}");

        var ex = Assert.ThrowsExactly<JinjaException>(() => template.Render(new Dictionary<string, object?>()));
        StringAssert.Contains(ex.Message, "too deeply");
    }

    [TestMethod]
    public void AnUnclosedBlock_IsRefusedWithItsLine()
    {
        var ex = Assert.ThrowsExactly<JinjaException>(() => JinjaTemplate.Parse("one\ntwo\n{% for x in y %}\nnever closed"));

        StringAssert.Contains(ex.Message, "endfor");
        Assert.IsTrue(ex.Line > 0);
    }

    /// <summary>The names the <c>filter</c> and <c>test</c> tests answer from are the ones the engine runs — none
    /// listed that would fall through to "not one chat templates can use".</summary>
    [TestMethod]
    public void EveryFilterAndTestNamed_IsOneTheEngineRuns()
    {
        var none = new Dictionary<string, object?>();
        foreach (string name in JinjaBuiltins.FilterNames)
            Assert.IsFalse(Unknown(() => JinjaBuiltins.Filter(name, "x", [], none, new JinjaRenderer(), 1)), $"filter {name}");
        foreach (string name in JinjaBuiltins.TestNames)
            Assert.IsFalse(Unknown(() => JinjaBuiltins.Test(name, 1L, [1L], none, new JinjaRenderer(), 1)), $"test {name}");
    }

    private static bool Unknown(Action run)
    {
        try { run(); return false; }
        catch (JinjaException ex) { return ex.Message.Contains("is not one chat templates can use", StringComparison.Ordinal); }
    }

    private static string Difference(string expected, string actual)
    {
        int at = 0;
        while (at < Math.Min(expected.Length, actual.Length) && expected[at] == actual[at]) at++;
        string Around(string s) => s[Math.Max(0, at - 40)..Math.Min(s.Length, at + 40)].Replace("\n", "\\n");
        return $"differs at {at}: expected …{Around(expected)}… got …{Around(actual)}…";
    }
}
