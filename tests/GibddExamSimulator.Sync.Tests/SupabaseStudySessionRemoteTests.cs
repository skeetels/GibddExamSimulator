using System.Net;
using System.Text;
using GibddExamSimulator.Application.Storage;
using GibddExamSimulator.Application.StudySessions;
using GibddExamSimulator.Sync;

namespace GibddExamSimulator.Sync.Tests;

public sealed class SupabaseStudySessionRemoteTests
{
    [Fact]
    public async Task ExamUpload_UsesProfileAwareSyncEndpointWithoutClientTelegramCall()
    {
        var handler = new RecordingHandler(Json(HttpStatusCode.OK, """{"disposition":"Uploaded","message":""}"""));
        var remote = CreateRemote(handler);

        var result = await remote.UploadAsync(CreateAuth(), CreateSession(StudyMode.Exam));

        Assert.Equal(UploadDisposition.Uploaded, result.Disposition);
        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/functions/v1/device-api/sync/push", request.Uri, StringComparison.Ordinal);
        Assert.Contains("\"deviceKind\":\"MobilePwa\"", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("telegram", request.Uri + request.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("production", request.EnvironmentId);
    }

    [Fact]
    public async Task AndroidExamUpload_UsesDistinctDeviceKind()
    {
        var handler = new RecordingHandler(Json(HttpStatusCode.OK, """{"disposition":"Uploaded"}"""));
        var remote = CreateRemote(handler);

        await remote.UploadAsync(CreateAuth(), CreateSession(StudyMode.Exam, StudyDeviceKind.AndroidApp));

        Assert.Contains("\"deviceKind\":\"AndroidApp\"", Assert.Single(handler.Requests).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerIdempotencyDisposition_IsPreserved()
    {
        var handler = new RecordingHandler(Json(HttpStatusCode.OK, """{"disposition":"AlreadyExists"}"""));
        var remote = CreateRemote(handler);

        var result = await remote.UploadAsync(CreateAuth(), CreateSession(StudyMode.SmartTen));

        Assert.Equal(UploadDisposition.AlreadyExists, result.Disposition);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Pull_UsesCursorAndReturnsProfileSessions()
    {
        var session = CreateSession(StudyMode.Exam);
        var body = $$"""{"items":[{"serverSequence":42,"session":{{System.Text.Json.JsonSerializer.Serialize(session)}}}],"hasMore":false}""";
        var handler = new RecordingHandler(Json(HttpStatusCode.OK, body));
        var remote = CreateRemote(handler);

        var page = await remote.PullAsync(CreateAuth(), 41, 100);

        Assert.Single(page.Items);
        Assert.Equal(42, page.Items[0].ServerSequence);
        Assert.Contains("sync/pull?after=41&limit=100", Assert.Single(handler.Requests).Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void SecretSupabaseKey_IsRejectedByClientOptions()
    {
        var options = new SupabaseClientOptions
        {
            ProjectUrl = new Uri("https://test-project.supabase.co"),
            PublishableKey = "sb_secret_not_for_clients",
            SyncApiBaseUrl = new Uri("https://test-project.supabase.co/functions/v1/device-api/"),
            EnvironmentId = "production"
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private static SupabaseStudySessionRemote CreateRemote(HttpMessageHandler handler) => new(
        new SupabaseClientOptions
        {
            ProjectUrl = new Uri("https://test-project.supabase.co"),
            PublishableKey = "sb_publishable_test_value",
            SyncApiBaseUrl = new Uri("https://test-project.supabase.co/functions/v1/device-api/"),
            EnvironmentId = "production"
        },
        new HttpClient(handler));

    private static AuthSession CreateAuth() => new(
        Guid.NewGuid(),
        string.Empty,
        "header.payload.signature",
        "refresh",
        DateTimeOffset.UtcNow.AddHours(1));

    private static StudySessionEnvelope CreateSession(
        StudyMode mode,
        StudyDeviceKind deviceKind = StudyDeviceKind.MobilePwa) => new StudySessionEnvelope
    {
        SessionId = Guid.NewGuid(),
        DeviceId = Guid.NewGuid(),
        DeviceKind = deviceKind,
        Mode = mode,
        StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        CompletedAtUtc = DateTimeOffset.UtcNow,
        Outcome = mode == StudyMode.Exam ? StudyOutcome.Passed : StudyOutcome.Completed,
        BankVersion = "test-ab",
        BankSha256 = new string('B', 64),
        RulesProfile = "test-rules",
        OrderedQuestionIds = [1],
        Answers = [],
        Summary = new StudySessionSummary
        {
            QuestionCount = 1,
            AnsweredCount = 0,
            CorrectCount = 0,
            ErrorCount = 0,
            ElapsedMs = 1000
        }
    }.WithComputedHash();

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<(string Uri, string Body, string? EnvironmentId)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((
                request.RequestUri!.ToString(),
                body,
                request.Headers.TryGetValues("X-Environment-Id", out var values) ? values.Single() : null));
            return _responses.Dequeue();
        }
    }
}
