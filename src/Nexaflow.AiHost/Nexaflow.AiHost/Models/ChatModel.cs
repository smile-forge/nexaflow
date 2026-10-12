
using Microsoft.ML.OnnxRuntimeGenAI;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;

namespace Nexaflow.AiHost.Models;

/// <summary>
/// A language model in memory: its onnxruntime-genai objects, the chat template it renders conversations with, and
/// the conversations it is keeping open.
/// </summary>
internal sealed class ChatModel : IHostModel
{
    private readonly Config _config;

    private ChatModel(ModelSpec spec, Config config, Model model, string template)
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

    public static ChatModel Load(ModelSpec spec, string executionProvider)
    {
        string template = File.ReadAllText(Path.Combine(spec.Folder, "chat_template.jinja"));
        var (config, model) = GenAiConfigFile.Open(spec, executionProvider);
        return new ChatModel(spec, config, model, template);
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

/// <summary>Language models onnxruntime-genai runs, chatted with through their own templates.</summary>
internal sealed class ChatModelRunner : IModelRunner
{
    public string Id => "genai-chat";

    public IHostModel Load(ModelSpec spec, string executionProvider) => ChatModel.Load(spec, executionProvider);
}
