using System.Text.Json.Serialization;
using GibddExamSimulator.Models;

namespace GibddExamSimulator.Database;

public sealed class QuestionImportDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("ticket")]
    public int Ticket { get; set; }

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = "AB";

    [JsonPropertyName("group")]
    public int Group { get; set; }

    [JsonPropertyName("thematicBlock")]
    public int ThematicBlock { get; set; }

    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("answers")]
    public List<string> Answers { get; set; } = [];

    [JsonPropertyName("correctAnswer")]
    public int CorrectAnswer { get; set; }

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = string.Empty;

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; } = true;

    public Models.Question ToQuestion() => new()
    {
        Id = Id,
        TicketNumber = Ticket,
        QuestionNumber = Number,
        Category = Category,
        GroupId = Group,
        ThematicBlockId = ThematicBlock,
        QuestionText = Question,
        ImagePath = Image,
        Answers = Answers,
        CorrectAnswer = CorrectAnswer,
        Explanation = Explanation,
        IsActive = IsActive
    };
}

