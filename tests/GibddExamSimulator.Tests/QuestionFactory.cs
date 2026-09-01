using GibddExamSimulator.Models;

namespace GibddExamSimulator.Tests;

internal static class QuestionFactory
{
    public static IReadOnlyList<Question> CreateBank(string category = "AB", int blocksPerGroup = 3)
    {
        var result = new List<Question>();
        long id = category == "AB" ? 1 : 10_001;
        for (var group = 1; group <= 4; group++)
        {
            for (var blockIndex = 1; blockIndex <= blocksPerGroup; blockIndex++)
            {
                var blockId = group * 100 + blockIndex;
                for (var position = 1; position <= 5; position++)
                {
                    result.Add(new Question
                    {
                        Id = id++,
                        TicketNumber = blockIndex,
                        QuestionNumber = (group - 1) * 5 + position,
                        Category = category,
                        GroupId = group,
                        ThematicBlockId = blockId,
                        QuestionText = $"Демонстрационный вопрос {group}.{blockIndex}.{position}",
                        Answers = ["Правильный", "Неправильный", "Другой"],
                        CorrectAnswer = 1,
                        Explanation = "Пояснение"
                    });
                }
            }
        }
        return result;
    }

    public static IReadOnlyList<Question> MainBlock(IReadOnlyList<Question> bank, int blockIndex = 1) =>
        bank.Where(q => q.ThematicBlockId % 100 == blockIndex)
            .OrderBy(q => q.GroupId)
            .ThenBy(q => q.QuestionNumber)
            .ToArray();

    public static IReadOnlyList<Question> Supplementary(
        IReadOnlyList<Question> bank,
        IReadOnlyCollection<int> groups,
        int blockIndex = 2) =>
        bank.Where(q => groups.Contains(q.GroupId) && q.ThematicBlockId % 100 == blockIndex)
            .OrderBy(q => q.GroupId)
            .ThenBy(q => q.QuestionNumber)
            .ToArray();
}

