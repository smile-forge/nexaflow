using System.Windows;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;

namespace Nexaflow.Features.ProductManager.Viewlets;

/// <summary>
/// Surfaces a product summary in the file browser for any folder containing a <c>.product/</c> directory
/// — exactly the mechanism the git viewlet uses for <c>.git</c>. Opening it launches the Product Manager
/// tab against that folder. FeatureManager's file-system registry injects <see cref="IShellServices"/>.
/// </summary>
public sealed class ProductViewlet : IFolderViewlet
{
    private readonly IShellServices _shell;

    public ProductViewlet(IShellServices shell) => _shell = shell;

    public string DisplayName    => "Product";
    public bool   AppliesToDrives => false;

    public string[]? ContainsFolderGlobs => [".product"];

    public ViewletDisplayMode   DefaultDisplayMode => ViewletDisplayMode.SingleBar;
    public ViewletDisplayMode[] SupportedModes     => [ViewletDisplayMode.SingleBar, ViewletDisplayMode.DoubleBar];

    public FrameworkElement CreateView(string folderPath, IViewletController controller)
        => new Views.ProductViewletView(_shell, folderPath);
}
