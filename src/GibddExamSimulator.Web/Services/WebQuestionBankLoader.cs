using System.Security.Cryptography;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GibddExamSimulator.Models;

namespace GibddExamSimulator.Web.Services;

public sealed record WebQuestionBankManifest
{
    public int SchemaVersion { get; init; }
    public string BankVersion { get; init; } = string.Empty;
    public string BankSha256 { get; init; } = string.Empty;
    public int QuestionCount { get; init; }
    public int TicketCount { get; init; }
    public int BlockCount { get; init; }
    public int ImageCount { get; init; }
    public long ImageBytes { get; init; }
}

public sealed record WebQuestionBank(WebQuestionBankManifest Manifest, IReadOnlyList<Question> Questions);

public sealed class WebQuestionBankLoader(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<WebQuestionBank> LoadAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await httpClient.GetFromJsonAsync<WebQuestionBankManifest>(
                           "question-bank/ab/bank-manifest.json",
                           JsonOptions,
                           cancellationToken)
                       ?? throw new InvalidDataException("Манифест комплекта AB пуст.");
        var payload = await httpClient.GetByteArrayAsync("question-bank/ab/official-questions.json", cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        if (!string.Equals(hash, manifest.BankSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Контрольная сумма комплекта AB не совпадает с манифестом.");

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("questions", out var questionsElement) ||
            questionsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Комплект AB не содержит массива questions.");
        var imports = questionsElement.Deserialize<List<QuestionImport>>(JsonOptions) ?? [];
        var questions = imports.Select(item => item.ToQuestion()).ToArray();
        foreach (var question in questions)
            question.Validate();
        Validate(manifest, questions);
        return new WebQuestionBank(manifest, questions);
    }

    private static void Validate(WebQuestionBankManifest manifest, IReadOnlyList<Question> questions)
    {
        if (manifest.SchemaVersion != 1 || manifest.QuestionCount != 800 || manifest.TicketCount != 40 ||
            manifest.BlockCount != 160 || manifest.ImageCount != 548 || manifest.ImageBytes <= 0 || questions.Count != 800)
            throw new InvalidDataException("Комплект AB имеет неверное количество вопросов, билетов, блоков или изображений.");
        if (!questions.Select(question => question.Id).Order().SequenceEqual(Enumerable.Range(1, 800).Select(id => (long)id)))
            throw new InvalidDataException("Идентификаторы вопросов AB должны быть 1..800.");
        if (questions.Any(question => !string.Equals(question.Category, "AB", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("В комплекте AB обнаружена другая категория.");
        if (questions.GroupBy(question => question.TicketNumber).Count() != 40 ||
            questions.GroupBy(question => question.TicketNumber).Any(ticket => ticket.Count() != 20))
            throw new InvalidDataException("Комплект AB должен содержать 40 билетов по 20 вопросов.");
        if (questions.GroupBy(question => (question.GroupId, question.ThematicBlockId)).Count() != 160 ||
            questions.GroupBy(question => (question.GroupId, question.ThematicBlockId)).Any(block => block.Count() != 5))
            throw new InvalidDataException("Комплект AB должен содержать 160 тематических блоков по 5 вопросов.");
        if (questions.Where(question => question.ImagePath is not null)
                .Select(question => question.ImagePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != 548)
            throw new InvalidDataException("Комплект AB должен ссылаться на 548 JPEG-изображений.");
    }

    private sealed record QuestionImport
    {
        public long Id { get; init; }
        public int Ticket { get; init; }
        public int Number { get; init; }
        public string Category { get; init; } = "AB";
        public int Group { get; init; }
        public int ThematicBlock { get; init; }
        public string Question { get; init; } = string.Empty;
        public string? Image { get; init; }
        public IReadOnlyList<string> Answers { get; init; } = [];
        public int CorrectAnswer { get; init; }
        public string Explanation { get; init; } = string.Empty;
        public bool IsActive { get; init; } = true;

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
            Answers = Answers.ToList(),
            CorrectAnswer = CorrectAnswer,
            Explanation = Explanation,
            IsActive = IsActive
        };
    }
}
