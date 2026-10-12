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

/// <summary>
/// What a presented frame is a picture of: the recipe and frame it was rendered for, and the matrices from the
/// straightened frame and from the source to its layout (device-independent) coordinates — what anything drawn over
/// the picture, the crop handles and markup selection, is placed with.
/// </summary>
public sealed record EditCanvas(ImageRecipe Recipe, RenderFrame Frame, SKSize Source, double LayoutScale, SKSizeI Size,
                                SKMatrix StraightenedToCanvas, SKMatrix SourceToCanvas);
