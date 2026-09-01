using GibddExamSimulator.Application.StudySessions;
using GibddExamSimulator.Models;
using GibddExamSimulator.Web.Services;

namespace GibddExamSimulator.Web.Tests;

public sealed class MobileSessionControllerTests
{
    [Fact]
    public void TrainingCompletion_CreatesMobilePwaEnvelope()
    {
        var device = Guid.NewGuid();
        var controller = MobileSessionController.CreateTraining(
            device,
            StudyMode.SmartTen,
            [Question(1), Question(2)]);

        Assert.True(controller.SelectAnswer(1));
        Assert.True(controller.ConfirmAnswer());
        Assert.True(controller.SelectAnswer(1));
        Assert.True(controller.ConfirmAnswer());

        var envelope = controller.BuildEnvelope("test-ab", new string('C', 64), "rules");
        Assert.Equal(StudyDeviceKind.MobilePwa, envelope.DeviceKind);
        Assert.Equal(StudyMode.SmartTen, envelope.Mode);
        Assert.Equal(2, envelope.Summary.CorrectCount);
        Assert.True(StudySessionCanonicalizer.VerifyPayloadSha256(envelope));
    }

    [Fact]
    public void TrainingDraft_RestoresConfirmedAnswersAndPosition()
    {
        var device = Guid.NewGuid();
        var questions = new[] { Question(1), Question(2), Question(3) };
        var controller = MobileSessionController.CreateTraining(device, StudyMode.WeakTopics, questions);
        controller.SelectAnswer(1);
        controller.ConfirmAnswer();
        var draft = controller.CreateDraft("test-ab", new string('D', 64));

        var restored = MobileSessionController.Restore(device, draft, questions.ToDictionary(question => question.Id));

        Assert.Single(restored.ActiveStates, state => state.ConfirmedAnswer.HasValue);
        Assert.Equal(1, restored.CurrentIndex);
        Assert.Equal(StudyMode.WeakTopics, restored.Mode);
    }

    [Fact]
    public void ExamDraft_RestoresAllTwentyNavigationItems()
    {
        var device = Guid.NewGuid();
        var questions = ExamQuestions();
        var controller = MobileSessionController.CreateExam(
            device,
            new CandidateProfile { FullName = "Test", Category = "AB" },
            questions);
        controller.NavigateTo(11);
        var draft = controller.CreateDraft("test-ab", new string('E', 64));

        var restored = MobileSessionController.Restore(device, draft, questions.ToDictionary(question => question.Id));

        Assert.Equal(20, restored.ActiveStates.Count);
        Assert.Equal(11, restored.CurrentIndex);
        Assert.True(restored.IsExam);
    }

    private static IReadOnlyList<Question> ExamQuestions() => Enumerable.Range(1, 20)
        .Select(index => new Question
        {
            Id = index,
            TicketNumber = index,
            QuestionNumber = index,
            Category = "AB",
            GroupId = (index - 1) / 5 + 1,
            ThematicBlockId = (index - 1) / 5 + 1,
            QuestionText = $"Question {index}",
            Answers = ["Correct", "Wrong"],
            CorrectAnswer = 1
        }).ToArray();

    private static Question Question(long id) => new()
    {
        Id = id,
        TicketNumber = 1,
        QuestionNumber = (int)id,
        Category = "AB",
        GroupId = 1,
        ThematicBlockId = 1,
        QuestionText = $"Question {id}",
        Answers = ["Correct", "Wrong"],
        CorrectAnswer = 1
    };
}
