using GibddExamSimulator.Application.StudySessions;
using GibddExamSimulator.Models;

namespace GibddExamSimulator.Application.Learning;

public sealed class TrainingQuestionPlanner
{
    private readonly Random _random;

    public TrainingQuestionPlanner(Random? random = null)
    {
        _random = random ?? Random.Shared;
    }

    public IReadOnlyList<Question> Select(
        IEnumerable<Question> source,
        LearningProfile profile,
        StudyMode mode,
        int count = 10,
        int? ticketNumber = null)
    {
        var questions = source.Where(question => question.IsActive &&
                                                string.Equals(question.Category, "AB", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (questions.Length == 0)
            throw new InvalidOperationException("The AB question bank is empty.");

        return mode switch
        {
            StudyMode.Ticket => questions.Where(question => question.TicketNumber == ticketNumber)
                .OrderBy(question => question.QuestionNumber)
                .ToArray(),
            StudyMode.SmartTen => SelectSmart(questions, profile, Math.Clamp(count, 1, 80)),
            StudyMode.MistakeReview => Rank(questions, profile)
                .Where(item => item.Profile?.ErrorCount > 0 && item.Profile.IsDue(profile.CalculatedAtUtc))
                .Take(Math.Clamp(count, 1, 80)).Select(item => item.Question).ToArray(),
            StudyMode.WeakTopics => SelectWeakTopics(questions, profile, Math.Clamp(count, 1, 80)),
            StudyMode.Marathon => Shuffle(questions).Take(Math.Clamp(count, 1, 800)).ToArray(),
            StudyMode.NoMistakeChallenge => SelectSmart(questions, profile, Math.Clamp(count, 1, 800)),
            _ => SelectSmart(questions, profile, Math.Clamp(count, 1, 80))
        };
    }

    private IReadOnlyList<Question> SelectSmart(Question[] questions, LearningProfile profile, int count)
    {
        var ranked = Rank(questions, profile).ToArray();
        var unseen = ranked.Where(item => item.Profile is null).Select(item => item.Question).ToArray();
        var explorationCount = Math.Min(unseen.Length, Math.Max(1, (int)Math.Round(count * 0.2)));
        var selected = ranked.Where(item => item.Profile is not null)
            .Take(Math.Max(0, count - explorationCount))
            .Select(item => item.Question)
            .Concat(Shuffle(unseen).Take(explorationCount))
            .DistinctBy(question => question.Id)
            .Take(count)
            .ToList();
        if (selected.Count < count)
            selected.AddRange(ranked.Select(item => item.Question).Where(question => selected.All(x => x.Id != question.Id)).Take(count - selected.Count));
        return selected;
    }

    private IReadOnlyList<Question> SelectWeakTopics(Question[] questions, LearningProfile profile, int count)
    {
        var weakBlocks = questions.GroupBy(question => (question.GroupId, question.ThematicBlockId))
            .Select(group => new
            {
                Questions = group.ToArray(),
                Score = group.Average(question => profile.GetQuestion(question.Id)?.RiskScore ?? 30)
            })
            .OrderByDescending(block => block.Score)
            .ThenBy(_ => _random.Next())
            .ToArray();
        return weakBlocks.SelectMany(block => block.Questions.OrderBy(_ => _random.Next()))
            .DistinctBy(question => question.Id)
            .Take(count)
            .ToArray();
    }

    private IEnumerable<(Question Question, LearningQuestionProfile? Profile)> Rank(
        IEnumerable<Question> questions,
        LearningProfile profile) => questions
        .Select(question => (Question: question, Profile: profile.GetQuestion(question.Id)))
        .OrderByDescending(item => item.Profile?.IsDue(profile.CalculatedAtUtc) == true)
        .ThenByDescending(item => item.Profile?.RiskScore ?? -1)
        .ThenBy(_ => _random.Next());

    private IEnumerable<T> Shuffle<T>(IEnumerable<T> source) => source.OrderBy(_ => _random.Next());
}
