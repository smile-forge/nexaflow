using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.Projects.Model;
using Nexaflow.Visuals.Common.Theming;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Features.Projects.Viewlets;

public partial class BacklogStatusViewletView : UserControl
{
    /// <summary>One status row in the breakdown.</summary>
    public sealed record StatusChip(string Label, Brush Brush, int Count);

    private readonly IShellServices _shell;
    private readonly string         _folderPath;

    public BacklogStatusViewletView(ProjectsConfig config, IShellServices shell, string folderPath, IViewletController controller)
    {
        InitializeComponent();
        _shell      = shell;
        _folderPath = folderPath;

        try
        {
            var info   = ProjectOperations.LoadProjectFile(folderPath);
            var counts = new ProjectOperations(config).BacklogCountsByStatus(info);
            var chips  = counts.Select(c => new StatusChip(c.Def.Label, SwatchPalette.Resolve(c.Def.SwatchKey), c.Count)).ToList();
            ChipList.ItemsSource = chips;
            EmptyText.Visibility = chips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { EmptyText.Visibility = Visibility.Visible; }
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
        => _shell.OpenTab("ProjectDetail", new() { ["path"] = _folderPath });
}
