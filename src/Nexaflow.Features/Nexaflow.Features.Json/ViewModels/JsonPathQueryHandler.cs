using Nexaflow.Features.Common;

namespace Nexaflow.Features.Json.ViewModels;

public sealed class JsonPathQueryHandler : IQueryHandler
{
    public string  Description => "Evaluates a JSONPath expression (starting with $) against the open JSON file.";
    public string? Symbol      => "$";

    public float CanProcess(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm is not JsonViewModel) return 0f;
        return prefixed || input.TrimStart().StartsWith('$') ? 0.95f : 0f;
    }

    public Task<string?> ProcessAsync(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        // "$" is this handler's Symbol AND the JSONPath root, so the router strips it when prefixed — put it back.
        if (pageVm is JsonViewModel vm)
            vm.EvaluateJsonPath(prefixed ? "$" + input.Trim() : input.Trim());
        return Task.FromResult<string?>(null);
    }
}
