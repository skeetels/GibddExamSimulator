namespace GibddExamSimulator.Models;

public enum AttemptMode
{
    Exam,
    Training
}

public enum ExamStage
{
    NotStarted,
    Main,
    SupplementaryBriefing,
    Supplementary,
    Completed
}

public enum AttemptStatus
{
    Ready,
    InProgress,
    Passed,
    Failed,
    Interrupted
}

public enum ExamOutcome
{
    None,
    Passed,
    Failed,
    Interrupted
}

public enum QuestionProgress
{
    NotViewed,
    Viewed,
    Answered
}

public enum TrainingSelectionMode
{
    Random,
    Ticket,
    ThematicBlock,
    Mistakes
}

public enum ConfirmAnswerStatus
{
    Accepted,
    NoAnswerSelected,
    AlreadyConfirmed,
    ExamNotRunning
}

