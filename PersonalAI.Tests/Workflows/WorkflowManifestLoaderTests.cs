using PersonalAI.Infrastructure.Workflows;
using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Workflows;

namespace PersonalAI.Tests.Workflows;

public sealed class WorkflowManifestLoaderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PersonalAI.Tests", Guid.NewGuid().ToString());

    [Fact]
    public async Task ValidManifest_Loads()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "summarize.manifest.json"),
            """
            {
              "id": "summarize",
              "name": "Summarize",
              "description": "Summarize a local document.",
              "version": "1.0.0",
              "author": "PersonalAI",
              "requiredCapabilities": ["TaskRuntime"],
              "requiredTools": ["workspace.file.read_text"],
              "riskLevel": "Low",
              "inputSchema": { "type": "object" },
              "outputSchema": { "type": "object" }
            }
            """);

        var result = await new FileSystemWorkflowManifestLoader(_directory)
            .DiscoverAsync();

        var manifest = Assert.Single(result.Manifests);
        Assert.Equal("summarize", manifest.Id);
        Assert.Empty(result.SafeErrors);
    }

    [Fact]
    public async Task MalformedDuplicateAndExecutableManifests_AreRejectedSafely()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "a.manifest.json"),
            ValidManifest("same"));
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "b.manifest.json"),
            ValidManifest("same"));
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "c.manifest.json"),
            "{ malformed");
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "d.manifest.json"),
            """
            {
              "id": "exec",
              "name": "Exec",
              "description": "Nope",
              "version": "1.0.0",
              "riskLevel": "Low",
              "requiredCapabilities": [],
              "requiredTools": [],
              "command": "powershell"
            }
            """);

        var result = await new FileSystemWorkflowManifestLoader(_directory)
            .DiscoverAsync();

        Assert.Single(result.Manifests);
        Assert.Contains("duplicate_manifest_id", result.SafeErrors);
        Assert.Contains("manifest_invalid", result.SafeErrors);
        Assert.Contains("manifest_contains_executable_fields", result.SafeErrors);
    }

    [Fact]
    public async Task CancellationDuringDiscovery_Propagates()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "a.manifest.json"),
            ValidManifest("a"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await new FileSystemWorkflowManifestLoader(_directory)
                .DiscoverAsync(cancellation.Token));
    }

    [Fact]
    public async Task QueryService_ValidatesUnsupportedCapabilitiesSafely()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "a.manifest.json"),
            """
            {
              "id": "needs-future",
              "name": "Needs Future",
              "description": "Description",
              "version": "1.0.0",
              "riskLevel": "Low",
              "requiredCapabilities": ["TaskRuntime", "DoesNotExist"],
              "requiredTools": []
            }
            """);
        var registry = BackendCapabilityRegistry.CreateDefault(
            hasTaskRuntime: true,
            hasDurableTaskHistory: true,
            hasWorkflowManifestLoader: true,
            hasSpeechToTextProvider: false,
            hasTextToSpeechProvider: false,
            hasLocalWorkerSupervisor: false,
            hasStructuredToolRuntime: true);
        var service = new WorkflowManifestQueryService(
            new FileSystemWorkflowManifestLoader(_directory),
            registry);

        var manifest = await service.GetAsync("needs-future");
        var statuses = await service.ValidateRequiredCapabilitiesAsync(manifest!);

        Assert.NotNull(manifest);
        Assert.Contains(statuses, status =>
            status.Capability == BackendCapability.TaskRuntime &&
            status.IsAvailable);
        Assert.Contains(statuses, status =>
            !status.IsAvailable &&
            status.SafeReasonCode == "unsupported_required_capability");
    }

    private static string ValidManifest(string id) =>
        $$"""
        {
          "id": "{{id}}",
          "name": "Name",
          "description": "Description",
          "version": "1.0.0",
          "riskLevel": "Low",
          "requiredCapabilities": ["TaskRuntime"],
          "requiredTools": []
        }
        """;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
