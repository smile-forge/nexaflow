using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Wpf;
using Nexaflow.Features.Images.Services;
using Nexaflow.Features.Images.Views;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>The viewer's sub-views, selected by the toolbar's segmented toggle (and its Edit button).</summary>
public enum ImageViewMode
{
    Carousel,   // one image at a time
    Album,      // wrapping thumbnail grid
    Explore,    // thumbnail strip on the left, selected image on the right
    Collage,    // scattered, tilted thumbnails on a pan/zoom canvas
    Edit,       // the current image on an editing canvas, with the tools beside it
}
