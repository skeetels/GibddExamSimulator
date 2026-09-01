using GibddExamSimulator.Application.StudySessions;

namespace GibddExamSimulator.Application.Learning;

public sealed class LearningProfileBuilder
{
    private static readonly int[] ReviewIntervalsDays = [1, 3, 7, 14, 30];

    public LearningProfile Build(IEnumerable<StudySessionEnvelope> source, DateTimeOffset asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(source);
        var sessions = Deduplicate(source)
            .OrderBy(session => session.CompletedAtUtc)
            .ThenBy(session => session.SessionId)
            .ToArray();

        var answersByQuestion = sessions
            .SelectMany(session => session.Answers
                .Where(answer => answer.SelectedAnswer.HasValue)
                .Select(answer => new WeightedAnswer(
                    session.Mode,
                    session.CompletedAtUtc,
                    answer)))
            .GroupBy(item => item.Answer.QuestionId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var aggregatesByQuestion = sessions
            .SelectMany(session => session.LegacyAggregates)
            .GroupBy(item => item.QuestionId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var questionIds = answersByQuestion.Keys.Concat(aggregatesByQuestion.Keys).Distinct().Order().ToArray();

        var profiles = questionIds.Select(questionId => BuildQuestion(
            questionId,
            answersByQuestion.GetValueOrDefault(questionId) ?? [],
            aggregatesByQuestion.GetValueOrDefault(questionId) ?? [],
            asOfUtc)).ToArray();
        return new LearningProfile(asOfUtc, profiles);
    }

    private static IReadOnlyList<StudySessionEnvelope> Deduplicate(IEnumerable<StudySessionEnvelope> source)
    {
        var result = new Dictionary<Guid, StudySessionEnvelope>();
        foreach (var original in source)
        {
            var session = string.IsNullOrWhiteSpace(original.PayloadSha256)
                ? original.WithComputedHash()
                : original;
            session.Validate();
            if (!result.TryGetValue(session.SessionId, out var existing))
            {
                result.Add(session.SessionId, session);
                continue;
            }
            if (!string.Equals(existing.PayloadSha256, session.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                throw new StudySessionIntegrityException(session.SessionId);
        }
        return result.Values.ToArray();
    }

    private static LearningQuestionProfile BuildQuestion(
        long questionId,
        IReadOnlyList<WeightedAnswer> source,
        IReadOnlyList<LegacyQuestionAggregate> legacyAggregates,
        DateTimeOffset asOfUtc)
    {
        var answers = source
            .OrderBy(item => item.Answer.AnsweredAtUtc ?? item.SessionCompletedAtUtc)
            .ThenBy(item => item.Answer.SequenceNumber)
            .ToArray();
        var legacyExposureCount = legacyAggregates.Sum(item => item.AttemptCount);
        var legacyErrorCount = legacyAggregates.Sum(item => item.AttemptCount - item.CorrectCount);
        var exposureCount = answers.Length + legacyExposureCount;
        var errorCount = answers.Count(item => !item.Answer.IsCorrect) + legacyErrorCount;
        var examErrorCount = answers.Count(item => item.Mode == StudyMode.Exam && !item.Answer.IsCorrect);

        var lastExplicit = answers.LastOrDefault();
        var lastExplicitAt = lastExplicit is null
            ? (DateTimeOffset?)null
            : lastExplicit.Answer.AnsweredAtUtc ?? lastExplicit.SessionCompletedAtUtc;
        var lastLegacyAt = legacyAggregates.Count == 0
            ? (DateTimeOffset?)null
            : legacyAggregates.Max(item => item.LastAttemptAtUtc);
        var lastAnsweredAt = Max(lastExplicitAt, lastLegacyAt);
        var explicitErrors = answers.Where(item => !item.Answer.IsCorrect).ToArray();
        var lastExplicitError = explicitErrors.Length == 0
            ? (DateTimeOffset?)null
            : explicitErrors[^1].Answer.AnsweredAtUtc ?? explicitErrors[^1].SessionCompletedAtUtc;
        var lastLegacyError = legacyErrorCount == 0 ? (DateTimeOffset?)null : lastLegacyAt;
        var lastError = Max(lastExplicitError, lastLegacyError);

        var streakAfterError = 0;
        if (explicitErrors.Length > 0)
        {
            foreach (var item in answers.Reverse())
            {
                if (!item.Answer.IsCorrect)
                    break;
                streakAfterError++;
            }
        }
        else if (answers.Length > 0 && legacyErrorCount == 0)
        {
            streakAfterError = answers.Count(item => item.Answer.IsCorrect);
        }

        var responseTimes = answers.Select(item => Math.Max(0L, item.Answer.ResponseTimeMs)).Order().ToArray();
        var responseTotal = responseTimes.Sum(value => (double)value) +
                            legacyAggregates.Sum(item => (double)item.TotalResponseTimeMs);
        var average = exposureCount == 0 ? 0 : responseTotal / exposureCount;
        var median = responseTimes.Length switch
        {
            0 => average,
            _ when responseTimes.Length % 2 == 1 => responseTimes[responseTimes.Length / 2],
            _ => (responseTimes[responseTimes.Length / 2 - 1] + responseTimes[responseTimes.Length / 2]) / 2.0
        };

        var mastery = Math.Min(5, streakAfterError);
        DateTimeOffset? dueAt;
        bool? lastAnswerWasCorrect;
        if (lastLegacyAt > lastExplicitAt)
        {
            lastAnswerWasCorrect = null;
            mastery = legacyErrorCount > 0 ? 0 : Math.Min(5, legacyExposureCount);
            dueAt = legacyErrorCount > 0 ? lastLegacyAt : lastLegacyAt?.AddDays(30);
        }
        else if (lastExplicit is not null && !lastExplicit.Answer.IsCorrect)
        {
            lastAnswerWasCorrect = false;
            mastery = 0;
            dueAt = lastAnsweredAt;
        }
        else if (lastExplicit is not null)
        {
            lastAnswerWasCorrect = true;
            var intervalIndex = Math.Clamp(Math.Max(1, streakAfterError) - 1, 0, ReviewIntervalsDays.Length - 1);
            dueAt = lastAnsweredAt?.AddDays(ReviewIntervalsDays[intervalIndex]);
        }
        else
        {
            lastAnswerWasCorrect = null;
            dueAt = lastAnsweredAt;
        }

        var weightedRecentErrors = answers
            .Where(item => !item.Answer.IsCorrect)
            .Sum(item => ModeWeight(item.Mode) *
                         RecencyWeight(asOfUtc, item.Answer.AnsweredAtUtc ?? item.SessionCompletedAtUtc));
        weightedRecentErrors += legacyAggregates.Sum(item =>
            (item.AttemptCount - item.CorrectCount) / (double)Math.Max(1, item.AttemptCount) *
            RecencyWeight(asOfUtc, item.LastAttemptAtUtc));
        var errorRate = exposureCount == 0 ? 0 : errorCount / (double)exposureCount;
        var overdueDays = dueAt <= asOfUtc ? Math.Max(0, (asOfUtc - dueAt.Value).TotalDays) : 0;
        var slowCorrectPenalty = average <= 20_000 ? 0 : Math.Min(12, (average - 20_000) / 4_000);
        var risk = errorRate * 40
                   + Math.Min(36, weightedRecentErrors * 13)
                   + Math.Min(15, overdueDays * 2 + (dueAt <= asOfUtc ? 5 : 0))
                   + slowCorrectPenalty
                   - mastery * 4;

        return new LearningQuestionProfile
        {
            QuestionId = questionId,
            ExposureCount = exposureCount,
            ErrorCount = errorCount,
            ErrorRate = errorRate,
            LastAnsweredAtUtc = lastAnsweredAt,
            LastErrorAtUtc = lastError,
            LastAnswerWasCorrect = lastAnswerWasCorrect,
            CorrectStreakAfterError = streakAfterError,
            AverageResponseTimeMs = average,
            MedianResponseTimeMs = median,
            MasteryLevel = mastery,
            DueAtUtc = dueAt,
            RiskScore = Math.Clamp(risk, 0, 100),
            ExamErrorCount = examErrorCount
        };
    }

    private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null)
            return right;
        if (right is null)
            return left;
        return left >= right ? left : right;
    }

    private static double ModeWeight(StudyMode mode) => mode == StudyMode.Exam ? 1.6 : 1.0;

    private static double RecencyWeight(DateTimeOffset asOfUtc, DateTimeOffset answeredAtUtc)
    {
        var ageDays = Math.Max(0, (asOfUtc - answeredAtUtc).TotalDays);
        return 1.0 / (1.0 + ageDays / 30.0);
    }

    private sealed record WeightedAnswer(
        StudyMode Mode,
        DateTimeOffset SessionCompletedAtUtc,
        StudyAnswerEvent Answer);
}
