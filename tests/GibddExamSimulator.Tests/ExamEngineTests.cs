using GibddExamSimulator.ExamEngine;
using GibddExamSimulator.Models;
using Engine = GibddExamSimulator.ExamEngine.ExamEngine;

namespace GibddExamSimulator.Tests;

public sealed class ExamEngineTests
{
    [Fact]
    public void TwentyCorrectAnswers_PassesWithoutSupplementaryBlock()
    {
        var (engine, _, _) = CreateEngine();
        AnswerAllRemaining(engine, correct: true);

        Assert.Equal(ExamOutcome.Passed, engine.Session!.Outcome);
        Assert.Equal(0, engine.Session.MainErrors);
        Assert.Empty(engine.Session.SupplementaryQuestions);
    }

    [Fact]
    public void OneMainError_GrantsFiveSupplementaryQuestions_AndAllCorrectPasses()
    {
        var (engine, bank, _) = CreateEngine();
        AnswerAt(engine, 0, correct: false);
        AnswerAllRemaining(engine, correct: true);

        Assert.Equal(ExamStage.SupplementaryBriefing, engine.Session!.Stage);
        engine.StartSupplementary(QuestionFactory.Supplementary(bank, [1]));
        Assert.Equal(5, engine.Session.SupplementaryQuestions.Count);
        Assert.Equal(TimeSpan.FromMinutes(5), engine.Session.CurrentStageRemaining);

        AnswerAllRemaining(engine, correct: true);
        Assert.Equal(ExamOutcome.Passed, engine.Session.Outcome);
    }

    [Fact]
    public void TwoErrorsInDifferentGroups_GrantTenSupplementaryQuestions()
    {
        var (engine, bank, _) = CreateEngine();
        AnswerAt(engine, 0, correct: false);
        AnswerAt(engine, 5, correct: false);
        AnswerAllRemaining(engine, correct: true);

        Assert.Equal(ExamStage.SupplementaryBriefing, engine.Session!.Stage);
        engine.StartSupplementary(QuestionFactory.Supplementary(bank, [1, 2]));
        Assert.Equal(10, engine.Session.SupplementaryQuestions.Count);
        Assert.Equal(TimeSpan.FromMinutes(10), engine.Session.CurrentStageRemaining);
    }

    [Fact]
    public void TenCorrectSupplementaryAnswers_PassExam()
    {
        var (engine, bank, _) = CreateEngine();
        AnswerAt(engine, 0, correct: false);
        AnswerAt(engine, 5, correct: false);
        AnswerAllRemaining(engine, correct: true);
        engine.StartSupplementary(QuestionFactory.Supplementary(bank, [1, 2]));

        AnswerAllRemaining(engine, correct: true);

        Assert.Equal(ExamOutcome.Passed, engine.Session!.Outcome);
        var result = engine.BuildResult();
        Assert.Equal(10, result.SupplementaryQuestionCount);
        Assert.Equal(10, result.SupplementaryCorrectCount);
    }

    [Fact]
    public void TwoErrorsInSameBlock_FailImmediately()
    {
        var (engine, _, _) = CreateEngine();
        AnswerAt(engine, 0, correct: false);
        AnswerAt(engine, 1, correct: false);

        Assert.Equal(ExamOutcome.Failed, engine.Session!.Outcome);
        Assert.Contains("одном тематическом блоке", engine.Session.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, engine.Session.ConfirmedMainAnswers);
    }

