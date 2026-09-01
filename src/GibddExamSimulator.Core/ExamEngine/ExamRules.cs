namespace GibddExamSimulator.ExamEngine;

public static class ExamRules
{
    public const int MainQuestionCount = 20;
    public const int QuestionsPerThematicBlock = 5;
    public const int ThematicBlockCount = 4;
    public const int SupplementaryQuestionsPerError = 5;
    public static readonly TimeSpan MainDuration = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan SupplementaryDurationPerError = TimeSpan.FromMinutes(5);
}

