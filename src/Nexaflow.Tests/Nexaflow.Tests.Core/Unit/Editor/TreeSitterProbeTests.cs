using System.Linq;
using TreeSitter;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.Editor;

/// <summary>
/// Probe: confirms the C# grammar native loads and pins down whether <c>Node.StartIndex/EndIndex</c> are
/// UTF-16 char offsets (what AvalonEdit needs), UTF-8 byte offsets, or UTF-16 byte offsets. The comment
/// "// café" is 7 UTF-16 units but 8 UTF-8 bytes (é = 2 bytes), so the end index disambiguates.
/// </summary>
[TestClass]
[CoversNode("syntax")]
public class TreeSitterProbeTests
{
    [TestMethod]
    public void CSharp_Loads_AndIndexConvention()
    {
        using var lang = new Language("c-sharp");
        using var parser = new Parser(lang);
        using var tree = parser.Parse("// café\nclass C {}")!;
        Assert.IsNotNull(tree);

        using var query = new Query(lang, "(comment) @comment");
        var comment = query.Execute(tree.RootNode).Captures.First(c => c.Name == "comment");

        Assert.AreEqual(0, comment.Node.StartIndex, "comment start index");
        Assert.AreEqual(7, comment.Node.EndIndex,
            $"comment end index — Text='{comment.Node.Text}', Text.Length={comment.Node.Text.Length} " +
            "(7 ⇒ UTF-16 chars, 8 ⇒ UTF-8 bytes, 14 ⇒ UTF-16 bytes)");
    }
}
