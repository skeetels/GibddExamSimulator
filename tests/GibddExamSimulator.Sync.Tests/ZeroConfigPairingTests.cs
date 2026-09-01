using System.Net;
using System.Text;
using GibddExamSimulator.Application.Storage;
using GibddExamSimulator.Application.StudySessions;
using GibddExamSimulator.Application.Synchronization;
using GibddExamSimulator.Sync;

namespace GibddExamSimulator.Sync.Tests;

public sealed class ZeroConfigPairingTests
{
    [Fact]
    public async Task NewDevice_CreatesAnonymousIdentityWithoutCredentials_OnlyOnce()
    {
        var authStore = new MemoryAuthStore();
        var linkStore = new MemoryLinkStore();
        var authClient = new FakeAuthClient();
        var profileId = Guid.NewGuid();
        var remote = new FakeDeviceApiRemote(new DeviceBootstrap(
            profileId,
            false,
            false,
            0,
            DateTimeOffset.UtcNow));
        var coordinator = new DeviceConnectionCoordinator(authStore, linkStore, authClient, remote);
        var deviceId = Guid.NewGuid();

        var first = await coordinator.InitializeAsync(deviceId, StudyDeviceKind.AndroidApp, "Телефон");
        var second = await coordinator.InitializeAsync(deviceId, StudyDeviceKind.AndroidApp, "Телефон");

        Assert.Equal(1, authClient.CreateAnonymousCalls);
        Assert.Equal(first.Auth.UserId, second.Auth.UserId);
        Assert.Equal(profileId, second.LinkState.ProfileId);
        Assert.False(second.LinkState.HasPeerDevice);
        Assert.DoesNotContain('@', second.Auth.Email);
    }

    [Fact]
    public async Task OfflineBootstrap_UsesEncryptedCachedLinkState()
    {
        var auth = FakeAuthClient.Session();
        var deviceId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var authStore = new MemoryAuthStore { Value = auth };
        var linkStore = new MemoryLinkStore
        {
            Value = new DeviceLinkState
            {
                DeviceId = deviceId,
                ProfileId = profileId,
                HasPeerDevice = true,
                LastValidatedAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
            }
        };
        var coordinator = new DeviceConnectionCoordinator(
            authStore,
            linkStore,
            new FakeAuthClient(),
            new FakeDeviceApiRemote(new HttpRequestException("offline")));

        var result = await coordinator.InitializeAsync(deviceId, StudyDeviceKind.WindowsDesktop, "Компьютер");

        Assert.True(result.IsOffline);
        Assert.True(result.LinkState.HasPeerDevice);
        Assert.Equal(profileId, result.LinkState.ProfileId);
    }

