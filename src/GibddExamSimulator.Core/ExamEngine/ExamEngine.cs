using GibddExamSimulator.Models;

namespace GibddExamSimulator.ExamEngine;

public sealed class ExamEngine
{
    private readonly IExamTimeSource _time;
    private TimeSpan _stageStartedAt;
    private TimeSpan _questionShownAt;

    public ExamEngine(IExamTimeSource? timeSource = null)
    {
        _time = timeSource ?? new StopwatchExamTimeSource();
    }

    public ExamSession? Session { get; private set; }

    public ExamSession Start(CandidateProfile candidate, IReadOnlyList<Question> mainQuestions, string? assignmentId = null)
    {
        if (mainQuestions.Count != ExamRules.MainQuestionCount)
            throw new ArgumentException($"Основной экзамен должен содержать {ExamRules.MainQuestionCount} вопросов.", nameof(mainQuestions));
        ValidateMainBlockStructure(mainQuestions);

        var session = new ExamSession
        {
            Candidate = candidate,
            Stage = ExamStage.Main,
            Status = AttemptStatus.InProgress,
            CreatedAtUtc = _time.UtcNow,
            StartedAtUtc = _time.UtcNow,
            CurrentQuestionIndex = 0,
            AssignmentId = assignmentId
        };
        session.MainQuestions.AddRange(mainQuestions.Select((question, index) => new ExamQuestionState
        {
            Question = question,
            Stage = ExamStage.Main,
            SequenceNumber = index + 1
        }));

        Session = session;
        _stageStartedAt = _time.Elapsed;
        _questionShownAt = _time.Elapsed;
        session.CurrentStageRemaining = ExamRules.MainDuration;
        MarkCurrentViewed();
        return session;
    }

