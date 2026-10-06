using System.Collections.Generic;
using Nexaflow.Visuals.Common.Controls;

namespace Nexaflow.Features.Tabular.ViewModels;

public sealed class TabularRowViewModel : VirtualizedRow
{
    public TabularRowViewModel(int absoluteIndex, IReadOnlyList<string> cells) : base(absoluteIndex, cells) { }
}
