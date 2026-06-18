using System.Text.Json;
using PersonalAI.Core.Workflows;

namespace PersonalAI.Infrastructure.Workflows;

public sealed class FileSystemWorkflowManifestLoader : IWorkflowManifestLoader
{
    private static readonly HashSet<string> ExecutableFieldNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "script",
            "scripts",
            "command",
            "commands",
            "shell",
            "url",
            "installUrl",
            "steps"
        };

    private readonly string _skillsFolder;

    public FileSystemWorkflowManifestLoader(string skillsFolder)
    {
        if (string.IsNullOrWhiteSpace(skillsFolder))
        {
            throw new ArgumentException("A skills folder is required.", nameof(skillsFolder));
        }

        _skillsFolder = skillsFolder;
    }

    public async ValueTask<WorkflowManifestDiscoveryResult> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_skillsFolder))
        {
            return new WorkflowManifestDiscoveryResult([], []);
        }

        var manifests = new List<WorkflowManifest>();
        var errors = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = Directory.EnumerateFiles(
                _skillsFolder,
                "*.manifest.json",
                SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = File.OpenRead(path);
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);

                if (ContainsExecutableField(document.RootElement))
                {
                    errors.Add("manifest_contains_executable_fields");
                    continue;
                }

                var manifest = ParseManifest(document.RootElement);
                if (!seenIds.Add(manifest.Id))
                {
                    errors.Add("duplicate_manifest_id");
                    continue;
                }

                manifests.Add(manifest);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                errors.Add("manifest_invalid");
            }
        }

        return new WorkflowManifestDiscoveryResult(manifests, errors);
    }

    private static WorkflowManifest ParseManifest(JsonElement root)
    {
        var id = GetRequiredString(root, "id");
        var name = GetRequiredString(root, "name");
        var description = GetRequiredString(root, "description");
        var version = GetRequiredString(root, "version");
        var riskText = GetRequiredString(root, "riskLevel");

        if (!Enum.TryParse<WorkflowRiskLevel>(riskText, ignoreCase: true, out var riskLevel))
        {
            throw new JsonException();
        }

        return new WorkflowManifest(
            id,
            name,
            description,
            version,
            GetOptionalString(root, "author") ?? GetOptionalString(root, "source"),
            GetStringArray(root, "requiredCapabilities"),
            GetStringArray(root, "requiredTools"),
            riskLevel,
            GetRawProperty(root, "inputSchema"),
            GetRawProperty(root, "outputSchema"));
    }

    private static bool ContainsExecutableField(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (ExecutableFieldNames.Contains(property.Name) ||
                    ContainsExecutableField(property.Value))
                {
                    return true;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsExecutableField(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string GetRequiredString(JsonElement root, string name)
    {
        var value = GetOptionalString(root, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException();
        }

        return value;
    }

    private static string? GetOptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static IReadOnlyList<string> GetStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property))
        {
            return [];
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException();
        }

        return property.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : throw new JsonException())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
    }

    private static string? GetRawProperty(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property)
            ? property.GetRawText()
            : null;
}
