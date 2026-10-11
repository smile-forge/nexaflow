using System.Text;

namespace Nexaflow.Core.Services.Agent;

/// <summary>
/// The prose of one model step while it streams in — what <see cref="ClientBlockParser"/> will return as the
/// explanation once the step is complete, as far as it can be known yet.
/// <para>
/// Text shows as it arrives. A fenced block shows only once it is closed: a half-written diagram or table has
/// nothing true to draw, and a fence the agent acts on (<c>client_tool</c>, …) never shows at all. A line still
/// being written is held back while it could yet open a fence.
/// </para>
/// </summary>
internal sealed class ReplyDraft
{
    private readonly StringBuilder _prose = new();
    private readonly StringBuilder _line  = new();
    private readonly StringBuilder _block = new();

    /// <summary>The fence the lines are inside, and whether it is one the agent acts on; null outside one.</summary>
    private (char Char, int Length, bool IsAction)? _fence;

    public void Append(string delta)
    {
        foreach (var c in delta)
        {
            if (c == '\n') EndLine();
            else if (c != '\r') _line.Append(c);
        }
    }

    /// <summary>The prose so far, trimmed as the parser trims it.</summary>
    public string Text
    {
        get
        {
            if (_fence is not null || _line.Length == 0 || MayOpenFence(_line))
                return _prose.ToString().Trim();
            return string.Concat(_prose.ToString(), _line.ToString()).Trim();
        }
    }

    private void EndLine()
    {
        var line = _line.ToString();
        _line.Clear();

        if (_fence is { } fence)
        {
            if (!fence.IsAction) _block.Append(line).Append('\n');
            if (!ClientBlockParser.IsFenceClose(line, fence.Char, fence.Length)) return;

            _fence = null;
            _prose.Append(_block);
            _block.Clear();
            return;
        }

        if (ClientBlockParser.TryParseFenceOpen(line, out var ch, out var length, out var info))
        {
            var isAction = ClientBlockParser.IsActionBlock(info);
            _fence = (ch, length, isAction);
            if (!isAction) _block.Append(line).Append('\n');
            return;
        }

        _prose.Append(line).Append('\n');
    }

    /// <summary>Whether a partial line could still turn out to open a fence: it is blank so far, or its first
    /// character is a backtick or a tilde. Such a line shows once it ends.</summary>
    private static bool MayOpenFence(StringBuilder line)
    {
        var i = 0;
        while (i < line.Length && line[i] == ' ') i++;
        if (i == line.Length) return true;

        var c = line[i];
        return c is '`' or '~';
    }
}
