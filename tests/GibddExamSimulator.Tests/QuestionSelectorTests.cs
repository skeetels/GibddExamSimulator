using GibddExamSimulator.ExamEngine;
using GibddExamSimulator.Models;

namespace GibddExamSimulator.Tests;

public sealed class QuestionSelectorTests
{
    [Fact]
    public void MainExam_ContainsFourWholeOrderedBlocks()
    {
        var selector = new QuestionSelector(new Random(17));
        var selected = selector.SelectMainExam(QuestionFactory.CreateBank(), "A/B");

        Assert.Equal(20, selected.Count);
        foreach (var group in Enumerable.Range(1, 4))
        {
            var questions = selected.Where(q => q.GroupId == group).ToArray();
            Assert.Equal(5, questions.Length);
            Assert.Single(questions.Select(q => q.ThematicBlockId).Distinct());
            Assert.Equal(questions.OrderBy(q => q.QuestionNumber).Select(q => q.Id), questions.Select(q => q.Id));
        }
    }

    [Fact]
    public void MainExam_DoesNotMixCategoriesOrInactiveQuestions()
    {
        var bank = QuestionFactory.CreateBank("AB").Concat(QuestionFactory.CreateBank("OTHER")).ToList();
        bank[0].IsActive = false;
        var selected = new QuestionSelector(new Random(4)).SelectMainExam(bank, "OTHER");

        Assert.All(selected, q => Assert.Equal("OTHER", q.Category));
        Assert.All(selected, q => Assert.True(q.IsActive));
    }

    [Fact]
    public void Supplementary_UsesWholeBlockFromRequiredGroupWithoutRepeats()
    {
        var bank = QuestionFactory.CreateBank();
        var main = QuestionFactory.MainBlock(bank);
        var selected = new QuestionSelector(new Random(1)).SelectSupplementary(
            bank, "AB", [1, 3], main.Select(q => q.Id).ToArray());

        Assert.Equal(10, selected.Count);
        Assert.Equal([1, 3], selected.Select(q => q.GroupId).Distinct().OrderBy(x => x));
        Assert.Empty(selected.Select(q => q.Id).Intersect(main.Select(q => q.Id)));
        Assert.All(selected.GroupBy(q => q.GroupId), g => Assert.Single(g.Select(q => q.ThematicBlockId).Distinct()));
    }

    [Fact]
    public void IncompleteQuestionBank_IsRejected()
    {
        var bank = QuestionFactory.CreateBank().Where(q => q.GroupId != 4).ToArray();
        Assert.Throws<InvalidOperationException>(() => new QuestionSelector().SelectMainExam(bank, "AB"));
    }

    [Fact]
    public void AdaptiveMainExam_LastMissedBlockDominatesOlderDifficulty()
    {
        var bank = QuestionFactory.CreateBank();
        var risks = new List<QuestionRisk>();

        foreach (var group in Enumerable.Range(1, 4))
        {
            var lastMissed = bank.First(q => q.GroupId == group && q.ThematicBlockId % 100 == 3);
            risks.Add(new QuestionRisk(lastMissed.Id, 1, 1, 1, 1, true));

            foreach (var difficult in bank.Where(q => q.GroupId == group && q.ThematicBlockId % 100 == 2))
                risks.Add(new QuestionRisk(difficult.Id, 100, 100, 100, 100, false));
        }

        var profile = CreateProfile(risks);
        foreach (var seed in Enumerable.Range(0, 20))
        {
            var selected = new QuestionSelector(new Random(seed)).SelectMainExam(bank, "AB", profile);
            Assert.All(selected, question => Assert.Equal(3, question.ThematicBlockId % 100));
        }
    }

    [Fact]
    public void AdaptiveMainExam_UsesCandidateAndGlobalHistoricalDifficulty()
    {
        var bank = QuestionFactory.CreateBank();
        var risks = new List<QuestionRisk>();

        foreach (var question in bank.Where(q => q.GroupId == 1 && q.ThematicBlockId % 100 == 2))
            risks.Add(new QuestionRisk(question.Id, 5, 4, 5, 4, false));

        foreach (var question in bank.Where(q => q.GroupId == 2 && q.ThematicBlockId % 100 == 3))
            risks.Add(new QuestionRisk(question.Id, 0, 0, 10, 8, false));

        var selected = new QuestionSelector(new Random(11)).SelectMainExam(bank, "AB", CreateProfile(risks));

        Assert.All(selected.Where(q => q.GroupId == 1), q => Assert.Equal(2, q.ThematicBlockId % 100));
        Assert.All(selected.Where(q => q.GroupId == 2), q => Assert.Equal(3, q.ThematicBlockId % 100));
        Assert.Equal(20, selected.Count);
        Assert.All(selected.GroupBy(q => q.GroupId), group =>
        {
            Assert.Equal(5, group.Count());
            Assert.Single(group.Select(q => q.ThematicBlockId).Distinct());
        });
    }

    [Fact]
    public void AdaptiveMainExam_EmptyProfileUsesSameDeterministicRandomFallback()
    {
        var bank = QuestionFactory.CreateBank();
        var expected = new QuestionSelector(new Random(27)).SelectMainExam(bank, "AB");
        var profile = CreateProfile([]);

        var actual = new QuestionSelector(new Random(27)).SelectMainExam(bank.Reverse(), "A/B", profile);

        Assert.Equal(expected.Select(q => q.Id), actual.Select(q => q.Id));
    }

    [Fact]
    public void AdaptiveMainExam_DoesNotTakeFiveQuestionsFromOversizedMalformedBlock()
    {
        var bank = QuestionFactory.CreateBank().ToList();
        var malformed = bank.First(q => q.GroupId == 1 && q.ThematicBlockId % 100 == 1);
        bank.Add(new Question
        {
            Id = 99_999,
            TicketNumber = malformed.TicketNumber,
            QuestionNumber = 99,
            Category = malformed.Category,
            GroupId = malformed.GroupId,
            ThematicBlockId = malformed.ThematicBlockId,
            QuestionText = "Лишний вопрос в повреждённом блоке",
            Answers = ["Да", "Нет"],
            CorrectAnswer = 1
        });
        var profile = CreateProfile([
            new QuestionRisk(malformed.Id, 1, 1, 1, 1, true),
            new QuestionRisk(99_999, 1, 1, 1, 1, true)
        ]);

        var selected = new QuestionSelector(new Random(3)).SelectMainExam(bank, "AB", profile);
        var groupOne = selected.Where(q => q.GroupId == 1).ToArray();

        Assert.Equal(5, groupOne.Length);
        Assert.DoesNotContain(groupOne, q => q.ThematicBlockId == malformed.ThematicBlockId);
    }

    [Fact]
    public void AdaptiveMainExam_RejectsProfileForAnotherQuestionSet()
    {
        var profile = CreateProfile([], "OTHER");

        Assert.Throws<ArgumentException>(() =>
            new QuestionSelector().SelectMainExam(QuestionFactory.CreateBank(), "AB", profile));
    }

    private static CandidateExamRiskProfile CreateProfile(
        IEnumerable<QuestionRisk> risks,
        string category = "AB") =>
        new("ИВАНОВ ИВАН ИВАНОВИЧ", new DateOnly(2000, 1, 1), category, risks);
}
