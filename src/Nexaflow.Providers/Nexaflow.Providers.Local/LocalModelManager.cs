using System.Diagnostics;
using System.IO;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.Local;

/// <summary>
/// Owns one loaded GGUF model (its <see cref="LLamaWeights"/>) for an execution provider instance.
/// Mirrors the reference engine's ModelManager: weights load once and stay resident; a fresh
/// <see cref="LLamaContext"/> is created per inference; semaphores serialize load and inference.
/// Model lifetime equals this object's lifetime (warm-up loads, cool-down/dispose unloads).
/// </summary>
internal sealed class LocalModelManager(string modelPath, uint contextSize, int gpuLayers) : IDisposable
{
    private readonly SemaphoreSlim _loadLock  = new(1, 1);
    private readonly SemaphoreSlim _inferGate = new(1, 1);

    private LLamaWeights? _weights;
    private ModelParams?  _params;

    public async Task LoadAsync(CancellationToken ct)
    {
        if (_weights is not null) return;
        await _loadLock.WaitAsync(ct);
        try
        {
            if (_weights is not null) return;
            if (!File.Exists(modelPath))
                throw new LlmProviderException(
                    $"Model file not found: '{modelPath}'. The download may have failed — check the repo/filename in catalog.json.");

            LocalNativeRuntime.EnsureConfigured();
            _params = new ModelParams(modelPath) { ContextSize = contextSize, GpuLayerCount = gpuLayers };
            Debug.WriteLine($"[Local] loading '{Path.GetFileName(modelPath)}' contextSize={contextSize} gpuLayers={gpuLayers}");
            try
            {
                _weights = await Task.Run(() => LLamaWeights.LoadFromFile(_params), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new LlmProviderException(
                    $"Failed to load model '{modelPath}'. It may be incomplete, corrupt, or an unsupported GGUF — " +
                    "delete the file to force a re-download, or fix the repo/filename in catalog.json. " +
                    $"({ex.Message})", ex);
            }
        }
        finally { _loadLock.Release(); }
    }

    public async Task<string> InferAsync(
        string prompt, IReadOnlyList<string> antiPrompts, int maxTokens, CancellationToken ct)
    {
        await LoadAsync(ct);
        await _inferGate.WaitAsync(ct);
        try
        {
            if (_weights is null || _params is null)
                throw new InvalidOperationException("Model weights are not loaded.");

            using var context = _weights.CreateContext(_params);
            var executor = new InteractiveExecutor(context);
            var inferParams = new InferenceParams
            {
                MaxTokens        = maxTokens,
                AntiPrompts      = [.. antiPrompts],
                SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.7f, TopP = 0.95f, TopK = 64 },
            };

            var sb = new StringBuilder();
            await foreach (var chunk in executor.InferAsync(prompt, inferParams, ct).WithCancellation(ct))
                sb.Append(chunk);
            return sb.ToString();
        }
        finally { _inferGate.Release(); }
    }

    /// <summary>Unloads the weights, waiting for any in-flight inference to finish. Idempotent.</summary>
    public async Task UnloadAsync()
    {
        await _loadLock.WaitAsync();
        try
        {
            await _inferGate.WaitAsync();
            try { _weights?.Dispose(); _weights = null; }
            finally { _inferGate.Release(); }
        }
        finally { _loadLock.Release(); }
    }

    public void Dispose()
    {
        _weights?.Dispose();
        _weights = null;
        _loadLock.Dispose();
        _inferGate.Dispose();
    }
}
