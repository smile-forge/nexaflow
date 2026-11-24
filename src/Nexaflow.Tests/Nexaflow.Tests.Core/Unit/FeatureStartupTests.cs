using System.IO;
using System.Windows.Threading;
using Nexaflow.Core;
using Nexaflow.Core.Services;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// A feature sets itself up for the session when its assembly activates (<c>IFeatureStartup</c>): Core activates it and the
/// feature does the rest, so Core never reaches into a feature to set it up.
/// </summary>
[TestClass]
[CoversNode("feature-startup")]
public class FeatureStartupTests
{
    [TestMethod]
    public void AFeaturesOwnSetup_RunsWhenItsAssemblyActivates()
    {
        FeatureManager.Instance.RegisterFeatures(new WpfUiThread(Dispatcher.CurrentDispatcher), new Nexaflow.IO.Common.Locations.LocationProviderRegistry());

        FeatureCatalog.Instance.ActivateAssemblyOwning(typeof(WindowsFileSystemStartup).FullName!);

        Assert.IsTrue(Path.IsPathRooted(TemplatedCreateRegistry.Instance.TemplatesDir),
            "the file system set its template store up under the app's data folder as its assembly activated");
    }
}
