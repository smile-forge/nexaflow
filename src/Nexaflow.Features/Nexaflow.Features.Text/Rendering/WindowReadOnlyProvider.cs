using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using System;
using System.Collections.Generic;

namespace Nexaflow.Features.Text.Rendering;

/// <summary>
/// Makes everything outside the resident window read-only while editing a large file, so the user can't
/// type into (or delete from) the placeholder regions that stand in for unloaded content. The editable
/// span is read live via delegates because it moves as the window slides.
/// </summary>
internal sealed class WindowReadOnlyProvider(Func<int> editableStart, Func<int> editableEnd) : IReadOnlySectionProvider
{
    public bool CanInsert(int offset)
    {
        int s = editableStart(), e = editableEnd();
        return offset >= s && offset <= e;
    }

    public IEnumerable<ISegment> GetDeletableSegments(ISegment segment)
    {
        int s = editableStart(), e = editableEnd();
        int from = Math.Max(segment.Offset, s);
        int to   = Math.Min(segment.EndOffset, e);
        if (to > from)
            yield return new TextSegment { StartOffset = from, EndOffset = to };
    }
}
