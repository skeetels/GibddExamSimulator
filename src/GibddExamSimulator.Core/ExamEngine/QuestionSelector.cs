using GibddExamSimulator.Models;

namespace GibddExamSimulator.ExamEngine;

public sealed class QuestionSelector
{
    private readonly Random _random;

    public QuestionSelector(Random? random = null)
    {
        _random = random ?? Random.Shared;
    }

    public IReadOnlyList<Question> SelectMainExam(IEnumerable<Question> source, string category) =>
        SelectMainExam(source, category, riskProfile: null);

    public IReadOnlyList<Question> SelectMainExam(
        IEnumerable<Question> source,
        string category,
        CandidateExamRiskProfile? riskProfile)
    {
        if (riskProfile is not null && !CategoryMatches(riskProfile.Category, category))
            throw new ArgumentException("Профиль риска относится к другому комплекту вопросов.", nameof(riskProfile));

        var active = source.Where(q => q.IsActive && CategoryMatches(q.Category, category)).ToList();
        var result = new List<Question>(ExamRules.MainQuestionCount);

        for (var groupNumber = 1; groupNumber <= ExamRules.ThematicBlockCount; groupNumber++)
        {
            var groupQuestions = active.Where(q => q.GroupId == groupNumber).ToList();
            var completeBlocks = GetCompleteBlocks(groupQuestions, ExamRules.QuestionsPerThematicBlock);

            if (completeBlocks.Count == 0)
                throw new InvalidOperationException($"Для категории {category} недостаточно полных тематических блоков в группе {groupNumber}.");

            result.AddRange(SelectMainBlock(completeBlocks, riskProfile));
        }

        return result;
    }

    public IReadOnlyList<Question> SelectSupplementary(
        IEnumerable<Question> source,
        string category,
        IReadOnlyCollection<int> groupNumbers,
        IReadOnlyCollection<long> excludedQuestionIds)
    {
        var active = source
            .Where(q => q.IsActive && CategoryMatches(q.Category, category))
            .Where(q => !excludedQuestionIds.Contains(q.Id))
            .ToList();
        var result = new List<Question>();

        foreach (var groupNumber in groupNumbers.Distinct().OrderBy(x => x))
        {
            var candidates = active.Where(q => q.GroupId == groupNumber).ToList();
            var completeBlocks = GetCompleteBlocks(candidates, ExamRules.SupplementaryQuestionsPerError);

            IReadOnlyList<Question> selected;
            if (completeBlocks.Count > 0)
            {
                selected = completeBlocks[_random.Next(completeBlocks.Count)];
            }
            else
            {
                throw new InvalidOperationException($"Для группы {groupNumber} нет отдельного полного дополнительного тематического блока.");
            }

            result.AddRange(selected);
        }

        return result;
    }

    public IReadOnlyList<Question> SelectTraining(
        IEnumerable<Question> source,
        string category,
        TrainingSelectionMode mode,
        int? selectorValue,
        IReadOnlyCollection<long>? problemQuestionIds,
        int count = 20)
    {
        var query = source.Where(q => q.IsActive && CategoryMatches(q.Category, category));
        query = mode switch
        {
            TrainingSelectionMode.Ticket when selectorValue.HasValue => query.Where(q => q.TicketNumber == selectorValue),
            TrainingSelectionMode.ThematicBlock when selectorValue.HasValue => query.Where(q => q.ThematicBlockId == selectorValue),
            TrainingSelectionMode.Mistakes when problemQuestionIds is not null => query.Where(q => problemQuestionIds.Contains(q.Id)),
            _ => query
        };

        var list = query.ToList();
        if (list.Count == 0)
            throw new InvalidOperationException("По выбранным условиям вопросы не найдены.");

        return mode == TrainingSelectionMode.Ticket
            ? list.OrderBy(q => q.QuestionNumber).ToArray()
            : list.OrderBy(_ => _random.Next()).Take(Math.Min(count, list.Count)).ToArray();
    }

