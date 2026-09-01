using System.Net;
using System.Text.Json;
using GibddExamSimulator.Application.Storage;
using GibddExamSimulator.Application.StudySessions;
using GibddExamSimulator.Sync;

namespace GibddExamSimulator.Sync.Tests;

public sealed class SupabaseStudySessionRemoteTests
{
    [Fact]
    public async Task ExamUpload_IncludesDeviceKindAndInvokesAutomaticTelegramFunction()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.Created),
            new HttpResponseMessage(HttpStatusCode.OK));
        var remote = CreateRemote(handler);

        var result = await remote.UploadAsync(CreateAuth(), CreateSession(StudyMode.Exam));

        Assert.Equal(UploadDisposition.Uploaded, result.Disposition);
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith("/rest/v1/study_sessions", handler.Requests[0].Uri, StringComparison.Ordinal);
        Assert.Contains("\"device_kind\":\"MobilePwa\"", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.EndsWith("/functions/v1/telegram-report", handler.Requests[1].Uri, StringComparison.Ordinal);
        Assert.Contains("sessionId", handler.Requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrainingUpload_DoesNotInvokeTelegramFunction()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Created));
        var remote = CreateRemote(handler);

        await remote.UploadAsync(CreateAuth(), CreateSession(StudyMode.SmartTen));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task BusyTelegramDelivery_RemainsInOutboxForAutomaticRetry()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.Created),
            new HttpResponseMessage(HttpStatusCode.Accepted));
        var remote = CreateRemote(handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => remote.UploadAsync(CreateAuth(), CreateSession(StudyMode.Exam)));

        Assert.Equal(HttpStatusCode.Accepted, exception.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void SecretSupabaseKey_IsRejectedByClientOptions()
    {
        var options = new SupabaseClientOptions
        {
            ProjectUrl = new Uri("https://example.supabase.co"),
            PublishableKey = "sb_secret_not_for_clients"
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private static SupabaseStudySessionRemote CreateRemote(HttpMessageHandler handler) => new(
        new SupabaseClientOptions
        {
            ProjectUrl = new Uri("https://example.supabase.co"),
            PublishableKey = "sb_publishable_test_value"
        },
        new HttpClient(handler));

    private static AuthSession CreateAuth() => new(
        Guid.NewGuid(),
        "candidate@example.test",
        "header.payload.signature",
        "refresh",
        DateTimeOffset.UtcNow.AddHours(1));

    private static StudySessionEnvelope CreateSession(StudyMode mode) => new StudySessionEnvelope
    {
        SessionId = Guid.NewGuid(),
        DeviceId = Guid.NewGuid(),
        DeviceKind = StudyDeviceKind.MobilePwa,
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

    private sealed class RecordingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<(string Uri, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.ToString(), body));
            return _responses.Dequeue();
        }
    }
}