    public ExamSession Restore(ExamSession session, TimeSpan currentStageElapsed)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Status != AttemptStatus.InProgress ||
            session.Stage is not (ExamStage.Main or ExamStage.SupplementaryBriefing or ExamStage.Supplementary))
            throw new ArgumentException("Only an in-progress exam can be restored.", nameof(session));
        if (session.MainQuestions.Count != ExamRules.MainQuestionCount)
            throw new ArgumentException("A restored exam must contain 20 main questions.", nameof(session));
        ValidateMainBlockStructure(session.MainQuestions.Select(state => state.Question).ToArray());
        if (session.Stage == ExamStage.Supplementary)
        {
            var expected = session.ErrorGroups.Count * ExamRules.SupplementaryQuestionsPerError;
            if (expected == 0 || session.SupplementaryQuestions.Count != expected)
                throw new ArgumentException("A restored supplementary exam has an invalid question set.", nameof(session));
        }
        if (session.CurrentQuestionIndex < 0 || session.CurrentQuestionIndex >= session.ActiveQuestions.Count)
            throw new ArgumentException("A restored exam has an invalid current question index.", nameof(session));

        Session = session;
        var safeElapsed = currentStageElapsed < TimeSpan.Zero ? TimeSpan.Zero : currentStageElapsed;
        _stageStartedAt = _time.Elapsed - safeElapsed;
        _questionShownAt = _time.Elapsed;
        if (session.Stage is ExamStage.Main or ExamStage.Supplementary)
            Tick();
        MarkCurrentViewed();
        return session;
    }

    public bool NavigateTo(int zeroBasedIndex)
    {
        var session = RequireSession();
        Tick();
        if (session.Stage is not (ExamStage.Main or ExamStage.Supplementary))
            return false;
        if (zeroBasedIndex < 0 || zeroBasedIndex >= session.ActiveQuestions.Count)
            return false;

        session.CurrentQuestionIndex = zeroBasedIndex;
        _questionShownAt = _time.Elapsed;
        MarkCurrentViewed();
        return true;
    }

    public bool NavigateRelative(int offset)
    {
        var session = RequireSession();
        var target = session.CurrentQuestionIndex + offset;
        return NavigateTo(target);
    }

    public bool SelectAnswer(int oneBasedAnswer)
    {
        var session = RequireSession();
        Tick();
        var state = session.CurrentQuestion;
        if (state is null || session.Stage is not (ExamStage.Main or ExamStage.Supplementary))
            return false;
        if (state.ConfirmedAnswer.HasValue || oneBasedAnswer < 1 || oneBasedAnswer > state.Question.Answers.Count)
            return false;

        state.PendingAnswer = oneBasedAnswer;
        if (state.Progress == QuestionProgress.NotViewed)
            state.Progress = QuestionProgress.Viewed;
        return true;
    }

    public ConfirmAnswerStatus ConfirmAnswer()
    {
        var session = RequireSession();
        Tick();
        if (session.Stage is not (ExamStage.Main or ExamStage.Supplementary) || session.Status != AttemptStatus.InProgress)
            return ConfirmAnswerStatus.ExamNotRunning;

        var state = session.CurrentQuestion;
        if (state is null)
            return ConfirmAnswerStatus.ExamNotRunning;
        if (state.ConfirmedAnswer.HasValue)
            return ConfirmAnswerStatus.AlreadyConfirmed;
        if (!state.PendingAnswer.HasValue)
            return ConfirmAnswerStatus.NoAnswerSelected;

        state.ConfirmedAnswer = state.PendingAnswer;
        state.IsCorrect = state.ConfirmedAnswer.Value == state.Question.CorrectAnswer;
        state.Progress = QuestionProgress.Answered;
        state.AnswerTime = _time.Elapsed - _questionShownAt;
        state.AnsweredAtUtc = _time.UtcNow;

        if (session.Stage == ExamStage.Supplementary && state.IsCorrect == false)
        {
            Fail("Допущена ошибка при ответе на дополнительный вопрос.");
            return ConfirmAnswerStatus.Accepted;
        }

        if (session.Stage == ExamStage.Main && state.IsCorrect == false)
        {
            if (session.MainErrors >= 3)
            {
                Fail("Допущено три ошибки в основном блоке.");
                return ConfirmAnswerStatus.Accepted;
            }

            var errorsInCurrentBlock = session.MainQuestions.Count(x =>
                x.IsCorrect == false && x.Question.GroupId == state.Question.GroupId);
            if (errorsInCurrentBlock >= 2)
            {
                Fail("Допущено две ошибки в одном тематическом блоке.");
                return ConfirmAnswerStatus.Accepted;
            }
        }

        if (session.ActiveQuestions.All(x => x.ConfirmedAnswer.HasValue))
        {
            if (session.Stage == ExamStage.Main)
                CompleteMainStage();
            else
                Pass();
        }
        else
        {
            NavigateTo(FindNextUnansweredIndex(session));
        }

        return ConfirmAnswerStatus.Accepted;
    }

    public void StartSupplementary(IReadOnlyList<Question> questions)
    {
        var session = RequireSession();
        if (session.Stage != ExamStage.SupplementaryBriefing)
            throw new InvalidOperationException("Дополнительный блок сейчас недоступен.");

        var requiredGroups = session.ErrorGroups;
        var expectedCount = requiredGroups.Count * ExamRules.SupplementaryQuestionsPerError;
        if (questions.Count != expectedCount)
            throw new ArgumentException($"Требуется {expectedCount} дополнительных вопросов.", nameof(questions));

        var excludedIds = session.MainQuestions.Select(x => x.Question.Id).ToHashSet();
        if (questions.Any(q => excludedIds.Contains(q.Id)))
            throw new ArgumentException("Дополнительный блок не должен повторять вопросы основного экзамена.", nameof(questions));

        foreach (var groupNumber in requiredGroups)
        {
            if (questions.Count(q => q.GroupId == groupNumber) != ExamRules.SupplementaryQuestionsPerError)
                throw new ArgumentException($"Для группы {groupNumber} требуется ровно 5 дополнительных вопросов.", nameof(questions));
        }

        session.SupplementaryQuestions.Clear();
        session.SupplementaryQuestions.AddRange(questions.Select((question, index) => new ExamQuestionState
        {
            Question = question,
            Stage = ExamStage.Supplementary,
            SequenceNumber = index + 1
        }));
        session.Stage = ExamStage.Supplementary;
        session.CurrentQuestionIndex = 0;
        _stageStartedAt = _time.Elapsed;
        _questionShownAt = _time.Elapsed;
        session.CurrentStageRemaining = ExamRules.SupplementaryDurationPerError * requiredGroups.Count;
        MarkCurrentViewed();
    }

    public bool Tick()
    {
        var session = RequireSession();
        if (session.Status != AttemptStatus.InProgress || session.Stage is not (ExamStage.Main or ExamStage.Supplementary))
            return false;

        var duration = session.Stage == ExamStage.Main
            ? ExamRules.MainDuration
            : ExamRules.SupplementaryDurationPerError * session.ErrorGroups.Count;
        var elapsed = _time.Elapsed - _stageStartedAt;
        session.CurrentStageRemaining = duration - elapsed;

        if (session.Stage == ExamStage.Main)
            session.MainElapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        else
            session.SupplementaryElapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;

        if (session.CurrentStageRemaining > TimeSpan.Zero)
            return false;

        session.CurrentStageRemaining = TimeSpan.Zero;
        if (session.Stage == ExamStage.Main)
        {
            foreach (var unanswered in session.MainQuestions.Where(x => !x.ConfirmedAnswer.HasValue))
            {
                unanswered.IsCorrect = false;
                unanswered.Progress = QuestionProgress.Answered;
                unanswered.AnswerTime = elapsed;
            }
            CompleteMainStage(deadlineReached: true);
        }
        else
        {
            foreach (var unanswered in session.SupplementaryQuestions.Where(x => !x.ConfirmedAnswer.HasValue))
            {
                unanswered.IsCorrect = false;
                unanswered.Progress = QuestionProgress.Answered;
                unanswered.AnswerTime = elapsed;
            }
            Fail("Истекло время дополнительного блока. Неотвеченные вопросы засчитаны как ошибки.");
        }
        return true;
    }

    public void Interrupt(string reason = "Попытка прервана до завершения экзамена.")
    {
        var session = RequireSession();
        if (session.Status != AttemptStatus.InProgress)
            return;

        UpdateElapsed();
        session.Status = AttemptStatus.Interrupted;
        session.Outcome = ExamOutcome.Interrupted;
        session.Stage = ExamStage.Completed;
        session.EndedAtUtc = _time.UtcNow;
        session.FailureReason = reason;
    }

    public ExamResult BuildResult()
    {
        var session = RequireSession();
        if (session.Stage != ExamStage.Completed || session.Outcome == ExamOutcome.None)
            throw new InvalidOperationException("Экзамен ещё не завершён.");

        var all = session.MainQuestions.Concat(session.SupplementaryQuestions).ToArray();
        var start = session.StartedAtUtc ?? session.CreatedAtUtc;
        var end = session.EndedAtUtc ?? _time.UtcNow;
        return new ExamResult
        {
            AttemptId = session.Id,
            Candidate = session.Candidate,
            Outcome = session.Outcome,
            StartedAtUtc = start,
            EndedAtUtc = end,
            MainQuestionCount = session.MainQuestions.Count,
            MainCorrectCount = session.MainQuestions.Count(x => x.IsCorrect == true),
            MainErrorCount = session.MainQuestions.Count(x => x.IsCorrect == false),
            SupplementaryQuestionCount = session.SupplementaryQuestions.Count,
            SupplementaryCorrectCount = session.SupplementaryQuestions.Count(x => x.IsCorrect == true),
            SupplementaryErrorCount = session.SupplementaryQuestions.Count(x => x.IsCorrect == false),
            Elapsed = session.MainElapsed + session.SupplementaryElapsed,
            FailureReason = session.FailureReason ?? string.Empty,
            IncorrectAnswers = all.Where(x => x.IsCorrect == false).ToArray()
        };
    }

    private void CompleteMainStage(bool deadlineReached = false)
    {
        var session = RequireSession();
        UpdateElapsed();
        if (session.MainErrors == 0)
        {
            Pass();
            return;
        }

        if (session.MainErrors is 1 or 2 && session.ErrorGroups.Count == session.MainErrors)
        {
            session.Stage = ExamStage.SupplementaryBriefing;
            session.CurrentStageRemaining = TimeSpan.Zero;
            return;
        }

        if (deadlineReached)
        {
            var reason = session.MainErrors >= 3
                ? "Истекло время основного экзамена. Неотвеченные вопросы дали три или более ошибок."
                : "Истекло время основного экзамена. Две ошибки допущены в одном тематическом блоке.";
            Fail(reason);
        }
        else
        {
            Fail("Превышено допустимое количество ошибок.");
        }
    }

    private void Pass()
    {
        var session = RequireSession();
        UpdateElapsed();
        session.Status = AttemptStatus.Passed;
        session.Outcome = ExamOutcome.Passed;
        session.Stage = ExamStage.Completed;
        session.EndedAtUtc = _time.UtcNow;
        session.FailureReason = string.Empty;
    }

    private void Fail(string reason)
    {
        var session = RequireSession();
        UpdateElapsed();
        session.Status = AttemptStatus.Failed;
        session.Outcome = ExamOutcome.Failed;
        session.Stage = ExamStage.Completed;
        session.EndedAtUtc = _time.UtcNow;
        session.FailureReason = reason;
    }

    private void UpdateElapsed()
    {
        var session = RequireSession();
        var elapsed = _time.Elapsed - _stageStartedAt;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        if (session.Stage == ExamStage.Main)
            session.MainElapsed = elapsed;
        else if (session.Stage == ExamStage.Supplementary)
            session.SupplementaryElapsed = elapsed;
    }

    private void MarkCurrentViewed()
    {
        var state = RequireSession().CurrentQuestion;
        if (state is not null && state.Progress == QuestionProgress.NotViewed)
            state.Progress = QuestionProgress.Viewed;
    }

    private static int FindNextUnansweredIndex(ExamSession session)
    {
        var questions = session.ActiveQuestions;
        for (var offset = 1; offset <= questions.Count; offset++)
        {
            var index = (session.CurrentQuestionIndex + offset) % questions.Count;
            if (!questions[index].ConfirmedAnswer.HasValue)
                return index;
        }
        return session.CurrentQuestionIndex;
    }

    private static void ValidateMainBlockStructure(IReadOnlyList<Question> questions)
    {
        for (var groupNumber = 1; groupNumber <= ExamRules.ThematicBlockCount; groupNumber++)
        {
            var groupQuestions = questions.Where(q => q.GroupId == groupNumber).ToArray();
            if (groupQuestions.Length != ExamRules.QuestionsPerThematicBlock)
                throw new ArgumentException($"Экзамен должен содержать ровно один блок из пяти вопросов группы {groupNumber}.", nameof(questions));
            if (groupQuestions.Select(q => q.ThematicBlockId).Distinct().Count() != 1)
                throw new ArgumentException($"Пять вопросов группы {groupNumber} должны принадлежать одному тематическому блоку.", nameof(questions));
        }
    }

    private ExamSession RequireSession() =>
        Session ?? throw new InvalidOperationException("Экзамен не создан.");
}
