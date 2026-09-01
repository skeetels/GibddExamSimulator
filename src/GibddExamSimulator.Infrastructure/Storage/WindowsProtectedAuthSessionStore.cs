using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GibddExamSimulator.Application.Storage;
using GibddExamSimulator.Application.Synchronization;

namespace GibddExamSimulator.Infrastructure.Storage;

public sealed class WindowsProtectedAuthSessionStore : IAuthSessionStore, IDeviceLinkStateStore
{
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("GibddExamSimulator.AuthSession.v2");
    private static readonly byte[] LinkStateEntropy = Encoding.UTF8.GetBytes("GibddExamSimulator.DeviceLink.v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;

    public WindowsProtectedAuthSessionStore(string path)
    {
        _path = Path.GetFullPath(path);
    }

    public async Task<AuthSession?> LoadAsync(CancellationToken cancellationToken = default)
        => await LoadProtectedAsync<AuthSession>(_path, OptionalEntropy, cancellationToken);

    public async Task<DeviceLinkState?> LoadLinkStateAsync(CancellationToken cancellationToken = default)
        => await LoadProtectedAsync<DeviceLinkState>(LinkStatePath, LinkStateEntropy, cancellationToken);

    Task<DeviceLinkState?> IDeviceLinkStateStore.LoadAsync(CancellationToken cancellationToken) =>
        LoadLinkStateAsync(cancellationToken);

    private static async Task<T?> LoadProtectedAsync<T>(
        string path,
        byte[] entropy,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!File.Exists(path))
            return null;
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI token storage is available only on Windows.");
        var encrypted = await File.ReadAllBytesAsync(path, cancellationToken);
        try
        {
            var clear = ProtectedData.Unprotect(encrypted, entropy, DataProtectionScope.CurrentUser);
            try
            {
                return JsonSerializer.Deserialize<T>(clear, JsonOptions);
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
        => await SaveProtectedAsync(_path, session, OptionalEntropy, cancellationToken);

    public async Task SaveAsync(DeviceLinkState state, CancellationToken cancellationToken = default)
        => await SaveProtectedAsync(LinkStatePath, state, LinkStateEntropy, cancellationToken);

    private static async Task SaveProtectedAsync<T>(
        string path,
        T value,
        byte[] entropy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI token storage is available only on Windows.");
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var clear = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        try
        {
            var encrypted = ProtectedData.Protect(clear, entropy, DataProtectionScope.CurrentUser);
            var temporaryPath = path + ".new";
            await File.WriteAllBytesAsync(temporaryPath, encrypted, cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
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

    async Task IDeviceLinkStateStore.ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(LinkStatePath))
            File.Delete(LinkStatePath);
        await Task.CompletedTask;
    }

    private string LinkStatePath => _path + ".device-link";
}
