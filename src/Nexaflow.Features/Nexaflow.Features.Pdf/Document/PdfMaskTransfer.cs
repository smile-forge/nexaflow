using System;
using UglyToad.PdfPig.Functions;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// A soft mask's transfer function (its <c>/TR</c>, 11.6.5.2): what each level the mask's group gives — its luminosity or
/// its alpha — becomes before it masks, held as the 256 levels a mask is read in. A document inverts a gradient with
/// one, <c>{1 exch sub}</c>, so that the same fade can show one picture in and another out.
/// </summary>
internal sealed class PdfMaskTransfer
{
    private readonly byte[] _levels;

    private PdfMaskTransfer(byte[] levels) => _levels = levels;

    /// <summary>Level <paramref name="level"/> through the function.</summary>
    public byte this[byte level] => _levels[level];

    /// <summary>
    /// The transfer <paramref name="function"/> makes, or <see langword="null"/> where there is none and every level is
    /// as the group gives it. A level the function gives nothing for, or no number, stays as it was.
    /// </summary>
    public static PdfMaskTransfer? Of(PdfFunction? function)
    {
        if (function is null) return null;

        var levels = new byte[256];
        for (var i = 0; i < levels.Length; i++)
        {
            var input = i / 255.0;
            var output = function.Eval(input);
            var level = output.Length > 0 && !double.IsNaN(output[0]) ? Math.Clamp(output[0], 0, 1) : input;
            levels[i] = (byte)Math.Round(level * 255);
        }

        return new PdfMaskTransfer(levels);
    }
}
