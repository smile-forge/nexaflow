using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.Features.Images.Services;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.OpenRaster;
using Nexaflow.Visuals.Common.Drafts;
using Nexaflow.Visuals.Common.Localization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>What an Artist tab edits from its start screen on, for its page to follow: the picture, and — a draft — where the
/// draft's Save as starts.</summary>
public sealed record ArtistPicture(string Path, bool IsDraft, string? DraftFolder);
