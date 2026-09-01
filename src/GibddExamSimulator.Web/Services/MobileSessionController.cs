using GibddExamSimulator.Application.StudySessions;
using GibddExamSimulator.ExamEngine;
using GibddExamSimulator.Models;

namespace GibddExamSimulator.Web.Services;

public sealed class MobileSessionController
{
    private readonly ExamEngine.ExamEngine? _exam;
    private readonly List<ExamQuestionState> _trainingStates = [];
    private readonly List<StudyAnswerEvent> _trainingEvents = [];
    private DateTimeOffset _questionShownAtUtc;
    private DateTimeOffset _trainingCompletedAtUtc;
    private int _trainingIndex;

    private MobileSessionController(
        Guid sessionId,
        Guid deviceId,
        StudyMode mode,
        DateTimeOffset startedAtUtc,
        ExamEngine.ExamEngine? exam)
    {
        SessionId = sessionId;
        DeviceId = deviceId;
        Mode = mode;
        StartedAtUtc = startedAtUtc;
        _exam = exam;
        _questionShownAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid SessionId { get; }
    public Guid DeviceId { get; }
    public StudyMode Mode { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public bool IsExam => Mode == StudyMode.Exam;
    public bool IsCompleted { get; private set; }
    public bool NeedsSupplementary => _exam?.Session?.Stage == ExamStage.SupplementaryBriefing;
    public int CurrentIndex => IsExam ? _exam?.Session?.CurrentQuestionIndex ?? 0 : _trainingIndex;
    public IReadOnlyList<ExamQuestionState> ActiveStates => IsExam
        ? _exam?.Session?.ActiveQuestions ?? []
        : _trainingStates;
    public ExamQuestionState? CurrentState =>
        CurrentIndex >= 0 && CurrentIndex < ActiveStates.Count ? ActiveStates[CurrentIndex] : null;
    public TimeSpan Remaining => IsExam ? _exam?.Session?.CurrentStageRemaining ?? TimeSpan.Zero : TimeSpan.Zero;
    public string StageCaption => IsExam
        ? _exam?.Session?.Stage == ExamStage.Supplementary ? "Дополнительный блок" : "Экзамен"
        : Mode switch
        {
            StudyMode.Ticket => "Билет",
            StudyMode.SmartTen => "Умные 10",
            StudyMode.MistakeReview => "Работа над ошибками",
            StudyMode.WeakTopics => "Слабые темы",
            StudyMode.Marathon => "Марафон",
            StudyMode.NoMistakeChallenge => "Без ошибок",
            _ => "Тренировка"
        };

    public static MobileSessionController CreateExam(
        Guid deviceId,
        CandidateProfile candidate,
        IReadOnlyList<Question> questions)
    {
        var engine = new ExamEngine.ExamEngine();
        var session = engine.Start(candidate, questions);
        return new MobileSessionController(session.Id, deviceId, StudyMode.Exam, session.StartedAtUtc!.Value, engine);
    }

    public static MobileSessionController CreateTraining(
        Guid deviceId,
        StudyMode mode,
        IReadOnlyList<Question> questions)
    {
        if (mode == StudyMode.Exam || questions.Count == 0)
            throw new ArgumentException("Training requires a non-empty non-exam question set.", nameof(questions));
        var started = DateTimeOffset.UtcNow;
        var controller = new MobileSessionController(Guid.NewGuid(), deviceId, mode, started, exam: null);
        controller._trainingStates.AddRange(questions.Select((question, index) => new ExamQuestionState
        {
            Question = question,
            Stage = ExamStage.Main,
            SequenceNumber = index + 1
        }));
        controller._trainingStates[0].Progress = QuestionProgress.Viewed;
        return controller;
    }

    public static MobileSessionController Restore(
        Guid deviceId,
        ActiveSessionDraft draft,
        IReadOnlyDictionary<long, Question> questionById)
    {
        if (draft.DeviceId != deviceId)
            throw new InvalidDataException("Черновик принадлежит другой установке приложения.");
        if (draft.Mode != StudyMode.Exam)
        {
            var questions = draft.OrderedQuestionIds.Select(id => questionById[id]).ToArray();
            var training = new MobileSessionController(draft.DraftId, deviceId, draft.Mode, draft.StartedAtUtc, exam: null);
            training._trainingStates.AddRange(questions.Select((question, index) => new ExamQuestionState
            {
                Question = question,
                Stage = ExamStage.Main,
                SequenceNumber = index + 1
            }));
            training._trainingIndex = Math.Clamp(draft.CurrentQuestionIndex, 0, training._trainingStates.Count - 1);
            foreach (var answer in draft.ConfirmedAnswers.OrderBy(answer => answer.SequenceNumber))
            {
                var state = training._trainingStates.First(item => item.Question.Id == answer.QuestionId);
                ApplyAnswer(state, answer);
                training._trainingEvents.Add(answer);
            }
            training._trainingStates[training._trainingIndex].Progress = QuestionProgress.Viewed;
            training._questionShownAtUtc = DateTimeOffset.UtcNow;
            return training;
        }

        var mainIds = draft.OrderedQuestionIds.Take(ExamRules.MainQuestionCount).ToArray();
        if (mainIds.Length != ExamRules.MainQuestionCount)
            throw new InvalidDataException("Черновик экзамена не содержит 20 основных вопросов.");
        var restored = new ExamSession
        {
            Id = draft.DraftId,
            Candidate = new CandidateProfile { FullName = "Кандидат", Category = "AB" },
            Stage = draft.Stage switch
            {
                StudyStage.Supplementary => ExamStage.Supplementary,
                StudyStage.SupplementaryBriefing => ExamStage.SupplementaryBriefing,
                _ => ExamStage.Main
            },
            Status = AttemptStatus.InProgress,
            Outcome = ExamOutcome.None,
            CreatedAtUtc = draft.StartedAtUtc,
            StartedAtUtc = draft.StartedAtUtc,
            CurrentQuestionIndex = draft.CurrentQuestionIndex,
            CurrentStageRemaining = TimeSpan.FromMilliseconds(Math.Max(0, draft.RemainingTimeMs))
        };
        restored.MainQuestions.AddRange(mainIds.Select((id, index) => new ExamQuestionState
        {
            Question = questionById[id],
            Stage = ExamStage.Main,
            SequenceNumber = index + 1
        }));
        var supplementaryIds = draft.OrderedQuestionIds.Skip(ExamRules.MainQuestionCount).ToArray();
        restored.SupplementaryQuestions.AddRange(supplementaryIds.Select((id, index) => new ExamQuestionState
        {
            Question = questionById[id],
            Stage = ExamStage.Supplementary,
            SequenceNumber = index + 1
        }));
        foreach (var answer in draft.ConfirmedAnswers)
        {
            var target = answer.Stage == StudyStage.Supplementary
                ? restored.SupplementaryQuestions.First(state => state.Question.Id == answer.QuestionId)
                : restored.MainQuestions.First(state => state.Question.Id == answer.QuestionId);
            ApplyAnswer(target, answer);
        }
        var duration = restored.Stage switch
        {
            ExamStage.Main => ExamRules.MainDuration,
            ExamStage.Supplementary => ExamRules.SupplementaryDurationPerError * restored.ErrorGroups.Count,
            _ => TimeSpan.Zero
        };
        var stageElapsed = duration - restored.CurrentStageRemaining;
        if (restored.Stage == ExamStage.Main)
            restored.MainElapsed = stageElapsed;
        else
            restored.SupplementaryElapsed = stageElapsed;
        var engine = new ExamEngine.ExamEngine();
        engine.Restore(restored, stageElapsed);
        return new MobileSessionController(restored.Id, deviceId, StudyMode.Exam, draft.StartedAtUtc, engine);
    }

    public bool NavigateTo(int index)
    {
        if (IsCompleted)
            return false;
        if (IsExam)
            return _exam?.NavigateTo(index) == true;
        if (index < 0 || index >= _trainingStates.Count)
            return false;
        _trainingIndex = index;
        _questionShownAtUtc = DateTimeOffset.UtcNow;
        if (_trainingStates[index].Progress == QuestionProgress.NotViewed)
            _trainingStates[index].Progress = QuestionProgress.Viewed;
        return true;
    }

    public bool SelectAnswer(int answer)
    {
        if (IsCompleted)
            return false;
        if (IsExam)
            return _exam?.SelectAnswer(answer) == true;
        var state = CurrentState;
        if (state is null || state.ConfirmedAnswer.HasValue || answer < 1 || answer > state.Question.Answers.Count)
            return false;
        state.PendingAnswer = answer;
        return true;
    }

    public bool ConfirmAnswer()
    {
        if (IsCompleted)
            return false;
        if (IsExam)
        {
            var accepted = _exam?.ConfirmAnswer() == ConfirmAnswerStatus.Accepted;
            IsCompleted = _exam?.Session?.Stage == ExamStage.Completed;
            return accepted;
        }

        var state = CurrentState;
        if (state?.PendingAnswer is null || state.ConfirmedAnswer.HasValue)
            return false;
        var now = DateTimeOffset.UtcNow;
        state.ConfirmedAnswer = state.PendingAnswer;
        state.IsCorrect = state.ConfirmedAnswer == state.Question.CorrectAnswer;
        state.Progress = QuestionProgress.Answered;
        state.AnsweredAtUtc = now;
        state.AnswerTime = now - _questionShownAtUtc;
        _trainingEvents.Add(ToAnswerEvent(state, _trainingEvents.Count + 1, training: true));
        if (Mode == StudyMode.NoMistakeChallenge && state.IsCorrect == false)
            FinishTraining(now);
        else if (_trainingStates.All(item => item.ConfirmedAnswer.HasValue))
            FinishTraining(now);
        else
            NavigateTo(FindNextUnansweredTrainingIndex());
        return true;
    }

    public void StartSupplementary(IReadOnlyList<Question> questions)
    {
        if (!IsExam || _exam is null)
            throw new InvalidOperationException("Дополнительный блок доступен только на экзамене.");
        _exam.StartSupplementary(questions);
    }

    public void Tick()
    {
        if (!IsExam || IsCompleted || _exam is null)
            return;
        _exam.Tick();
        IsCompleted = _exam.Session?.Stage == ExamStage.Completed;
    }

    public ActiveSessionDraft CreateDraft(string bankVersion, string bankSha256)
    {
        var states = AllStates();
        var answers = IsExam
            ? states.Where(state => state.ConfirmedAnswer.HasValue)
                .Select((state, index) => ToAnswerEvent(state, index + 1, training: false)).ToArray()
            : _trainingEvents.ToArray();
        return new ActiveSessionDraft
        {
            DraftId = SessionId,
            DeviceId = DeviceId,
            Mode = Mode,
            StartedAtUtc = StartedAtUtc,
            SavedAtUtc = DateTimeOffset.UtcNow,
            BankVersion = bankVersion,
            BankSha256 = bankSha256,
            OrderedQuestionIds = states.Select(state => state.Question.Id).ToArray(),
            ConfirmedAnswers = answers,
            CurrentQuestionIndex = CurrentIndex,
            RemainingTimeMs = IsExam ? Math.Max(0, (long)Remaining.TotalMilliseconds) : 0,
            Stage = IsExam
                ? _exam?.Session?.Stage switch
                {
                    ExamStage.Supplementary => StudyStage.Supplementary,
                    ExamStage.SupplementaryBriefing => StudyStage.SupplementaryBriefing,
                    _ => StudyStage.Main
                }
                : StudyStage.Training
        };
    }

    public StudySessionEnvelope BuildEnvelope(string bankVersion, string bankSha256, string rulesProfile)
    {
        if (!IsCompleted)
            throw new InvalidOperationException("Нельзя сохранить незавершённую сессию как результат.");
        if (IsExam && _exam?.Session is not null)
        {
            return ExamSessionEnvelopeFactory.Create(
                _exam.Session,
                DeviceId,
                StudyDeviceKind.MobilePwa,
                bankVersion,
                bankSha256,
                rulesProfile);
        }

        var completedAt = _trainingCompletedAtUtc == default ? DateTimeOffset.UtcNow : _trainingCompletedAtUtc;
        var answers = _trainingEvents.ToArray();
        var longest = 0;
        var current = 0;
        foreach (var answer in answers)
        {
            current = answer.IsCorrect ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }
        return new StudySessionEnvelope
        {
            SessionId = SessionId,
            DeviceId = DeviceId,
            DeviceKind = StudyDeviceKind.MobilePwa,
            Mode = Mode,
            StartedAtUtc = StartedAtUtc,
            CompletedAtUtc = completedAt,
            Outcome = StudyOutcome.Completed,
            BankVersion = bankVersion,
            BankSha256 = bankSha256,
            RulesProfile = rulesProfile,
            OrderedQuestionIds = _trainingStates.Select(state => state.Question.Id).ToArray(),
            Answers = answers,
            Summary = new StudySessionSummary
            {
                QuestionCount = _trainingStates.Count,
                AnsweredCount = answers.Length,
                CorrectCount = answers.Count(answer => answer.IsCorrect),
                ErrorCount = answers.Count(answer => !answer.IsCorrect),
                ElapsedMs = Math.Max(0, (long)(completedAt - StartedAtUtc).TotalMilliseconds),
                LongestCorrectStreak = longest
            }
        }.WithComputedHash();
    }

    public IReadOnlyCollection<int> ExamErrorGroups => _exam?.Session?.ErrorGroups ?? [];
    public IReadOnlyCollection<long> ExamMainQuestionIds =>
        _exam?.Session?.MainQuestions.Select(state => state.Question.Id).ToArray() ?? [];

    private IReadOnlyList<ExamQuestionState> AllStates()
    {
        if (!IsExam)
            return _trainingStates;
        var session = _exam?.Session;
        return session is null ? [] : session.MainQuestions.Concat(session.SupplementaryQuestions).ToArray();
    }

    private int FindNextUnansweredTrainingIndex()
    {
        for (var offset = 1; offset <= _trainingStates.Count; offset++)
        {
            var index = (_trainingIndex + offset) % _trainingStates.Count;
            if (!_trainingStates[index].ConfirmedAnswer.HasValue)
                return index;
        }
        return _trainingIndex;
    }

    private void FinishTraining(DateTimeOffset completedAtUtc)
    {
        IsCompleted = true;
        _trainingCompletedAtUtc = completedAtUtc;
    }

    private static StudyAnswerEvent ToAnswerEvent(ExamQuestionState state, int sequenceNumber, bool training) => new()
    {
        SequenceNumber = sequenceNumber,
        QuestionId = state.Question.Id,
        TicketNumber = state.Question.TicketNumber,
        QuestionNumber = state.Question.QuestionNumber,
        GroupId = state.Question.GroupId,
        ThematicBlockId = state.Question.ThematicBlockId,
        Stage = training
            ? StudyStage.Training
            : state.Stage == ExamStage.Supplementary ? StudyStage.Supplementary : StudyStage.Main,
        SelectedAnswer = state.ConfirmedAnswer,
        CorrectAnswer = state.Question.CorrectAnswer,
        IsCorrect = state.IsCorrect == true,
        ResponseTimeMs = Math.Max(0, (long)(state.AnswerTime ?? TimeSpan.Zero).TotalMilliseconds),
        AnsweredAtUtc = state.AnsweredAtUtc
    };

    private static void ApplyAnswer(ExamQuestionState state, StudyAnswerEvent answer)
    {
        state.PendingAnswer = answer.SelectedAnswer;
        state.ConfirmedAnswer = answer.SelectedAnswer;
        state.IsCorrect = answer.IsCorrect;
        state.Progress = QuestionProgress.Answered;
        state.AnswerTime = TimeSpan.FromMilliseconds(Math.Max(0, answer.ResponseTimeMs));
        state.AnsweredAtUtc = answer.AnsweredAtUtc;
    }
}
