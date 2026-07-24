using System.Threading;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Imaging.NativeCodec;
using Microsoft.Extensions.DependencyInjection;

namespace Nexaflow.Features.Dicom.Services;

/// <summary>
/// One-time fo-dicom initialisation: registers the native transcoder manager (fo-dicom.Codecs) so
/// compressed transfer syntaxes (JPEG-Lossless / JPEG-LS / JPEG 2000 — most real CDs) decode, and pins
/// the core <see cref="RawImageManager"/> so <see cref="DicomImage.RenderImage"/> yields a BGRA32 buffer
/// we turn into a <c>WriteableBitmap</c> ourselves (no System.Drawing dependency).
/// <para>
/// <see cref="DicomSetupBuilder"/> installs a process-wide service provider, so this must run exactly once.
/// Guarded with <see cref="Interlocked"/>; called from the tab registration ctor and the loader.
/// </para>
/// </summary>
internal static class DicomBootstrap
{
    private static int _initialized;

    public static void EnsureInitialized()
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0) return;

        new DicomSetupBuilder()
            .RegisterServices(s => s
                .AddFellowOakDicom()
                .AddImageManager<RawImageManager>()
                .AddTranscoderManager<NativeTranscoderManager>())
            .SkipValidation()
            .Build();
    }
}
