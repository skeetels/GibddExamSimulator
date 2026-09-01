using System.Text.Json;

namespace GibddExamSimulator.Database;

public sealed class JsonQuestionImporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public async Task<IReadOnlyList<Models.Question>> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement array;
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            array = document.RootElement;
        }
        else if (document.RootElement.TryGetProperty("questions", out var questionsElement) &&
                 questionsElement.ValueKind == JsonValueKind.Array)
        {
            array = questionsElement;
        }
        else
        {
            throw new InvalidDataException("JSON должен содержать массив вопросов или объект с полем questions.");
        }

        var items = JsonSerializer.Deserialize<List<QuestionImportDto>>(array.GetRawText(), Options) ?? [];
        var questions = items.Select(x => x.ToQuestion()).ToArray();
        foreach (var question in questions)
            question.Validate();
        EnsureBlockIntegrity(questions);
        return questions;
    }

    private static void EnsureBlockIntegrity(IEnumerable<Models.Question> questions)
    {
        var duplicates = questions.GroupBy(q => q.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicates.Length > 0)
            throw new InvalidDataException($"Повторяются идентификаторы вопросов: {string.Join(", ", duplicates)}.");

        var invalidBlocks = questions
            .Where(q => q.IsActive)
            .GroupBy(q => new { q.Category, q.GroupId, q.ThematicBlockId })
            .Where(g => g.Count() != 5)
            .Select(g => $"{g.Key.Category}/группа {g.Key.GroupId}/блок {g.Key.ThematicBlockId}: {g.Count()}")
            .ToArray();
        if (invalidBlocks.Length > 0)
            throw new InvalidDataException("Каждый активный тематический блок должен содержать ровно 5 вопросов. " +
                                           string.Join("; ", invalidBlocks));
    }
}

