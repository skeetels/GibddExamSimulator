using System.Collections.ObjectModel;
using GibddExamSimulator.Models;

namespace GibddExamSimulator.Application.Learning;

public sealed record LearningQuestionProfile
{
    public required long QuestionId { get; init; }
    public required int ExposureCount { get; init; }
    public required int ErrorCount { get; init; }
    public required double ErrorRate { get; init; }
    public DateTimeOffset? LastAnsweredAtUtc { get; init; }
    public DateTimeOffset? LastErrorAtUtc { get; init; }
    public required bool? LastAnswerWasCorrect { get; init; }
    public required int CorrectStreakAfterError { get; init; }
    public required double AverageResponseTimeMs { get; init; }
    public required double MedianResponseTimeMs { get; init; }
    public required int MasteryLevel { get; init; }
    public DateTimeOffset? DueAtUtc { get; init; }
    public required double RiskScore { get; init; }
    public required int ExamErrorCount { get; init; }
    public bool IsDue(DateTimeOffset nowUtc) => DueAtUtc is not null && DueAtUtc <= nowUtc;
}

public sealed class LearningProfile
{
    private readonly IReadOnlyDictionary<long, LearningQuestionProfile> _questions;

    public LearningProfile(DateTimeOffset calculatedAtUtc, IEnumerable<LearningQuestionProfile> questions)
    {
        CalculatedAtUtc = calculatedAtUtc;
        _questions = new ReadOnlyDictionary<long, LearningQuestionProfile>(
            questions.ToDictionary(question => question.QuestionId));
    }

    public DateTimeOffset CalculatedAtUtc { get; }
    public IReadOnlyDictionary<long, LearningQuestionProfile> Questions => _questions;
    public LearningQuestionProfile? GetQuestion(long questionId) =>
        _questions.TryGetValue(questionId, out var profile) ? profile : null;

    public CandidateExamRiskProfile ToExamRiskProfile(string localCandidateName = "", DateOnly? localBirthDate = null)
    {
        var risks = _questions.Values.Select(question => new QuestionRisk(
            question.QuestionId,
            question.ExposureCount,
            question.ErrorCount,
            question.ExposureCount,
            question.ErrorCount,
            question.LastErrorAtUtc is not null &&
            CalculatedAtUtc - question.LastErrorAtUtc.Value <= TimeSpan.FromDays(14))
        {
            LearningRiskScore = question.RiskScore
        });
        return new CandidateExamRiskProfile(
            localCandidateName,
            localBirthDate ?? new DateOnly(2000, 1, 1),
            "AB",
            risks);
    }
}

public sealed class StudySessionIntegrityException : InvalidOperationException
{
    public StudySessionIntegrityException(Guid sessionId)
        : base($"Session {sessionId:D} has conflicting payload hashes.")
    {
        SessionId = sessionId;
    }

    public Guid SessionId { get; }
}
