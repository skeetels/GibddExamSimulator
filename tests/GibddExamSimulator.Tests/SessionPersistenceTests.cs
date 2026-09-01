using System.Text.RegularExpressions;
using GibddExamSimulator.Application.Learning;
using GibddExamSimulator.Application.StudySessions;
using GibddExamSimulator.Database;
using GibddExamSimulator.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace GibddExamSimulator.Tests;

public sealed class SessionPersistenceTests
{
    [Fact]
    public void CanonicalHash_IncludesDeviceKind()
    {
        var desktop = CreateSession(StudyDeviceKind.WindowsDesktop);
        var mobile = desktop with { DeviceKind = StudyDeviceKind.MobilePwa, PayloadSha256 = string.Empty };

        Assert.NotEqual(
            StudySessionCanonicalizer.ComputePayloadSha256(desktop),
            StudySessionCanonicalizer.ComputePayloadSha256(mobile));
    }

    [Fact]
    public async Task DesktopStore_SavesSessionAndOutboxAtomicallyAndIdempotently()
    {
        var root = Path.Combine(Path.GetTempPath(), "gibdd-v2-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new DesktopStudyStore(Path.Combine(root, "study.db"));
            await store.InitializeAsync();
            var user = Guid.NewGuid();
            var session = CreateSession(StudyDeviceKind.WindowsDesktop);

            await store.SaveCompletedSessionAsync(user, session);
            await store.SaveCompletedSessionAsync(user, session);

            Assert.Single(await store.GetSessionsAsync(user));
            var pending = Assert.Single(await store.GetPendingOutboxAsync(user, 10, DateTimeOffset.UtcNow.AddMinutes(1)));
            Assert.Equal(session.SessionId, pending.SessionId);
            await store.MarkOutboxSucceededAsync(user, session.SessionId);
            Assert.Empty(await store.GetPendingOutboxAsync(user, 10, DateTimeOffset.UtcNow.AddMinutes(1)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DesktopStore_CreatesOneRecoverableBackupBeforeV2SchemaMigration()
    {
        var root = Path.Combine(Path.GetTempPath(), "gibdd-v2-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var database = Path.Combine(root, "legacy.db");
            await CreateLegacyDatabaseAsync(database, includeTrainingAggregate: false);
            var store = new DesktopStudyStore(database);

            await store.InitializeAsync();
            var originalBackupTime = File.GetLastWriteTimeUtc(store.BackupPath);
            await store.InitializeAsync();

            Assert.True(File.Exists(store.BackupPath));
            Assert.Equal(originalBackupTime, File.GetLastWriteTimeUtc(store.BackupPath));
            await using var backup = new SqliteConnection($"Data Source={store.BackupPath};Mode=ReadOnly;Pooling=False");
            await backup.OpenAsync();
            var read = backup.CreateCommand();
            read.CommandText = "SELECT value FROM legacy_marker LIMIT 1;";
            Assert.Equal("before-v2", await read.ExecuteScalarAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyTrainingMigration_IsIdempotentAndNeverInventsAnswerEvents()
    {
        var root = Path.Combine(Path.GetTempPath(), "gibdd-v2-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var database = Path.Combine(root, "legacy.db");
            await CreateLegacyDatabaseAsync(database, includeTrainingAggregate: true);
            var store = new DesktopStudyStore(database);
            await store.InitializeAsync();
            var userId = Guid.NewGuid();
            var deviceId = Guid.NewGuid();

            var first = await store.MigrateLegacyAsync(
                userId, deviceId, "test-ab", new string('A', 64), "test-rules");
            var second = await store.MigrateLegacyAsync(
                userId, deviceId, "test-ab", new string('A', 64), "test-rules");

            Assert.Equal(1, first.LegacyTrainingQuestionsImported);
            Assert.False(first.AlreadyApplied);
            Assert.True(second.AlreadyApplied);
            var session = Assert.Single(await store.GetSessionsAsync(userId));
            Assert.Equal(StudyMode.LegacyImport, session.Mode);
            Assert.Empty(session.Answers);
            Assert.Single(session.LegacyAggregates);
            Assert.Single(await store.GetPendingOutboxAsync(userId, 10, DateTimeOffset.UtcNow.AddMinutes(1)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyMigrationLedger_FollowsSessionsWhenAnonymousScopeIsLinked()
    {
        var root = Path.Combine(Path.GetTempPath(), "gibdd-v2-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var database = Path.Combine(root, "legacy.db");
            await CreateLegacyDatabaseAsync(database, includeTrainingAggregate: true);
            var store = new DesktopStudyStore(database);
            await store.InitializeAsync();
            var deviceScope = Guid.NewGuid();
            var linkedUserScope = Guid.NewGuid();
            var deviceId = Guid.NewGuid();

            var first = await store.MigrateLegacyAsync(
                deviceScope, deviceId, "test-ab", new string('A', 64), "test-rules");
            await store.MergeUserScopeAsync(deviceScope, linkedUserScope);
            var afterLink = await store.MigrateLegacyAsync(
                linkedUserScope, deviceId, "test-ab", new string('A', 64), "test-rules");

            Assert.False(first.AlreadyApplied);
            Assert.True(afterLink.AlreadyApplied);
            Assert.Empty(await store.GetSessionsAsync(deviceScope));
            Assert.Single(await store.GetSessionsAsync(linkedUserScope));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LearningProfile_IsOrderIndependentAndDeduplicatesIdenticalSession()
    {
        var first = CreateSession(StudyDeviceKind.WindowsDesktop, isCorrect: false);
        var second = CreateSession(StudyDeviceKind.AndroidApp, isCorrect: true) with
        {
            SessionId = Guid.NewGuid(),
            StartedAtUtc = first.StartedAtUtc.AddHours(1),
            CompletedAtUtc = first.CompletedAtUtc.AddHours(1),
            PayloadSha256 = string.Empty
        };
        second = second.WithComputedHash();
        var builder = new LearningProfileBuilder();

        var left = builder.Build([first, second, first], second.CompletedAtUtc.AddMinutes(1));
        var right = builder.Build([second, first], second.CompletedAtUtc.AddMinutes(1));

        var leftQuestion = left.GetQuestion(1);
        var rightQuestion = right.GetQuestion(1);
        Assert.NotNull(leftQuestion);
        Assert.NotNull(rightQuestion);
        Assert.Equal(2, leftQuestion.ExposureCount);
        Assert.Equal(1, leftQuestion.ErrorCount);
        Assert.True(leftQuestion.LastAnswerWasCorrect);
        Assert.Equal(leftQuestion, rightQuestion);
    }

    [Fact]
    public void LearningProfile_IgnoresUnansweredQuestionsFromEarlyTerminatedExam()
    {
        var answered = CreateSession(StudyDeviceKind.WindowsDesktop, isCorrect: false);
        var unanswered = answered.Answers[0] with
        {
            SequenceNumber = 2,
            QuestionId = 2,
            QuestionNumber = 2,
            SelectedAnswer = null,
            IsCorrect = false,
            ResponseTimeMs = 0,
            AnsweredAtUtc = null
        };
        var session = (answered with
        {
            OrderedQuestionIds = [1, 2],
            Answers = [answered.Answers[0], unanswered],
            PayloadSha256 = string.Empty
        }).WithComputedHash();

        var profile = new LearningProfileBuilder().Build([session], session.CompletedAtUtc.AddMinutes(1));

        Assert.Equal(1, profile.GetQuestion(1)!.ExposureCount);
        Assert.Null(profile.GetQuestion(2));
    }

    [Fact]
    public async Task CanonicalQuestionBank_HasOnlyAbAndValidJpegs()
    {
        var repository = FindRepositoryRoot();
        var package = await new QuestionBankPackageLoader().LoadAsync(
            Path.Combine(repository, "assets", "question-bank", "ab"));

        Assert.Equal(800, package.Questions.Count);
        Assert.Equal(40, package.Questions.Select(question => question.TicketNumber).Distinct().Count());
        Assert.Equal(548, package.Questions.Where(question => question.ImagePath is not null)
            .Select(question => question.ImagePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(package.Questions, question => Assert.Equal("AB", question.Category));
    }

    [Fact]
    public void ClientSource_DoesNotContainTelegramBotTokenShape()
    {
        var repository = FindRepositoryRoot();
        var tokenPattern = new Regex(@"\b\d{8,12}:[A-Za-z0-9_-]{30,}\b", RegexOptions.CultureInvariant);
        var clientRoots = new[]
        {
            Path.Combine(repository, "src", "GibddExamSimulator.App"),
            Path.Combine(repository, "src", "GibddExamSimulator.Sync"),
            Path.Combine(repository, "src", "GibddExamSimulator.Web"),
            Path.Combine(repository, "src", "GibddExamSimulator.Mobile.Shared"),
            Path.Combine(repository, "src", "GibddExamSimulator.Android")
        };
        foreach (var file in clientRoots.SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                    !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                     .Where(path => new[] { ".cs", ".razor", ".js", ".json", ".xaml", ".html" }
                         .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
        {
            Assert.DoesNotMatch(tokenPattern, File.ReadAllText(file));
        }
    }

    private static StudySessionEnvelope CreateSession(StudyDeviceKind kind, bool isCorrect = true)
    {
        var started = DateTimeOffset.Parse("2026-08-31T10:00:00Z");
        return new StudySessionEnvelope
        {
            SessionId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            DeviceKind = kind,
            Mode = StudyMode.Exam,
            StartedAtUtc = started,
            CompletedAtUtc = started.AddMinutes(1),
            Outcome = isCorrect ? StudyOutcome.Passed : StudyOutcome.Failed,
            BankVersion = "test-ab",
            BankSha256 = new string('A', 64),
            RulesProfile = "test-rules",
            OrderedQuestionIds = [1],
            Answers =
            [
                new StudyAnswerEvent
                {
                    SequenceNumber = 1,
                    QuestionId = 1,
                    TicketNumber = 1,
                    QuestionNumber = 1,
                    GroupId = 1,
                    ThematicBlockId = 1,
                    Stage = StudyStage.Main,
                    SelectedAnswer = isCorrect ? 1 : 2,
                    CorrectAnswer = 1,
                    IsCorrect = isCorrect,
                    ResponseTimeMs = 5000,
                    AnsweredAtUtc = started.AddSeconds(5)
                }
            ],
            Summary = new StudySessionSummary
            {
                QuestionCount = 1,
                AnsweredCount = 1,
                CorrectCount = isCorrect ? 1 : 0,
                ErrorCount = isCorrect ? 0 : 1,
                ElapsedMs = 5000,
                LongestCorrectStreak = isCorrect ? 1 : 0
            }
        }.WithComputedHash();
    }

    private static async Task CreateLegacyDatabaseAsync(string path, bool includeTrainingAggregate)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE legacy_marker(value TEXT NOT NULL); INSERT INTO legacy_marker(value) VALUES ('before-v2');";
        await command.ExecuteNonQueryAsync();
        if (!includeTrainingAggregate)
            return;
        command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE training_question_stats (
                question_id INTEGER NOT NULL,
                category TEXT NOT NULL,
                group_id INTEGER NOT NULL,
                attempts INTEGER NOT NULL,
                correct_answers INTEGER NOT NULL,
                total_answer_ms INTEGER NOT NULL,
                last_attempt_utc TEXT NOT NULL
            );
            INSERT INTO training_question_stats (
                question_id, category, group_id, attempts, correct_answers,
                total_answer_ms, last_attempt_utc)
            VALUES (17, 'AB', 2, 4, 1, 44000, '2026-08-31T10:00:00+00:00');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GibddExamSimulator.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
