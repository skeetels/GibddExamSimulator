namespace GibddExamSimulator.Models;

public sealed class ExamResult
{
    public required Guid AttemptId { get; init; }
    public required CandidateProfile Candidate { get; init; }
    public required ExamOutcome Outcome { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset EndedAtUtc { get; init; }
    public required int MainQuestionCount { get; init; }
    public required int MainCorrectCount { get; init; }
    public required int MainErrorCount { get; init; }
    public required int SupplementaryQuestionCount { get; init; }
    public required int SupplementaryCorrectCount { get; init; }
    public required int SupplementaryErrorCount { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public required string FailureReason { get; init; }
    public required IReadOnlyList<ExamQuestionState> IncorrectAnswers { get; init; }
}

