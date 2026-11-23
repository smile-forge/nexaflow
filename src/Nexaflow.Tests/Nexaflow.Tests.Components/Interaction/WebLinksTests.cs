using System.Text;
using Nexaflow.Interaction;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Interaction;

/// <summary>An address out of what applications drag as one.</summary>
[TestClass]
[CoversNode("interaction-web-links")]
public class WebLinksTests
{
    [TestMethod]
    public void AnAddress_IsReadFromEitherEncoding_AndFromAFirefoxPair()
    {
        Assert.AreEqual("https://a.example/x", WebLinks.Decode(Encoding.Unicode.GetBytes("https://a.example/x\0junk")));
        Assert.AreEqual("http://a.example/x", WebLinks.Decode(Encoding.ASCII.GetBytes("http://a.example/x\0")));
        Assert.AreEqual("https://a.example/x", WebLinks.Decode("https://a.example/x\nIts title"));
    }

    [TestMethod]
    [DataRow("file:///C:/secret.txt")]
    [DataRow("javascript:alert(1)")]
    [DataRow("ms-settings:privacy")]
    [DataRow("see https://a.example")]
    [DataRow("")]
    public void OnlyOneWholeWebAddress_IsALink(string text)
        => Assert.IsNull(WebLinks.SingleAddress(text));
}
