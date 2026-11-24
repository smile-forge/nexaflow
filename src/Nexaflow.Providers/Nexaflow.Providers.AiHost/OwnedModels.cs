using System.Runtime.CompilerServices;
using Nexaflow.AiHost.Catalog;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// The models Nexaflow runs itself: the AI host's catalog (<see cref="HostCatalog"/>) as Nexaflow sees it — each model as the kind
/// of provider that serves it, each entry as the catalog model a workspace shows and fetches, each shape and LoRA as a provider
/// offers it.
/// </summary>
internal static class OwnedModels
{
    public const string Cuda = HostCatalog.Cuda;
    public const string Cpu  = HostCatalog.Cpu;

    /// <summary>The shortest context a language model is given: room for the page it works on, its tools and a reply.</summary>
    public const int ShortestContext = HostCatalog.ShortestContext;

    private static readonly ConditionalWeakTable<HostedEntry, CatalogModel> Catalogs = new();
    private static readonly ConditionalWeakTable<HostedLora, ImageLora> Offers = new();

    public static IReadOnlyList<HostedModel> All => HostCatalog.Models;

    /// <summary>The tools' entries: the models each tool works with.</summary>
    public static IReadOnlyList<HostedTool> Tools => HostCatalog.Tools;

    /// <summary>The models of <paramref name="kind"/>.</summary>
    public static IEnumerable<HostedModel> OfKind(ModelKind kind) => All.Where(m => m.ModelKind == kind);

    /// <summary>The kind of provider that serves a model of <paramref name="kind"/>.</summary>
    public static ModelKind KindOf(HostedKind kind) => kind switch
    {
        HostedKind.Language        => ModelKind.Language,
        HostedKind.SpeechToText    => ModelKind.SpeechToText,
        HostedKind.TextToSpeech    => ModelKind.TextToSpeech,
        HostedKind.ImageGeneration => ModelKind.ImageGeneration,
        HostedKind.VideoGeneration => ModelKind.VideoGeneration,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no provider serves a model of this kind"),
    };

    public static HostedShape Shape(MediaShape shape) => shape switch
    {
        MediaShape.Square    => HostedShape.Square,
        MediaShape.Landscape => HostedShape.Landscape,
        MediaShape.Portrait  => HostedShape.Portrait,
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "no picture is made in this shape"),
    };

    public static MediaShape Shape(HostedShape shape) => shape switch
    {
        HostedShape.Square    => MediaShape.Square,
        HostedShape.Landscape => MediaShape.Landscape,
        HostedShape.Portrait  => MediaShape.Portrait,
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "no picture is made in this shape"),
    };

    private static ModelTier Tier(HostedTier tier) => tier switch
    {
        HostedTier.HighQuality => ModelTier.HighQuality,
        _ => ModelTier.Standard,
    };

    extension(HostedEntry entry)
    {
        /// <summary>The entry as a workspace shows and fetches it: every file any machine might need.</summary>
        public CatalogModel Catalog
            => Catalogs.GetValue(entry, e => new CatalogModel(e.Id, e.DisplayName, e.Files, e is HostedModel m ? Tier(m.Tier) : ModelTier.Standard));

        /// <summary>The entry as it stands on a machine running <paramref name="executionProvider"/>: only the files its parts there need.</summary>
        public CatalogModel CatalogFor(string executionProvider) => entry.Catalog with { Files = entry.FilesFor(executionProvider) };
    }

    extension(HostedModel model)
    {
        /// <summary>The kind of provider that serves it.</summary>
        public ModelKind ModelKind => KindOf(model.Kind);
    }

    extension(HostedLora lora)
    {
        /// <summary>What it is offered as.</summary>
        public ImageLora Offer
            => Offers.GetValue(lora, l => new ImageLora(l.Id, l.DisplayName, l.Description) { Adult = l.Adult, NegativePrompts = l.NegativePrompts });
    }
}
