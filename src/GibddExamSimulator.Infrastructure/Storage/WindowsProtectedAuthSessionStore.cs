using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GibddExamSimulator.Application.Storage;

namespace GibddExamSimulator.Infrastructure.Storage;

public sealed class WindowsProtectedAuthSessionStore : IAuthSessionStore
{
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("GibddExamSimulator.AuthSession.v2");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;

    public WindowsProtectedAuthSessionStore(string path)
    {
        _path = Path.GetFullPath(path);
    }

    public async Task<AuthSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return null;
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI token storage is available only on Windows.");
        var encrypted = await File.ReadAllBytesAsync(_path, cancellationToken);
        try
        {
            var clear = ProtectedData.Unprotect(encrypted, OptionalEntropy, DataProtectionScope.CurrentUser);
            try
            {
                return JsonSerializer.Deserialize<AuthSession>(clear, JsonOptions);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clear);
            }
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public async Task SaveAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI token storage is available only on Windows.");
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var clear = JsonSerializer.SerializeToUtf8Bytes(session, JsonOptions);
        try
        {
            var encrypted = ProtectedData.Protect(clear, OptionalEntropy, DataProtectionScope.CurrentUser);
            var temporaryPath = _path + ".new";
            await File.WriteAllBytesAsync(temporaryPath, encrypted, cancellationToken);
            File.Move(temporaryPath, _path, overwrite: true);
            CryptographicOperations.ZeroMemory(encrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_path))
            File.Delete(_path);
        return Task.CompletedTask;
    }
}