    [Fact]
    public async Task OfflineRefresh_KeepsCachedAnonymousIdentityAndHistoryScope()
    {
        var auth = FakeAuthClient.Session() with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5) };
        var deviceId = Guid.NewGuid();
        var authStore = new MemoryAuthStore { Value = auth };
        var linkStore = new MemoryLinkStore { Value = new DeviceLinkState { DeviceId = deviceId, HasPeerDevice = true } };
        var coordinator = new DeviceConnectionCoordinator(
            authStore,
            linkStore,
            new FakeAuthClient { RefreshException = new HttpRequestException("offline") },
            new FakeDeviceApiRemote(new InvalidOperationException("must not bootstrap")));

        var result = await coordinator.InitializeAsync(deviceId, StudyDeviceKind.AndroidApp, "Телефон");

        Assert.True(result.IsOffline);
        Assert.Equal(auth.UserId, result.Auth.UserId);
        Assert.True(result.LinkState.HasPeerDevice);
    }

    [Fact]
    public void PairingInvitation_AcceptsOnlyMatchingHttpsEnvironment()
    {
        var id = Guid.NewGuid();
        var secret = new string('A', 43);
        var invitation = PairingInvitation.Parse(
            $"https://study.test/pair?v=1&id={id:D}&secret={secret}&env=production",
            "production");

        Assert.Equal(id, invitation.PairingId);
        Assert.Equal(secret, invitation.OneTimeSecret);
        Assert.Throws<InvalidDataException>(() => PairingInvitation.Parse(
            $"https://study.test/pair?v=1&id={id:D}&secret={secret}&env=staging",
            "production"));
        Assert.Throws<InvalidDataException>(() => PairingInvitation.Parse(
            $"http://study.test/pair?v=1&id={id:D}&secret={secret}&env=production",
            "production"));
    }

    [Fact]
    public async Task SupabaseAnonymousSignup_SendsNoEmailOrPassword()
    {
        var userId = Guid.NewGuid();
        var handler = new SingleResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"access_token\":\"access\",\"refresh_token\":\"refresh\",\"expires_in\":3600," +
                $"\"user\":{{\"id\":\"{userId:D}\"}}}}",
                Encoding.UTF8,
                "application/json")
        });
        var client = new SupabaseAuthClient(new SupabaseClientOptions
        {
            ProjectUrl = new Uri("https://test-project.supabase.co"),
            PublishableKey = "sb_publishable_test_value",
            EnvironmentId = "test"
        }, new HttpClient(handler));

        var session = await client.CreateAnonymousAsync();

        Assert.Equal(userId, session.UserId);
        Assert.EndsWith("/auth/v1/signup", handler.Uri, StringComparison.Ordinal);
        Assert.Equal("{}", handler.Body);
        Assert.DoesNotContain("email", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", handler.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeviceApi_ListsAndRevokesOnlyTheSelectedDevice()
    {
        var selectedId = Guid.NewGuid();
        var handler = new SequenceResponseHandler(
            JsonResponse("{\"items\":[{\"deviceId\":\"" + selectedId.ToString("D") +
                         "\",\"deviceKind\":\"AndroidApp\",\"deviceName\":\"Телефон\"," +
                         "\"createdAtUtc\":\"2026-09-01T08:00:00Z\",\"lastSeenAtUtc\":\"2026-09-01T09:00:00Z\"," +
                         "\"isCurrentDevice\":false}]}"),
            JsonResponse("{\"ok\":true}"));
        var options = new SupabaseClientOptions
        {
            ProjectUrl = new Uri("https://project.supabase.co"),
            PublishableKey = "sb_publishable_test_value",
            SyncApiBaseUrl = new Uri("https://project.supabase.co/functions/v1/device-api/"),
            EnvironmentId = "test"
        };
        var remote = new SupabaseDeviceApiRemote(options, new HttpClient(handler));
        var auth = FakeAuthClient.Session();

        var devices = await remote.ListDevicesAsync(auth);
        await remote.RevokeDeviceAsync(auth, selectedId);

        var device = Assert.Single(devices);
        Assert.Equal(selectedId, device.DeviceId);
        Assert.Equal(StudyDeviceKind.AndroidApp, device.DeviceKind);
        Assert.Equal("Телефон", device.DeviceName);
        Assert.EndsWith("/devices/list", handler.Requests[0].Uri, StringComparison.Ordinal);
        Assert.EndsWith("/devices/revoke", handler.Requests[1].Uri, StringComparison.Ordinal);
        Assert.Contains(selectedId.ToString("D"), handler.Requests[1].Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("profileId", handler.Requests[1].Body, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class MemoryAuthStore : IAuthSessionStore
    {
        public AuthSession? Value { get; set; }
        public Task<AuthSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task SaveAsync(AuthSession session, CancellationToken cancellationToken = default)
        {
            Value = session;
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Value = null;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryLinkStore : IDeviceLinkStateStore
    {
        public DeviceLinkState? Value { get; set; }
        public Task<DeviceLinkState?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task SaveAsync(DeviceLinkState state, CancellationToken cancellationToken = default)
        {
            Value = state;
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Value = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthClient : IAuthClient
    {
        public int CreateAnonymousCalls { get; private set; }
        public Exception? RefreshException { get; init; }
        public static AuthSession Session() => new(
            Guid.NewGuid(), string.Empty, "access", "refresh", DateTimeOffset.UtcNow.AddHours(1));
        public Task<AuthSession> CreateAnonymousAsync(CancellationToken cancellationToken = default)
        {
            CreateAnonymousCalls++;
            return Task.FromResult(Session());
        }
        public Task<AuthSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AuthSession> RefreshAsync(AuthSession session, CancellationToken cancellationToken = default) =>
            RefreshException is null
                ? Task.FromResult(session with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1) })
                : Task.FromException<AuthSession>(RefreshException);
        public Task SignOutAsync(AuthSession session, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeDeviceApiRemote : IDeviceApiRemote
    {
        private readonly DeviceBootstrap? _bootstrap;
        private readonly Exception? _exception;
        public FakeDeviceApiRemote(DeviceBootstrap bootstrap) => _bootstrap = bootstrap;
        public FakeDeviceApiRemote(Exception exception) => _exception = exception;
        public Task<DeviceBootstrap> BootstrapAsync(AuthSession auth, Guid deviceId, StudyDeviceKind deviceKind, string deviceName, CancellationToken cancellationToken = default) =>
            _exception is null ? Task.FromResult(_bootstrap!) : Task.FromException<DeviceBootstrap>(_exception);
        public Task<SyncApiHealth> GetHealthAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PairingStartResult> StartPairingAsync(AuthSession auth, Guid deviceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PairingStatusResult> GetPairingStatusAsync(AuthSession auth, Guid pairingId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PairingCompleteResult> CompletePairingAsync(AuthSession auth, Guid deviceId, StudyDeviceKind deviceKind, string deviceName, Guid pairingId, string oneTimeSecret, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PairingCompleteResult> CompletePairingWithShortCodeAsync(AuthSession auth, Guid deviceId, StudyDeviceKind deviceKind, string deviceName, string shortCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PairedDevice>> ListDevicesAsync(AuthSession auth, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RevokeDeviceAsync(AuthSession auth, Guid deviceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TelegramLinkResult> StartTelegramLinkAsync(AuthSession auth, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SingleResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public string Uri { get; private set; } = string.Empty;
        public string Body { get; private set; } = string.Empty;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri!.AbsoluteUri;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }

    private sealed class SequenceResponseHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private int _index;
        public List<(string Uri, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.AbsoluteUri, body));
            return responses[_index++];
        }
    }
}
