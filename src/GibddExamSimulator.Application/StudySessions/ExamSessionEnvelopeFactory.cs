using GibddExamSimulator.Models;

namespace GibddExamSimulator.Application.StudySessions;

public static class ExamSessionEnvelopeFactory
{
    public static StudySessionEnvelope Create(
        ExamSession session,
        Guid deviceId,
        StudyDeviceKind deviceKind,
        string bankVersion,
        string bankSha256,
        string rulesProfile)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Stage != ExamStage.Completed || session.EndedAtUtc is null || session.StartedAtUtc is null)
            throw new InvalidOperationException("Only a completed exam can be converted to a study session.");

        var states = session.MainQuestions.Concat(session.SupplementaryQuestions).ToArray();
        var answers = states.Select((state, index) => new StudyAnswerEvent
        {
            SequenceNumber = index + 1,
            QuestionId = state.Question.Id,
            TicketNumber = state.Question.TicketNumber,
            QuestionNumber = state.Question.QuestionNumber,
            GroupId = state.Question.GroupId,
            ThematicBlockId = state.Question.ThematicBlockId,
            Stage = state.Stage == ExamStage.Supplementary ? StudyStage.Supplementary : StudyStage.Main,
            SelectedAnswer = state.ConfirmedAnswer,
            CorrectAnswer = state.Question.CorrectAnswer,
            IsCorrect = state.IsCorrect == true,
            ResponseTimeMs = Math.Max(0, (long)(state.AnswerTime ?? TimeSpan.Zero).TotalMilliseconds),
            AnsweredAtUtc = state.AnsweredAtUtc
        }).ToArray();
        var answered = answers.Where(answer => answer.SelectedAnswer.HasValue).ToArray();

        var longestStreak = 0;
        var currentStreak = 0;
        foreach (var answer in answered)
        {
            currentStreak = answer.IsCorrect ? currentStreak + 1 : 0;
            longestStreak = Math.Max(longestStreak, currentStreak);
        }

        return new StudySessionEnvelope
        {
            SessionId = session.Id,
            DeviceId = deviceId,
            DeviceKind = deviceKind,
            Mode = StudyMode.Exam,
            StartedAtUtc = session.StartedAtUtc.Value,
            CompletedAtUtc = session.EndedAtUtc.Value,
            Outcome = session.Outcome switch
            {
                ExamOutcome.Passed => StudyOutcome.Passed,
                ExamOutcome.Failed => StudyOutcome.Failed,
                _ => StudyOutcome.Abandoned
            },
            BankVersion = bankVersion,
            BankSha256 = bankSha256,
            RulesProfile = rulesProfile,
            OrderedQuestionIds = states.Select(state => state.Question.Id).ToArray(),
            Answers = answers,
            Summary = new StudySessionSummary
            {
                QuestionCount = states.Length,
                AnsweredCount = answered.Length,
                CorrectCount = answered.Count(answer => answer.IsCorrect),
                ErrorCount = answered.Count(answer => !answer.IsCorrect),
                ElapsedMs = Math.Max(0, (long)(session.MainElapsed + session.SupplementaryElapsed).TotalMilliseconds),
                LongestCorrectStreak = longestStreak
            }
        }.WithComputedHash();
    }
}
