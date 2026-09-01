using System.Security.Cryptography;
using System.Text.Json;
using GibddExamSimulator.Models;

namespace GibddExamSimulator.Database;

public sealed record QuestionBankManifest
{
    public int SchemaVersion { get; init; }
    public string BankVersion { get; init; } = string.Empty;
    public string BankSha256 { get; init; } = string.Empty;
    public int QuestionCount { get; init; }
    public int TicketCount { get; init; }
    public int BlockCount { get; init; }
    public int ImageCount { get; init; }
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public IReadOnlyList<string> Sources { get; init; } = [];
}

public sealed record QuestionBankPackage(
    QuestionBankManifest Manifest,
    IReadOnlyList<Question> Questions,
    string RootDirectory);

public sealed class QuestionBankPackageLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<QuestionBankPackage> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(rootDirectory);
        var manifestPath = Path.Combine(root, "bank-manifest.json");
        var questionsPath = Path.Combine(root, "official-questions.json");
        if (!File.Exists(manifestPath) || !File.Exists(questionsPath))
            throw new FileNotFoundException("The canonical AB question bank is incomplete.");

        await using var manifestStream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<QuestionBankManifest>(manifestStream, JsonOptions, cancellationToken)
                       ?? throw new InvalidDataException("Question-bank manifest is empty.");
        var actualHash = await ComputeSha256Async(questionsPath, cancellationToken);
        if (!string.Equals(actualHash, manifest.BankSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The AB question-bank SHA-256 does not match its manifest.");

        var questions = await new JsonQuestionImporter().ReadAsync(questionsPath, cancellationToken);
        ValidateQuestions(questions, manifest, root);
        return new QuestionBankPackage(manifest, questions, root);
    }

    private static void ValidateQuestions(
        IReadOnlyList<Question> questions,
        QuestionBankManifest manifest,
        string root)
    {
        if (manifest.SchemaVersion != 1 || manifest.QuestionCount != 800 ||
            manifest.TicketCount != 40 || manifest.BlockCount != 160 || manifest.ImageCount != 548)
            throw new InvalidDataException("The AB question-bank manifest contains unexpected counts.");
        if (questions.Count != 800 || questions.Select(question => question.Id).Order().SequenceEqual(Enumerable.Range(1, 800).Select(value => (long)value)) == false)
            throw new InvalidDataException("The AB question bank must contain IDs 1..800.");
        if (questions.Any(question => !string.Equals(question.Category, "AB", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The question bank contains a category other than AB.");
        if (questions.GroupBy(question => question.TicketNumber).Count() != 40 ||
            questions.GroupBy(question => question.TicketNumber).Any(ticket => ticket.Count() != 20))
            throw new InvalidDataException("The question bank must contain 40 tickets of 20 questions.");
        if (questions.GroupBy(question => (question.GroupId, question.ThematicBlockId)).Count() != 160 ||
            questions.GroupBy(question => (question.GroupId, question.ThematicBlockId)).Any(block => block.Count() != 5))
            throw new InvalidDataException("The question bank must contain 160 blocks of 5 questions.");

        var images = questions.Where(question => !string.IsNullOrWhiteSpace(question.ImagePath))
            .Select(question => question.ImagePath!.Replace('/', Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (images.Length != 548)
            throw new InvalidDataException("The AB question bank must reference 548 images.");
        foreach (var relativePath in images)
        {
            var path = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new InvalidDataException("A referenced question image is missing or outside the bank root.");
            using var stream = File.OpenRead(path);
            if (stream.Length < 4 || stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
                throw new InvalidDataException("A question image is not a JPEG file.");
            stream.Seek(-2, SeekOrigin.End);
            if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD9)
                throw new InvalidDataException("A question JPEG has an invalid end marker.");
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
