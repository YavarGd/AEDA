using System.Security.Cryptography;
using System.Text;
using PersonalAI.Core.Providers;

namespace PersonalAI.Infrastructure.Settings;

public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _directory;

    public DpapiSecretStore()
        : this(GetDefaultSecretDirectory())
    {
    }

    public DpapiSecretStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public Task<bool> ExistsAsync(
        string logicalKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(GetPath(logicalKey)));
    }

    public async Task<SecretValue?> GetAsync(
        string logicalKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(logicalKey);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            var bytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            var value = Encoding.UTF8.GetString(bytes);
            CryptographicOperations.ZeroMemory(bytes);
            return string.IsNullOrWhiteSpace(value) ? null : new SecretValue(value);
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is CryptographicException ||
            exception is NotSupportedException)
        {
            return null;
        }
    }

    public async Task SetAsync(
        string logicalKey,
        SecretValue secret,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(secret);
        Directory.CreateDirectory(_directory);

        var bytes = Encoding.UTF8.GetBytes(secret.Value);
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new NotSupportedException("DPAPI is only available on Windows.");
            }

            var protectedBytes = ProtectedData.Protect(
                bytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(
                GetPath(logicalKey),
                protectedBytes,
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public Task DeleteAsync(
        string logicalKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(logicalKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetPath(string logicalKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalKey);
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(logicalKey.Trim()));
        var fileName = Convert.ToHexString(keyBytes).ToLowerInvariant() + ".secret";
        return Path.Combine(_directory, fileName);
    }

    private static string GetDefaultSecretDirectory()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "PersonalAI", "secrets");
    }
}
