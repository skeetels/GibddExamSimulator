using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GibddExamSimulator.Application.Storage;
using GibddExamSimulator.Application.StudySessions;
using GibddExamSimulator.Application.Synchronization;

namespace GibddExamSimulator.Sync;

public sealed class SupabaseDeviceApiRemote : IDeviceApiRemote
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _httpClient;
    private readonly SupabaseClientOptions _options;

    public SupabaseDeviceApiRemote(SupabaseClientOptions options, HttpClient? httpClient = null)
    {
        options.Validate();
        _options = options;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task<SyncApiHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(null, HttpMethod.Get, "health");
        return await SendAsync<SyncApiHealth>(request, "Сервис синхронизации временно недоступен.", cancellationToken);
    }

    public async Task<DeviceBootstrap> BootstrapAsync(
        AuthSession auth,
        Guid deviceId,
        StudyDeviceKind deviceKind,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(auth, HttpMethod.Post, "identity/bootstrap");
        request.Content = JsonContent.Create(new { deviceId, deviceKind, deviceName }, options: JsonOptions);
        return await SendAsync<DeviceBootstrap>(request, "Не удалось подготовить связь устройств.", cancellationToken);
    }

    public async Task<PairingStartResult> StartPairingAsync(
        AuthSession auth,
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(auth, HttpMethod.Post, "pairing/start");
        request.Content = JsonContent.Create(new { deviceId }, options: JsonOptions);
        return await SendAsync<PairingStartResult>(request, "Не удалось создать QR-код. Повторите попытку.", cancellationToken);
    }

    public async Task<PairingStatusResult> GetPairingStatusAsync(
        AuthSession auth,
        Guid pairingId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(auth, HttpMethod.Get, $"pairing/status?id={pairingId:D}");
        return await SendAsync<PairingStatusResult>(request, "Не удалось проверить привязку.", cancellationToken);
    }

    public async Task<PairingCompleteResult> CompletePairingAsync(
        AuthSession auth,
        Guid deviceId,
        StudyDeviceKind deviceKind,
        string deviceName,
        Guid pairingId,
        string oneTimeSecret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(oneTimeSecret) || oneTimeSecret.Length > 1024)
            throw new InvalidOperationException("QR-код повреждён или уже недействителен.");
        using var request = CreateRequest(auth, HttpMethod.Post, "pairing/complete");
        request.Content = JsonContent.Create(
            new { deviceId, deviceKind, deviceName, pairingId, secret = oneTimeSecret },
            options: JsonOptions);
        return await SendAsync<PairingCompleteResult>(request, "Не удалось связать устройства.", cancellationToken);
    }

    public async Task<PairingCompleteResult> CompletePairingWithShortCodeAsync(
        AuthSession auth,
        Guid deviceId,
        StudyDeviceKind deviceKind,
        string deviceName,
        string shortCode,
        CancellationToken cancellationToken = default)
    {
        var normalized = new string((shortCode ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray())
            .ToUpperInvariant();
        if (normalized.Length != 8)
            throw new InvalidOperationException("Введите восьмизначный одноразовый код.");
        using var request = CreateRequest(auth, HttpMethod.Post, "pairing/complete-code");
        request.Content = JsonContent.Create(
            new { deviceId, deviceKind, deviceName, shortCode = normalized },
            options: JsonOptions);
        return await SendAsync<PairingCompleteResult>(request, "Код не подошёл или уже истёк.", cancellationToken);
    }

    public async Task<IReadOnlyList<PairedDevice>> ListDevicesAsync(
        AuthSession auth,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(auth, HttpMethod.Get, "devices/list");
        var response = await SendAsync<DeviceListResponse>(request, "Не удалось получить список устройств.", cancellationToken);
        return response.Items;
    }

    public async Task RevokeDeviceAsync(
        AuthSession auth,
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(auth, HttpMethod.Post, "devices/revoke");
        request.Content = JsonContent.Create(new { deviceId }, options: JsonOptions);
        await SendAsync<OperationResponse>(request, "Не удалось отвязать устройство.", cancellationToken);
    }

    public async Task<TelegramLinkResult> StartTelegramLinkAsync(
        AuthSession auth,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(auth, HttpMethod.Post, "telegram/link");
        request.Content = JsonContent.Create(new { }, options: JsonOptions);
        return await SendAsync<TelegramLinkResult>(request, "Не удалось подключить Telegram.", cancellationToken);
    }

    private HttpRequestMessage CreateRequest(AuthSession? auth, HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, _options.ResolveSyncApi(relativePath));
        request.Headers.Add("apikey", _options.PublishableKey);
        if (auth is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Environment-Id", _options.EnvironmentId);
        return request;
    }

    private async Task<T> SendAsync<T>(
        HttpRequestMessage request,
        string fallback,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await SupabaseAuthClient.CreateProtocolExceptionAsync(response, fallback, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
               ?? throw new SupabaseProtocolException("Sync API returned an empty response.");
    }

    private sealed record DeviceListResponse(IReadOnlyList<PairedDevice> Items);
    private sealed record OperationResponse(bool Ok);
}
