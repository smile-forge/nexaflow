using Nexaflow.Features.Common;
using Nexaflow.Tests.Features.FileActions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Web.FileActions;

// The one viewer-opening file action outside the Viewers suite. Same contract, same six lines — see
// Nexaflow.Tests.Features.Common/FileActions/FileActionConformance.cs for what it holds an action to.

[TestClass]
[CoversNode("web-browse-action")]
public class ShowHtmlActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Web.FileActions.ShowHtmlAction(shell);
    protected override string ExpectedPageKind => "Html";
    protected override string AcceptableFile   => @"C:\site\index.html";
}
