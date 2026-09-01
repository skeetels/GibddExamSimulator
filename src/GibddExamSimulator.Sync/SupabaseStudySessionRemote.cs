using System.Net;
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
        var row = new UploadRow
        {
            SessionId = session.SessionId,
            DeviceId = session.DeviceId,
            DeviceKind = session.DeviceKind.ToString(),
            Mode = session.Mode.ToString(),
            StartedAt = session.StartedAtUtc,
            CompletedAt = session.CompletedAtUtc,
            Outcome = session.Outcome.ToString(),
            BankVersion = session.BankVersion,
            BankSha256 = session.BankSha256,
            RulesProfile = session.RulesProfile,
            SchemaVersion = session.SchemaVersion,
            Payload = JsonSerializer.SerializeToElement(session, JsonOptions),
            PayloadSha256 = session.PayloadSha256
        };

        using var request = CreateAuthorizedRequest(auth, HttpMethod.Post, "rest/v1/study_sessions");
        request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
        request.Content = JsonContent.Create(row, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            await NotifyTelegramReportAsync(auth, session, cancellationToken);
            return new UploadResult(UploadDisposition.Uploaded);
        }
        if (response.StatusCode != HttpStatusCode.Conflict)
            throw await SupabaseAuthClient.CreateProtocolExceptionAsync(response, "Не удалось загрузить учебную сессию.", cancellationToken);

        var remoteHash = await GetRemoteHashAsync(auth, session.SessionId, cancellationToken);
        if (!string.Equals(remoteHash, session.PayloadSha256, StringComparison.OrdinalIgnoreCase))
            return new UploadResult(UploadDisposition.IntegrityConflict, "The server has the same sessionId with another hash.");

        await NotifyTelegramReportAsync(auth, session, cancellationToken);
        return new UploadResult(UploadDisposition.AlreadyExists);
    }

    public async Task<RemoteStudyPage> PullAsync(
        AuthSession auth,
        long afterServerSequence,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 500);
        var relative = $"rest/v1/study_sessions?select=server_seq,payload&server_seq=gt.{afterServerSequence}&order=server_seq.asc&limit={pageSize}";
        using var request = CreateAuthorizedRequest(auth, HttpMethod.Get, relative);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await SupabaseAuthClient.CreateProtocolExceptionAsync(response, "Не удалось получить облачные сессии.", cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<PullRow>>(stream, JsonOptions, cancellationToken) ?? [];
        var items = new List<RemoteStudySession>(rows.Count);
        foreach (var row in rows)
        {
            var session = row.Payload.Deserialize<StudySessionEnvelope>(JsonOptions)
                          ?? throw new SupabaseProtocolException("The server returned an empty study-session payload.");
            session.Validate();
            items.Add(new RemoteStudySession(row.ServerSequence, session));
        }
        return new RemoteStudyPage(items, rows.Count == pageSize);
    }

    private async Task<string?> GetRemoteHashAsync(
        AuthSession auth,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var relative = $"rest/v1/study_sessions?select=payload_sha256&session_id=eq.{sessionId:D}&limit=1";
        using var request = CreateAuthorizedRequest(auth, HttpMethod.Get, relative);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await SupabaseAuthClient.CreateProtocolExceptionAsync(response, "Не удалось проверить существующую сессию.", cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<HashRow>>(stream, JsonOptions, cancellationToken) ?? [];
        return rows.FirstOrDefault()?.PayloadSha256;
    }

    private async Task NotifyTelegramReportAsync(
        AuthSession auth,
        StudySessionEnvelope session,
        CancellationToken cancellationToken)
    {
        if (session.Mode != StudyMode.Exam)
            return;

        using var request = CreateAuthorizedRequest(auth, HttpMethod.Post, "functions/v1/telegram-report");
        request.Content = JsonContent.Create(new { sessionId = session.SessionId }, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException(
                "Сессия сохранена в облаке, но Telegram-отчёт пока не доставлен. Отправка будет повторена автоматически.",
                inner: null,
                response.StatusCode);
        }
    }

    private HttpRequestMessage CreateAuthorizedRequest(AuthSession auth, HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, _options.Resolve(relativePath));
        request.Headers.Add("apikey", _options.PublishableKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private sealed record UploadRow
    {
        [JsonPropertyName("session_id")] public required Guid SessionId { get; init; }
        [JsonPropertyName("device_id")] public required Guid DeviceId { get; init; }
        [JsonPropertyName("device_kind")] public required string DeviceKind { get; init; }
        [JsonPropertyName("mode")] public required string Mode { get; init; }
        [JsonPropertyName("started_at")] public required DateTimeOffset StartedAt { get; init; }
        [JsonPropertyName("completed_at")] public required DateTimeOffset CompletedAt { get; init; }
        [JsonPropertyName("outcome")] public required string Outcome { get; init; }
        [JsonPropertyName("bank_version")] public required string BankVersion { get; init; }
        [JsonPropertyName("bank_sha256")] public required string BankSha256 { get; init; }
        [JsonPropertyName("rules_profile")] public required string RulesProfile { get; init; }
        [JsonPropertyName("schema_version")] public required int SchemaVersion { get; init; }
        [JsonPropertyName("payload")] public required JsonElement Payload { get; init; }
        [JsonPropertyName("payload_sha256")] public required string PayloadSha256 { get; init; }
    }

    private sealed record PullRow
    {
        [JsonPropertyName("server_seq")] public required long ServerSequence { get; init; }
        [JsonPropertyName("payload")] public required JsonElement Payload { get; init; }
    }

    private sealed record HashRow
    {
        [JsonPropertyName("payload_sha256")] public string? PayloadSha256 { get; init; }
    }
}
