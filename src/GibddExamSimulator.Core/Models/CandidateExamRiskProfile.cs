using System.Collections.ObjectModel;

namespace GibddExamSimulator.Models;

/// <summary>
/// Historical exam risk for a single question. Training answers are deliberately
/// not included: this profile is built only from completed exam attempts.
/// </summary>
public sealed record QuestionRisk(
    long QuestionId,
    int CandidateAttemptCount,
    int CandidateErrorCount,
    int GlobalAttemptCount,
    int GlobalErrorCount,
    bool WasWrongOnMostRecentCompletedAttempt)
{
    public double LearningRiskScore { get; init; }

    public double CandidateErrorRate => CandidateAttemptCount == 0
        ? 0
        : CandidateErrorCount / (double)CandidateAttemptCount;

    public double GlobalErrorRate => GlobalAttemptCount == 0
        ? 0
        : GlobalErrorCount / (double)GlobalAttemptCount;

    public bool HasEvidence => LearningRiskScore > 0 ||
                               WasWrongOnMostRecentCompletedAttempt ||
                               CandidateAttemptCount > 0 ||
                               GlobalAttemptCount > 0;

    public static QuestionRisk Empty(long questionId) => new(questionId, 0, 0, 0, 0, false);
}

/// <summary>
/// Immutable per-candidate profile used while selecting an adaptive exam form.
/// Candidate identity follows the fields already persisted with exam attempts.
/// </summary>
public sealed class CandidateExamRiskProfile
{
    private readonly IReadOnlyDictionary<long, QuestionRisk> _questions;

    public CandidateExamRiskProfile(
        string candidateName,
        DateOnly birthDate,
        string category,
        IEnumerable<QuestionRisk> questions)
    {
        ArgumentNullException.ThrowIfNull(candidateName);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(questions);

        CandidateName = candidateName.Trim();
        BirthDate = birthDate;
        Category = category;
        _questions = new ReadOnlyDictionary<long, QuestionRisk>(
            questions.ToDictionary(risk => risk.QuestionId));
    }

    public string CandidateName { get; }
    public DateOnly BirthDate { get; }
    public string Category { get; }
    public IReadOnlyDictionary<long, QuestionRisk> Questions => _questions;
    public bool HasEvidence => _questions.Values.Any(risk => risk.HasEvidence);

    public QuestionRisk GetRisk(long questionId) =>
        _questions.TryGetValue(questionId, out var risk) ? risk : QuestionRisk.Empty(questionId);
}
