using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GibddExamSimulator.Application.Storage;
using GibddExamSimulator.Application.StudySessions;

namespace GibddExamSimulator.Sync;

public sealed class SupabaseStudySessionRemote : IStudySessionRemote
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _httpClient;
    private readonly SupabaseClientOptions _options;

    public SupabaseStudySessionRemote(SupabaseClientOptions options, HttpClient? httpClient = null)
    {
        options.Validate();
        _options = options;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
    }

    public async Task<UploadResult> UploadAsync(
        AuthSession auth,
        StudySessionEnvelope original,
        CancellationToken cancellationToken = default)
    {
        var session = string.IsNullOrWhiteSpace(original.PayloadSha256) ? original.WithComputedHash() : original;
        session.Validate();
        using var request = CreateAuthorizedRequest(auth, HttpMethod.Post, "sync/push");
        request.Content = JsonContent.Create(new { session }, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreateSyncExceptionAsync(
                response,
                "Не удалось синхронизировать завершённую сессию.",
                cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<PushResponse>(stream, JsonOptions, cancellationToken)
                      ?? throw new SupabaseProtocolException("Sync API returned an empty push response.");
        if (!Enum.TryParse<UploadDisposition>(payload.Disposition, ignoreCase: true, out var disposition))
            throw new SupabaseProtocolException("Sync API returned an unknown upload disposition.");
        return new UploadResult(disposition, payload.Message ?? string.Empty);
    }

    public async Task<RemoteStudyPage> PullAsync(
        AuthSession auth,
        long afterServerSequence,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 500);
        var relative = $"sync/pull?after={Math.Max(0, afterServerSequence)}&limit={pageSize}";
        using var request = CreateAuthorizedRequest(auth, HttpMethod.Get, relative);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreateSyncExceptionAsync(
                response,
                "Не удалось получить общую историю.",
                cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<PullResponse>(stream, JsonOptions, cancellationToken)
                      ?? throw new SupabaseProtocolException("Sync API returned an empty pull response.");
        var items = payload.Items.Select(item =>
        {
            item.Session.Validate();
            return new RemoteStudySession(item.ServerSequence, item.Session);
        }).ToArray();
        return new RemoteStudyPage(items, payload.HasMore);
    }

    private HttpRequestMessage CreateAuthorizedRequest(
        AuthSession auth,
        HttpMethod method,
        string relativePath)
    {
        var request = new HttpRequestMessage(method, _options.ResolveSyncApi(relativePath));
        request.Headers.Add("apikey", _options.PublishableKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Environment-Id", _options.EnvironmentId);
        return request;
    }

    private static async Task<Exception> CreateSyncExceptionAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken)
    {
        var protocol = await SupabaseAuthClient.CreateProtocolExceptionAsync(response, fallback, cancellationToken);
        var status = (int)response.StatusCode;
        return status is 408 or 425 or 429 || status >= 500
            ? new HttpRequestException(protocol.Message, protocol, response.StatusCode)
            : protocol;
    }

    private sealed record PushResponse(string Disposition, string? Message);
    private sealed record PullResponse(IReadOnlyList<PullItem> Items, bool HasMore);
    private sealed record PullItem(long ServerSequence, StudySessionEnvelope Session);
}
