namespace PersonalAI.Core.Providers;

public sealed record SecretValue(string Value)
{
    public override string ToString() => "********";
}

public interface ISecretStore
{
    Task<bool> ExistsAsync(
        string logicalKey,
        CancellationToken cancellationToken = default);

    Task<SecretValue?> GetAsync(
        string logicalKey,
        CancellationToken cancellationToken = default);

    Task SetAsync(
        string logicalKey,
        SecretValue secret,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string logicalKey,
        CancellationToken cancellationToken = default);
}

public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, SecretValue> _secrets =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> ExistsAsync(
        string logicalKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_secrets.ContainsKey(NormalizeKey(logicalKey)));
    }

    public Task<SecretValue?> GetAsync(
        string logicalKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _secrets.TryGetValue(NormalizeKey(logicalKey), out var value);
        return Task.FromResult(value);
    }

    public Task SetAsync(
        string logicalKey,
        SecretValue secret,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(secret);
        _secrets[NormalizeKey(logicalKey)] = secret;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string logicalKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _secrets.Remove(NormalizeKey(logicalKey));
        return Task.CompletedTask;
    }

    private static string NormalizeKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }
}
