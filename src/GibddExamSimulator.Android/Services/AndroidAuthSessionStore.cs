using System.Text.Json;
using GibddExamSimulator.Application.Storage;
using GibddExamSimulator.Application.Synchronization;

namespace GibddExamSimulator.Android;

public sealed class AndroidAuthSessionStore : IAuthSessionStore, IDeviceLinkStateStore
{
    private const string StorageKey = "gibdd.auth-session.v1";
    private const string LinkStateStorageKey = "gibdd.device-link.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AuthSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var json = await SecureStorage.Default.GetAsync(StorageKey);
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<AuthSession>(json, JsonOptions);
        }
        catch
        {
            SecureStorage.Default.Remove(StorageKey);
            return null;
        }
    }

    public async Task SaveAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await SecureStorage.Default.SetAsync(StorageKey, JsonSerializer.Serialize(session, JsonOptions));
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureStorage.Default.Remove(StorageKey);
        return Task.CompletedTask;
    }

    async Task<DeviceLinkState?> IDeviceLinkStateStore.LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var json = await SecureStorage.Default.GetAsync(LinkStateStorageKey);
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<DeviceLinkState>(json, JsonOptions);
        }
        catch
        {
            SecureStorage.Default.Remove(LinkStateStorageKey);
            return null;
        }
    }

    async Task IDeviceLinkStateStore.SaveAsync(DeviceLinkState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await SecureStorage.Default.SetAsync(LinkStateStorageKey, JsonSerializer.Serialize(state, JsonOptions));
    }

    Task IDeviceLinkStateStore.ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureStorage.Default.Remove(LinkStateStorageKey);
        return Task.CompletedTask;
    }
}