    public static bool CategoryMatches(string questionCategory, string requestedCategory)
    {
        var q = NormalizeCategory(questionCategory);
        var r = NormalizeCategory(requestedCategory);
        return string.Equals(q, r, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCategory(string value) => value
        .Replace("/", string.Empty, StringComparison.Ordinal)
        .Replace(" ", string.Empty, StringComparison.Ordinal)
        .ToUpperInvariant();

    private IReadOnlyList<Question> SelectMainBlock(
        IReadOnlyList<IReadOnlyList<Question>> completeBlocks,
        CandidateExamRiskProfile? riskProfile)
    {
        if (riskProfile is null || !riskProfile.HasEvidence)
            return completeBlocks[_random.Next(completeBlocks.Count)];

        var ranked = completeBlocks
            .Select(block => RankBlock(block, riskProfile))
            .ToArray();

        if (ranked.Any(item => item.LearningRiskScore > 0))
            return SelectWeightedLearningBlock(ranked);

        // A miss in the most recent completed attempt is intentionally dominant.
        // Historical candidate/global difficulty then distinguishes equally recent blocks.
        var maxLastMissed = ranked.Max(item => item.LastMissedCount);
        var finalists = ranked.Where(item => item.LastMissedCount == maxLastMissed).ToArray();
        var maxDifficulty = finalists.Max(item => item.HistoricalDifficulty);
        finalists = finalists
            .Where(item => Math.Abs(item.HistoricalDifficulty - maxDifficulty) < 0.000000001)
            .ToArray();

        return finalists[_random.Next(finalists.Length)].Questions;
    }

    private IReadOnlyList<Question> SelectWeightedLearningBlock(IReadOnlyList<BlockRank> ranked)
    {
        const double explorationProbability = 0.12;
        if (_random.NextDouble() < explorationProbability)
            return ranked[_random.Next(ranked.Count)].Questions;

        var maxScore = ranked.Max(item => item.LearningRiskScore);
        var finalists = ranked
            .Where(item => item.LearningRiskScore >= Math.Max(0, maxScore - 20))
            .OrderBy(item => item.Questions[0].ThematicBlockId)
            .ToArray();
        var totalWeight = finalists.Sum(item => 1.0 + item.LearningRiskScore);
        var sample = _random.NextDouble() * totalWeight;
        foreach (var finalist in finalists)
        {
            sample -= 1.0 + finalist.LearningRiskScore;
            if (sample <= 0)
                return finalist.Questions;
        }
        return finalists[^1].Questions;
    }

    private static BlockRank RankBlock(
        IReadOnlyList<Question> block,
        CandidateExamRiskProfile riskProfile)
    {
        var lastMissed = 0;
        var candidateDifficulty = 0.0;
        var globalDifficulty = 0.0;
        var learningRisk = 0.0;

        foreach (var question in block)
        {
            var risk = riskProfile.GetRisk(question.Id);
            learningRisk += risk.LearningRiskScore;
            if (risk.WasWrongOnMostRecentCompletedAttempt)
                lastMissed++;

            if (risk.CandidateAttemptCount > 0)
            {
                candidateDifficulty += risk.CandidateErrorRate * 100.0;
                candidateDifficulty += Math.Min(risk.CandidateErrorCount, 20);
            }

            if (risk.GlobalAttemptCount > 0)
            {
                // A small sample may inform selection, but gains confidence up to ten responses.
                var confidence = Math.Min(1.0, risk.GlobalAttemptCount / 10.0);
                globalDifficulty += risk.GlobalErrorRate * confidence * 100.0;
            }
        }

        return new BlockRank(block, lastMissed, candidateDifficulty * 4.0 + globalDifficulty, learningRisk);
    }

    private static IReadOnlyList<IReadOnlyList<Question>> GetCompleteBlocks(
        IEnumerable<Question> questions,
        int requiredQuestionCount) =>
        questions
            .GroupBy(q => q.ThematicBlockId)
            .Where(block => block.Count() == requiredQuestionCount &&
                            block.Select(q => q.Id).Distinct().Count() == requiredQuestionCount &&
                            block.Select(q => q.QuestionNumber).Distinct().Count() == requiredQuestionCount)
            .Select(block => (IReadOnlyList<Question>)block
                .OrderBy(q => q.QuestionNumber)
                .ToArray())
            .OrderBy(block => block[0].ThematicBlockId)
            .ToArray();

    private sealed record BlockRank(
        IReadOnlyList<Question> Questions,
        int LastMissedCount,
        double HistoricalDifficulty,
        double LearningRiskScore);
}
