using GibddExamSimulator.Application.Storage;
using GibddExamSimulator.Application.StudySessions;

namespace GibddExamSimulator.Application.Synchronization;

public enum PairingRequestStatus
{
    Pending,
    Completed,
    Expired,
    Cancelled
}

public sealed record DeviceLinkState
{
    public required Guid DeviceId { get; init; }
    public Guid? ProfileId { get; init; }
    public bool HasPeerDevice { get; init; }
    public bool OnboardingSkipped { get; init; }
    public bool TelegramLinked { get; init; }
    public long LatestRevision { get; init; }
    public DateTimeOffset? LastValidatedAtUtc { get; init; }
}

public sealed record DeviceBootstrap(
    Guid ProfileId,
    bool HasPeerDevice,
    bool TelegramLinked,
    long LatestRevision,
    DateTimeOffset ServerTimeUtc);

public sealed record PairedDevice(
    Guid DeviceId,
    StudyDeviceKind DeviceKind,
    string DeviceName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastSeenAtUtc,
    bool IsCurrentDevice);

public sealed record PairingStartResult(
    Guid PairingId,
    string QrPayload,
    string ShortCode,
    DateTimeOffset ExpiresAtUtc);

public sealed record PairingStatusResult(
    PairingRequestStatus Status,
    Guid? ProfileId,
    string? LinkedDeviceName,
    DateTimeOffset ExpiresAtUtc);

public sealed record PairingCompleteResult(
    Guid ProfileId,
    long LatestRevision,
    string LinkedDeviceName);

public sealed record TelegramLinkResult(
    Uri DeepLink,
    DateTimeOffset ExpiresAtUtc);

public sealed record SyncApiHealth(
    string Status,
    string ApiVersion,
    string MinimumClientVersion,
    string BankVersion,
    string EnvironmentId,
    DateTimeOffset ServerTimeUtc);

public interface IDeviceLinkStateStore
{
    Task<DeviceLinkState?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(DeviceLinkState state, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public interface IDeviceApiRemote
{
    Task<SyncApiHealth> GetHealthAsync(CancellationToken cancellationToken = default);

    Task<DeviceBootstrap> BootstrapAsync(
        AuthSession auth,
        Guid deviceId,
        StudyDeviceKind deviceKind,
        string deviceName,
        CancellationToken cancellationToken = default);

    Task<PairingStartResult> StartPairingAsync(
        AuthSession auth,
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<PairingStatusResult> GetPairingStatusAsync(
        AuthSession auth,
        Guid pairingId,
        CancellationToken cancellationToken = default);

    Task<PairingCompleteResult> CompletePairingAsync(
        AuthSession auth,
        Guid deviceId,
        StudyDeviceKind deviceKind,
        string deviceName,
        Guid pairingId,
        string oneTimeSecret,
        CancellationToken cancellationToken = default);

    Task<PairingCompleteResult> CompletePairingWithShortCodeAsync(
        AuthSession auth,
        Guid deviceId,
        StudyDeviceKind deviceKind,
        string deviceName,
        string shortCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PairedDevice>> ListDevicesAsync(
        AuthSession auth,
        CancellationToken cancellationToken = default);

    Task RevokeDeviceAsync(
        AuthSession auth,
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<TelegramLinkResult> StartTelegramLinkAsync(
        AuthSession auth,
        CancellationToken cancellationToken = default);
}

public sealed record DeviceConnectionResult(
    AuthSession Auth,
    DeviceLinkState LinkState,
    bool IsOffline);

public sealed class DeviceConnectionCoordinator
{
    private readonly IAuthSessionStore _authStore;
    private readonly IDeviceLinkStateStore _linkStateStore;
    private readonly IAuthClient _authClient;
    private readonly IDeviceApiRemote _remote;
    private readonly TimeProvider _timeProvider;

    public DeviceConnectionCoordinator(
        IAuthSessionStore authStore,
        IDeviceLinkStateStore linkStateStore,
        IAuthClient authClient,
        IDeviceApiRemote remote,
        TimeProvider? timeProvider = null)
    {
        _authStore = authStore;
        _linkStateStore = linkStateStore;
        _authClient = authClient;
        _remote = remote;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DeviceConnectionResult> InitializeAsync(
        Guid deviceId,
        StudyDeviceKind deviceKind,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty)
            throw new ArgumentException("Device identifier must not be empty.", nameof(deviceId));
        if (string.IsNullOrWhiteSpace(deviceName))
            throw new ArgumentException("Device name must not be empty.", nameof(deviceName));

        var auth = await _authStore.LoadAsync(cancellationToken);
        try
        {
            if (auth is null)
            {
                auth = await _authClient.CreateAnonymousAsync(cancellationToken);
                await _authStore.SaveAsync(auth, cancellationToken);
            }
            else if (auth.NeedsRefresh(_timeProvider.GetUtcNow()))
            {
                auth = await _authClient.RefreshAsync(auth, cancellationToken);
                await _authStore.SaveAsync(auth, cancellationToken);
            }

            var bootstrap = await _remote.BootstrapAsync(
                auth,
                deviceId,
                deviceKind,
                deviceName.Trim(),
                cancellationToken);
            var state = new DeviceLinkState
            {
                DeviceId = deviceId,
                ProfileId = bootstrap.ProfileId,
                HasPeerDevice = bootstrap.HasPeerDevice,
                TelegramLinked = bootstrap.TelegramLinked,
                LatestRevision = bootstrap.LatestRevision,
                LastValidatedAtUtc = _timeProvider.GetUtcNow(),
                OnboardingSkipped = false
            };
            await _linkStateStore.SaveAsync(state, cancellationToken);
            return new DeviceConnectionResult(auth, state, false);
        }
        catch (HttpRequestException)
        {
            if (auth is null)
                throw;
            var cached = await _linkStateStore.LoadAsync(cancellationToken) ?? new DeviceLinkState
            {
                DeviceId = deviceId
            };
            return new DeviceConnectionResult(auth, cached, true);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (auth is null)
                throw;
            var cached = await _linkStateStore.LoadAsync(cancellationToken) ?? new DeviceLinkState
            {
                DeviceId = deviceId
            };
            return new DeviceConnectionResult(auth, cached, true);
        }
    }

    public async Task<DeviceLinkState> ApplyPairingAsync(
        DeviceLinkState current,
        PairingCompleteResult completed,
        CancellationToken cancellationToken = default)
    {
        var updated = current with
        {
            ProfileId = completed.ProfileId,
            HasPeerDevice = true,
            LatestRevision = completed.LatestRevision,
            LastValidatedAtUtc = _timeProvider.GetUtcNow(),
            OnboardingSkipped = false
        };
        await _linkStateStore.SaveAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<DeviceLinkState> SkipOnboardingAsync(
        DeviceLinkState current,
        CancellationToken cancellationToken = default)
    {
        var updated = current with { OnboardingSkipped = true };
        await _linkStateStore.SaveAsync(updated, cancellationToken);
        return updated;
    }
}
