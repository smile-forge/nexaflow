
using Microsoft.ML.OnnxRuntimeGenAI;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;

namespace Nexaflow.AiHost.Models;

/// <summary>
/// One model in memory: its onnxruntime-genai objects, the chat template it renders conversations with, and the
/// conversations it is keeping open.
/// </summary>
internal sealed class LoadedModel : IDisposable
{
    private readonly Config _config;

    private LoadedModel(ModelSpec spec, Config config, Model model, string template)
    {
        Spec          = spec;
        _config       = config;
        Model         = model;
        Tokenizer     = new Tokenizer(model);
        Processor     = new MultiModalProcessor(model);
        Template      = template;
        EndTokens     = new HashSet<int>(Tokenizer.GetEosTokenIds().ToArray());

        Conversations = new ConversationLog(this);
    }

    public ModelSpec Spec { get; }
    public Model Model { get; }
    public Tokenizer Tokenizer { get; }
    public MultiModalProcessor Processor { get; }

    /// <summary>The model's own chat template — a new model needs no harness of its own.</summary>
    public string Template { get; }

    /// <summary>The tokens that end the model's turn.</summary>
    public IReadOnlySet<int> EndTokens { get; }

    public ConversationLog Conversations { get; }

    /// <summary>Prepares the folder if it needs it, writes its config for <paramref name="executionProvider"/>, and loads it.</summary>
    public static LoadedModel Load(ModelSpec spec, string executionProvider, ModelAdapters adapters)
    {
        adapters.EnsurePrepared(spec.Folder, spec.Adapter);
        GenAiConfigFile.WriteFor(spec.Folder, executionProvider);

        var config = new Config(spec.Folder);
        try
        {
            var model = new Model(config);
            return new LoadedModel(spec, config, model, File.ReadAllText(Path.Combine(spec.Folder, "chat_template.jinja")));
        }
        catch
        {
            config.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Conversations.Dispose();
        Processor.Dispose();
        Tokenizer.Dispose();
        Model.Dispose();
        _config.Dispose();
    }
}
