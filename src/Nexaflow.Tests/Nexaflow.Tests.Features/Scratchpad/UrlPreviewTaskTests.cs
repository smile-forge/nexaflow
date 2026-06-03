using Nexaflow.Features.Scratchpad.Services;

namespace Nexaflow.Tests.Features.Scratchpad;

[TestClass]
public class UrlPreviewTaskTests
{
    [TestMethod]
    public void ExtractMetadata_ReadsOpenGraphTags()
    {
        const string html = """
            <html><head>
              <meta property="og:title" content="Hello World">
              <meta property="og:description" content="A short description">
              <meta property="og:image" content="https://x.com/img.png">
            </head></html>
            """;

        var (title, description, image) = UrlPreviewTask.ExtractMetadata(html);

        Assert.AreEqual("Hello World", title);
        Assert.AreEqual("A short description", description);
        Assert.AreEqual("https://x.com/img.png", image);
    }

    [TestMethod]
    public void ExtractMetadata_FallsBackToTitleTagAndDescriptionMeta()
    {
        const string html = """
            <html><head>
              <title>Page Title</title>
              <meta name="description" content="meta description">
            </head></html>
            """;

        var (title, description, image) = UrlPreviewTask.ExtractMetadata(html);

        Assert.AreEqual("Page Title", title);
        Assert.AreEqual("meta description", description);
        Assert.IsNull(image);
    }

    [TestMethod]
    public void ExtractMetadata_HandlesReversedAttributeOrderAndDecodesEntities()
    {
        // content before property, and an HTML entity in the value.
        const string html = """<meta content="Tom &amp; Jerry" property="og:title">""";

        var (title, _, _) = UrlPreviewTask.ExtractMetadata(html);

        Assert.AreEqual("Tom & Jerry", title);
    }

    [TestMethod]
    public void ExtractMetadata_NoMetadata_ReturnsNulls()
    {
        var (title, description, image) = UrlPreviewTask.ExtractMetadata("<html><body>nothing</body></html>");

        Assert.IsNull(title);
        Assert.IsNull(description);
        Assert.IsNull(image);
    }
}
