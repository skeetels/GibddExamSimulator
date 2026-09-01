namespace GibddExamSimulator.Models;

public sealed class ExamSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required CandidateProfile Candidate { get; init; }
    public AttemptMode Mode { get; init; } = AttemptMode.Exam;
    public ExamStage Stage { get; set; } = ExamStage.NotStarted;
    public AttemptStatus Status { get; set; } = AttemptStatus.Ready;
    public ExamOutcome Outcome { get; set; } = ExamOutcome.None;
    public List<ExamQuestionState> MainQuestions { get; } = [];
    public List<ExamQuestionState> SupplementaryQuestions { get; } = [];
    public int CurrentQuestionIndex { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public TimeSpan MainElapsed { get; set; }
    public TimeSpan SupplementaryElapsed { get; set; }
    public TimeSpan CurrentStageRemaining { get; set; }
    public string? FailureReason { get; set; }
    public string? AssignmentId { get; set; }

    public IReadOnlyList<ExamQuestionState> ActiveQuestions =>
        Stage == ExamStage.Supplementary ? SupplementaryQuestions : MainQuestions;

    public ExamQuestionState? CurrentQuestion =>
        CurrentQuestionIndex >= 0 && CurrentQuestionIndex < ActiveQuestions.Count
            ? ActiveQuestions[CurrentQuestionIndex]
            : null;

    public int MainErrors => MainQuestions.Count(x => x.IsCorrect == false);
    public int SupplementaryErrors => SupplementaryQuestions.Count(x => x.IsCorrect == false);
    public int ConfirmedMainAnswers => MainQuestions.Count(x => x.ConfirmedAnswer.HasValue);
    public int ConfirmedSupplementaryAnswers => SupplementaryQuestions.Count(x => x.ConfirmedAnswer.HasValue);
    public int ConfirmedAnswers => MainQuestions.Count(x => x.ConfirmedAnswer.HasValue) +
                                   SupplementaryQuestions.Count(x => x.ConfirmedAnswer.HasValue);
    public IReadOnlyList<int> ErrorGroups => MainQuestions
        .Where(x => x.IsCorrect == false)
        .Select(x => x.Question.GroupId)
        .Distinct()
        .OrderBy(x => x)
        .ToArray();
}
