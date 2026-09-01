using System.Text.Json.Serialization;

namespace GibddExamSimulator.Models;

public sealed class Question
{
    public long Id { get; set; }
    public int TicketNumber { get; set; }
    public int QuestionNumber { get; set; }
    public string Category { get; set; } = "AB";
    public int GroupId { get; set; }
    public int ThematicBlockId { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string? ImagePath { get; set; }
    public List<string> Answers { get; set; } = [];
    public int CorrectAnswer { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    [JsonIgnore]
    public string DisplayNumber => $"{TicketNumber}/{QuestionNumber}";

    public void Validate()
    {
        if (Id <= 0)
            throw new InvalidOperationException("Идентификатор вопроса должен быть положительным.");
        if (GroupId is < 1 or > 4)
            throw new InvalidOperationException($"Вопрос {Id}: номер группы должен быть от 1 до 4.");
        if (ThematicBlockId <= 0)
            throw new InvalidOperationException($"Вопрос {Id}: идентификатор тематического блока должен быть положительным.");
        if (Answers.Count is < 2 or > 5)
            throw new InvalidOperationException($"Вопрос {Id}: допустимо от 2 до 5 ответов.");
        if (CorrectAnswer < 1 || CorrectAnswer > Answers.Count)
            throw new InvalidOperationException($"Вопрос {Id}: неверный номер правильного ответа.");
        if (string.IsNullOrWhiteSpace(QuestionText))
            throw new InvalidOperationException($"Вопрос {Id}: текст вопроса пуст.");
    }
}
