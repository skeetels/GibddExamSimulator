using GibddExamSimulator.Application.StudySessions;

namespace GibddExamSimulator.Application.Learning;

public sealed record ProblemTicketStatistics(
    int TicketNumber,
    int ErrorCount,
    int AttemptCount)
{
    public double ErrorRate => AttemptCount == 0 ? 0 : (double)ErrorCount / AttemptCount;
}

public static class ExamStatistics
{
    public static IReadOnlyList<ProblemTicketStatistics> GetProblemTickets(
        IEnumerable<StudySessionEnvelope> sessions,
        int limit = 3)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (limit <= 0)
            return [];

        return sessions
            .Where(session => session.Mode == StudyMode.Exam)
            .DistinctBy(session => session.SessionId)
            .SelectMany(session => session.Answers)
            .Where(answer => answer.SelectedAnswer.HasValue)
            .GroupBy(answer => answer.TicketNumber)
            .Select(group => new ProblemTicketStatistics(
                group.Key,
                group.Count(answer => !answer.IsCorrect),
                group.Count()))
            .Where(item => item.ErrorCount > 0)
            .OrderByDescending(item => item.ErrorCount)
            .ThenByDescending(item => item.ErrorRate)
            .ThenBy(item => item.TicketNumber)
            .Take(limit)
            .ToArray();
    }
}
