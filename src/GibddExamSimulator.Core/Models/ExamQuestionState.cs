namespace GibddExamSimulator.Models;

public sealed class ExamQuestionState
{
    public required Question Question { get; init; }
    public required ExamStage Stage { get; init; }
    public required int SequenceNumber { get; init; }
    public QuestionProgress Progress { get; set; }
    public int? PendingAnswer { get; set; }
    public int? ConfirmedAnswer { get; set; }
    public bool? IsCorrect { get; set; }
    public TimeSpan? AnswerTime { get; set; }
    public DateTimeOffset? AnsweredAtUtc { get; set; }
}

