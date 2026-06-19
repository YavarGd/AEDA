using System.Net;

namespace PersonalAI.Core.Providers;

public readonly record struct ProviderId(string Value)
{
    public override string ToString() => Value;

    public static ProviderId Ollama { get; } = new("ollama");
}

public readonly record struct ModelId(string Value)
{
    public override string ToString() => Value;
}

public enum ProviderKind
{
    Ollama,
    OpenAICompatible,
    LocalGateway,
    CloudGateway,
    TestFake
}

public enum ProviderEndpointOrigin
{
    Invalid,
    Local,
    PrivateNetwork,
    Remote
}

[Flags]
public enum ModelCapability
{
    None = 0,
    Chat = 1 << 0,
    StreamingChat = 1 << 1,
    StructuredToolCalls = 1 << 2,
    Embeddings = 1 << 3,
    Vision = 1 << 4,
    Code = 1 << 5,
    Reasoning = 1 << 6,
    LocalOnly = 1 << 7,
    Remote = 1 << 8,
    SupportsJsonMode = 1 << 9,
    SupportsSystemPrompt = 1 << 10,
    SupportsImages = 1 << 11,
    SupportsTools = 1 << 12
}

public enum ProviderStatus
{
    Available,
    Disabled,
    Unconfigured,
    Unavailable
}

public sealed record ProviderEndpoint(
    Uri? BaseUri,
    ProviderEndpointOrigin Origin,
    string? SafeReasonCode = null)
{
    public bool IsUsable => BaseUri is not null &&
        Origin is not ProviderEndpointOrigin.Invalid;
}

public sealed record ProviderHealth(
    ProviderStatus Status,
    string? SafeReasonCode = null)
{
    public static ProviderHealth Available { get; } = new(ProviderStatus.Available);
}

public sealed record ModelSafetyProfile(
    bool IsLocalOnly,
    bool IsRemote,
    bool AllowsWorkspaceContext,
    bool AllowsMemoryContext,
    bool AllowsScreenshots,
    bool AllowsClipboardOrAppContext);

public sealed record ModelDescriptor(
    ProviderId ProviderId,
    ModelId ModelId,
    ModelCapability Capabilities,
    ModelSafetyProfile SafetyProfile,
    string DisplayName);

public sealed record ProviderProfile(
    ProviderId Id,
    ProviderKind Kind,
    string DisplayName,
    ProviderEndpoint Endpoint,
    bool IsEnabled,
    string? ChatModel,
    string? EmbeddingModel,
    string? SecretReference,
    IReadOnlyList<ModelDescriptor> Models)
{
    public bool IsLocal => Endpoint.Origin == ProviderEndpointOrigin.Local ||
        Kind == ProviderKind.Ollama ||
        Kind == ProviderKind.TestFake;

    public bool IsRemote => Endpoint.Origin == ProviderEndpointOrigin.Remote ||
        Kind == ProviderKind.CloudGateway;
}

public static class ProviderEndpointClassifier
{
    public static ProviderEndpoint Classify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return new ProviderEndpoint(null, ProviderEndpointOrigin.Invalid, "provider_endpoint_invalid");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return new ProviderEndpoint(
                SanitizeUri(uri),
                ProviderEndpointOrigin.Invalid,
                "provider_endpoint_credentials_not_allowed");
        }

        var host = uri.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderEndpoint(uri, ProviderEndpointOrigin.Local);
        }

        if (IPAddress.TryParse(host, out var address))
        {
            if (IPAddress.IsLoopback(address))
            {
                return new ProviderEndpoint(uri, ProviderEndpointOrigin.Local);
            }

            if (IsPrivateNetwork(address))
            {
                return new ProviderEndpoint(uri, ProviderEndpointOrigin.PrivateNetwork);
            }
        }

        return new ProviderEndpoint(uri, ProviderEndpointOrigin.Remote);
    }

    public static string SafeDisplay(ProviderEndpoint endpoint)
    {
        if (endpoint.BaseUri is null)
        {
            return endpoint.SafeReasonCode ?? "provider_endpoint_invalid";
        }

        var builder = new UriBuilder(endpoint.BaseUri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty
        };
        return builder.Uri.ToString();
    }

    private static Uri SanitizeUri(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty
        };
        return builder.Uri;
    }

    private static bool IsPrivateNetwork(IPAddress address)
    {
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                bytes[0] == 192 && bytes[1] == 168;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return (bytes[0] & 0xFE) == 0xFC;
        }

        return false;
    }
}
