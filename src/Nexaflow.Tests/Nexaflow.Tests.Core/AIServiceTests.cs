using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using System.Text.Json;

namespace Nexaflow.Tests.Core;

[TestClass]
public class AIServiceTests
{

    private string _tempDir;
    private MockLlmProvider _mockLlm;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), " NexaflowTests_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        
        _mockLlm = new MockLlmProvider();
        LlmProviderRegistry.Register("Mock", _mockLlm);
    }


    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }


    [TestMethod]
    public async Task SaveAndLoad_ShouldPersistConversation()
    {
        var service = new AIService(_tempDir);
        var record = new ConversationRecord { Id = "test-conv", StartedAt = DateTime.Now };
        await service.SaveAsync(record);
        var conversations = (await service.LoadAllAsync()).ToList();
        Assert.AreEqual(1, conversations.Count);
        Assert.AreEqual("test-conv", conversations[0].Id);
    }

    private class MockLlmProvider : ILlmProvider
    {
        public string Name => "Mock";
        public Func<string, string, Task<LlmResponse?>>? QueryFunc { get; set; }
        public Func<IReadOnlyList<LlmMessage>, string, Task<LlmResponse?>>? ChatFunc { get; set; }
        public Task<LlmResponse?> QueryAsync(string systemPrompt, string userPrompt, IReadOnlyList<string>? attachments = null, CancellationToken ct = default)
            => QueryFunc?.Invoke(systemPrompt, userPrompt) ?? Task.FromResult<LlmResponse?>(null);
        public Task<LlmResponse?> ChatAsync(IReadOnlyList<LlmMessage> history, string newUserPrompt, IReadOnlyList<string>? attachments = null, CancellationToken ct = default)
            => ChatFunc?.Invoke(history, newUserPrompt) ?? Task.FromResult<LlmResponse?>(null);
    }

    private class MockQueryHandler : IQueryHandler
    {
        public string Description { get; }
        public MockQueryHandler(string desc) => Description = desc;
        public float CanProcess(string input, IPageViewModel? pageVm = null) => 1.0f;
        public Task<string?> ProcessAsync(string input, IPageViewModel? pageVm = null) => Task.FromResult<string?>(null);
    }

    private class MockPageViewModel : IPageViewModel
    {
        public string Context { get; set; } = "Mock Context";
        public List<ActionDescriptor> Actions { get; set; } = new();
        public string GetContext() => Context;
        public IReadOnlyList<ActionDescriptor> GetAvailableActions() => Actions;
    }
}