    [Fact]
    public void ThirdErrorAcrossGroups_FailsImmediately()
    {
        var (engine, _, _) = CreateEngine();
        AnswerAt(engine, 0, correct: false);
        AnswerAt(engine, 5, correct: false);
        AnswerAt(engine, 10, correct: false);

        Assert.Equal(ExamOutcome.Failed, engine.Session!.Outcome);
        Assert.Contains("три ошибки", engine.Session.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnySupplementaryError_FailsImmediately()
    {
        var (engine, bank, _) = CreateEngine();
        AnswerAt(engine, 0, correct: false);
        AnswerAllRemaining(engine, correct: true);
        engine.StartSupplementary(QuestionFactory.Supplementary(bank, [1]));

        AnswerAt(engine, 0, correct: false);
        Assert.Equal(ExamOutcome.Failed, engine.Session!.Outcome);
        Assert.Contains("дополнительный", engine.Session.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnswerNeedsSeparateSelectionAndConfirmation_AndCannotChangeAfterwards()
    {
        var (engine, _, _) = CreateEngine();
        Assert.Equal(ConfirmAnswerStatus.NoAnswerSelected, engine.ConfirmAnswer());
        Assert.True(engine.SelectAnswer(1));
        Assert.Null(engine.Session!.CurrentQuestion!.ConfirmedAnswer);
        Assert.Equal(ConfirmAnswerStatus.Accepted, engine.ConfirmAnswer());
        engine.NavigateTo(0);
        Assert.False(engine.SelectAnswer(2));
        Assert.Equal(ConfirmAnswerStatus.AlreadyConfirmed, engine.ConfirmAnswer());
    }

    [Fact]
    public void MainDeadline_WithOneUnanswered_GrantsSupplementaryBlock()
    {
        var (engine, _, time) = CreateEngine();
        AnswerEveryQuestionExcept(engine, [0]);
        time.Advance(TimeSpan.FromMinutes(20));

        Assert.True(engine.Tick());
        Assert.Equal(ExamStage.SupplementaryBriefing, engine.Session!.Stage);
        Assert.Equal(1, engine.Session.MainErrors);
        Assert.Null(engine.Session.MainQuestions[0].ConfirmedAnswer);
    }

    [Fact]
    public void MainDeadline_WithTwoUnansweredInDifferentGroups_GrantsTenQuestions()
    {
        var (engine, _, time) = CreateEngine();
        AnswerEveryQuestionExcept(engine, [0, 5]);
        time.Advance(TimeSpan.FromMinutes(20));

        engine.Tick();
        Assert.Equal(ExamStage.SupplementaryBriefing, engine.Session!.Stage);
        Assert.Equal([1, 2], engine.Session.ErrorGroups);
    }

    [Fact]
    public void MainDeadline_WithTwoUnansweredInSameBlock_Fails()
    {
        var (engine, _, time) = CreateEngine();
        AnswerEveryQuestionExcept(engine, [0, 1]);
        time.Advance(TimeSpan.FromMinutes(20));

        engine.Tick();
        Assert.Equal(ExamOutcome.Failed, engine.Session!.Outcome);
        Assert.Contains("Истекло время", engine.Session.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DraftSelectionAtDeadline_IsStillAnUnansweredError()
    {
        var (engine, _, time) = CreateEngine();
        Assert.True(engine.SelectAnswer(1));
        AnswerEveryQuestionExcept(engine, [0]);
        time.Advance(TimeSpan.FromMinutes(20));

        engine.Tick();

        Assert.Equal(ExamStage.SupplementaryBriefing, engine.Session!.Stage);
        Assert.Null(engine.Session.MainQuestions[0].ConfirmedAnswer);
        Assert.False(engine.Session.MainQuestions[0].IsCorrect);
    }

    [Fact]
    public void MainDeadline_WithConfirmedErrorAndUnansweredInDifferentGroup_GrantsTenQuestions()
    {
        var (engine, _, time) = CreateEngine();
        AnswerAt(engine, 0, correct: false);
        AnswerEveryQuestionExcept(engine, [0, 5]);
        time.Advance(TimeSpan.FromMinutes(20));

        engine.Tick();

        Assert.Equal(ExamStage.SupplementaryBriefing, engine.Session!.Stage);
        Assert.Equal([1, 2], engine.Session.ErrorGroups);
    }

    [Fact]
    public void SupplementaryDeadline_Fails()
    {
        var (engine, bank, time) = CreateEngine();
        AnswerAt(engine, 0, correct: false);
        AnswerAllRemaining(engine, correct: true);
        engine.StartSupplementary(QuestionFactory.Supplementary(bank, [1]));
        time.Advance(TimeSpan.FromMinutes(5));

        engine.Tick();
        Assert.Equal(ExamOutcome.Failed, engine.Session!.Outcome);
        Assert.Equal(5, engine.Session.SupplementaryErrors);
    }

    [Fact]
    public void MonotonicClockControlsRemainingTime_NotWallClockTicks()
    {
        var (engine, _, time) = CreateEngine();
        time.Advance(TimeSpan.FromMinutes(7) + TimeSpan.FromSeconds(13));
        engine.Tick();
        Assert.Equal(TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(47), engine.Session!.CurrentStageRemaining);
    }

    [Fact]
    public void InterruptedAttemptCannotContinue()
    {
        var (engine, _, _) = CreateEngine();
        engine.Interrupt();
        Assert.Equal(ExamOutcome.Interrupted, engine.Session!.Outcome);
        Assert.False(engine.SelectAnswer(1));
    }

    [Fact]
    public void ImmediateFailure_DoesNotTurnRemainingQuestionsIntoErrors()
    {
        var (engine, _, _) = CreateEngine();
        AnswerAt(engine, 0, correct: false);
        AnswerAt(engine, 1, correct: false);

        var result = engine.BuildResult();

        Assert.Equal(2, result.MainErrorCount);
        Assert.Equal(2, result.IncorrectAnswers.Count);
        Assert.Equal(18, engine.Session!.MainQuestions.Count(x => x.IsCorrect is null));
    }

    [Fact]
    public void NavigationPreservesDraftButDoesNotConfirmIt()
    {
        var (engine, _, _) = CreateEngine();
        Assert.True(engine.SelectAnswer(2));
        Assert.True(engine.NavigateTo(7));
        Assert.True(engine.NavigateTo(0));

        Assert.Equal(2, engine.Session!.CurrentQuestion!.PendingAnswer);
        Assert.Null(engine.Session.CurrentQuestion.ConfirmedAnswer);
        Assert.Equal(0, engine.Session.ConfirmedMainAnswers);
    }

    private static (Engine Engine, IReadOnlyList<Question> Bank, FakeExamTimeSource Time) CreateEngine()
    {
        var bank = QuestionFactory.CreateBank();
        var time = new FakeExamTimeSource();
        var engine = new Engine(time);
        engine.Start(new CandidateProfile { FullName = "ИВАНОВ ИВАН ИВАНОВИЧ" }, QuestionFactory.MainBlock(bank));
        return (engine, bank, time);
    }

    private static void AnswerAt(Engine engine, int index, bool correct)
    {
        Assert.True(engine.NavigateTo(index));
        var question = engine.Session!.CurrentQuestion!.Question;
        var answer = correct ? question.CorrectAnswer : question.CorrectAnswer % question.Answers.Count + 1;
        Assert.True(engine.SelectAnswer(answer));
        Assert.Equal(ConfirmAnswerStatus.Accepted, engine.ConfirmAnswer());
    }

    private static void AnswerAllRemaining(Engine engine, bool correct)
    {
        while (engine.Session!.Stage is ExamStage.Main or ExamStage.Supplementary &&
               engine.Session.ActiveQuestions.Any(q => !q.ConfirmedAnswer.HasValue && q.IsCorrect is null))
        {
            var index = engine.Session.ActiveQuestions.ToList().FindIndex(q => !q.ConfirmedAnswer.HasValue && q.IsCorrect is null);
            AnswerAt(engine, index, correct);
        }
    }

    private static void AnswerEveryQuestionExcept(Engine engine, IReadOnlyCollection<int> excludedIndexes)
    {
        for (var index = 0; index < engine.Session!.MainQuestions.Count; index++)
        {
            if (!excludedIndexes.Contains(index))
                AnswerAt(engine, index, correct: true);
        }
    }
}
