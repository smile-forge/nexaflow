using Nexaflow.Features.Images.ViewModels;
using System.Windows.Controls;

namespace Nexaflow.Features.Images.Views;

/// <summary>What the picture on show says about its edits — changed underneath them, kept only for the session, a picture
/// tool at work or waiting — as the viewer and Artist both show it. Its DataContext is the page's <see cref="PictureStage"/>.</summary>
public partial class PictureNotices : UserControl
{
    public PictureNotices() => InitializeComponent();
}
