using Nexaflow.Features.Common;

namespace Nexaflow.Features.Json.ViewModels;

public sealed class JsonPathQueryHandler : IQueryHandler
{
    public string  Description => "Evaluates a JSONPath expression (starting with $) against the open JSON file.";
    public string? Symbol      => "$";

    public float CanProcess(string input, IPageViewModel? pageVm = null)
    {
        if (pageVm is not JsonViewModel) return 0f;
        return input.TrimStart().StartsWith('$') ? 0.95f : 0f;
    }

    public Task<string?> ProcessAsync(string input, IPageViewModel? pageVm = null)
    {
        if (pageVm is JsonViewModel vm)
            vm.EvaluateJsonPath(input.Trim());
        return Task.FromResult<string?>(null);
    }
}
