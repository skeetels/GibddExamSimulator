namespace GibddExamSimulator.Application.StudySessions;

public enum StudyMode
{
    Exam,
    SmartTen,
    MistakeReview,
    WeakTopics,
    Ticket,
    Marathon,
    NoMistakeChallenge,
    LegacyImport
}

public enum StudyDeviceKind
{
    WindowsDesktop,
    MobilePwa,
    AndroidApp
}

public enum StudyOutcome
{
    Passed,
    Failed,
    Completed,
    Abandoned
}

public enum StudyStage
{
    Main,
    SupplementaryBriefing,
    Supplementary,
    Training
}

public sealed record StudyAnswerEvent
{
    public required int SequenceNumber { get; init; }
    public required long QuestionId { get; init; }
    public required int TicketNumber { get; init; }
    public required int QuestionNumber { get; init; }
    public required int GroupId { get; init; }
    public required int ThematicBlockId { get; init; }
    public required StudyStage Stage { get; init; }
    public int? SelectedAnswer { get; init; }
    public required int CorrectAnswer { get; init; }
    public required bool IsCorrect { get; init; }
    public required long ResponseTimeMs { get; init; }
    public DateTimeOffset? AnsweredAtUtc { get; init; }
}

public sealed record StudySessionSummary
{
    public required int QuestionCount { get; init; }
    public required int AnsweredCount { get; init; }
    public required int CorrectCount { get; init; }
    public required int ErrorCount { get; init; }
    public required long ElapsedMs { get; init; }
    public int LongestCorrectStreak { get; init; }
}

public sealed record LegacyQuestionAggregate
{
    public required long QuestionId { get; init; }
    public required int GroupId { get; init; }
    public required int AttemptCount { get; init; }
    public required int CorrectCount { get; init; }
    public required long TotalResponseTimeMs { get; init; }
    public required DateTimeOffset LastAttemptAtUtc { get; init; }
}

public sealed record StudySessionEnvelope
{
    public const int CurrentSchemaVersion = 1;

    public required Guid SessionId { get; init; }
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required Guid DeviceId { get; init; }
    public required StudyDeviceKind DeviceKind { get; init; }
    public required StudyMode Mode { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required StudyOutcome Outcome { get; init; }
    public required string BankVersion { get; init; }
    public required string BankSha256 { get; init; }
    public required string RulesProfile { get; init; }
    public required IReadOnlyList<long> OrderedQuestionIds { get; init; }
    public required IReadOnlyList<StudyAnswerEvent> Answers { get; init; }
    public IReadOnlyList<LegacyQuestionAggregate> LegacyAggregates { get; init; } = [];
    public required StudySessionSummary Summary { get; init; }
    public string PayloadSha256 { get; init; } = string.Empty;

    public StudySessionEnvelope WithComputedHash() => this with
    {
        PayloadSha256 = StudySessionCanonicalizer.ComputePayloadSha256(this)
    };

    public void Validate()
    {
        if (SessionId == Guid.Empty || DeviceId == Guid.Empty)
            throw new InvalidDataException("Session and device identifiers must not be empty.");
        if (SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported study-session schema version: {SchemaVersion}.");
        if (CompletedAtUtc < StartedAtUtc)
            throw new InvalidDataException("A study session cannot end before it starts.");
        if (string.IsNullOrWhiteSpace(BankVersion) || string.IsNullOrWhiteSpace(BankSha256) ||
            string.IsNullOrWhiteSpace(RulesProfile))
            throw new InvalidDataException("Bank and rules metadata are required.");
        if (OrderedQuestionIds.Count == 0 || OrderedQuestionIds.Distinct().Count() != OrderedQuestionIds.Count)
            throw new InvalidDataException("A study session must contain a non-empty ordered set of unique questions.");
        if (Answers.Select(answer => answer.SequenceNumber).Distinct().Count() != Answers.Count)
            throw new InvalidDataException("Answer sequence numbers must be unique.");
        if (Answers.Any(answer => answer.ResponseTimeMs < 0 || answer.CorrectAnswer < 1))
            throw new InvalidDataException("An answer contains invalid timing or answer metadata.");
        if (LegacyAggregates.Any(aggregate => aggregate.AttemptCount < 0 ||
                                                aggregate.CorrectCount < 0 ||
                                                aggregate.CorrectCount > aggregate.AttemptCount ||
                                                aggregate.TotalResponseTimeMs < 0))
            throw new InvalidDataException("A legacy training aggregate contains invalid values.");
        if (!string.IsNullOrWhiteSpace(PayloadSha256) && !StudySessionCanonicalizer.VerifyPayloadSha256(this))
            throw new InvalidDataException("Study-session payload hash does not match the canonical payload.");
    }
}

public sealed record ActiveSessionDraft
{
    public required Guid DraftId { get; init; }
    public required Guid DeviceId { get; init; }
    public required StudyMode Mode { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset SavedAtUtc { get; init; }
    public required string BankVersion { get; init; }
    public required string BankSha256 { get; init; }
    public required IReadOnlyList<long> OrderedQuestionIds { get; init; }
    public required IReadOnlyList<StudyAnswerEvent> ConfirmedAnswers { get; init; }
    public required int CurrentQuestionIndex { get; init; }
    public required long RemainingTimeMs { get; init; }
    public required StudyStage Stage { get; init; }
}
