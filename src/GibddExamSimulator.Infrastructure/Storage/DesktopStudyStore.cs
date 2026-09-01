using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GibddExamSimulator.Application.Learning;
using GibddExamSimulator.Application.Storage;
using GibddExamSimulator.Application.StudySessions;
using GibddExamSimulator.Models;
using Microsoft.Data.Sqlite;

namespace GibddExamSimulator.Infrastructure.Storage;

public sealed class DesktopStudyStore : ILocalStudyStore, ILegacyStudyMigration, ILocalUserScopeMigration
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _databasePath;
    private bool _backupCreatedDuringInitialization;

    public DesktopStudyStore(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    public string BackupPath => Path.Combine(
        Path.GetDirectoryName(_databasePath) ?? string.Empty,
        Path.GetFileNameWithoutExtension(_databasePath) + ".pre-v2.backup.db");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_databasePath) && new FileInfo(_databasePath).Length > 0 &&
            !await ContainsV2SchemaAsync(cancellationToken))
        {
            _backupCreatedDuringInitialization = await CreateBackupIfMissingAsync(cancellationToken);
        }

        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;

            CREATE TABLE IF NOT EXISTS study_sessions_local (
                user_id TEXT NOT NULL,
                session_id TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                payload_sha256 TEXT NOT NULL,
                server_seq INTEGER NULL,
                origin TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                PRIMARY KEY(user_id, session_id)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_study_sessions_user_server_seq
                ON study_sessions_local(user_id, server_seq)
                WHERE server_seq IS NOT NULL;

            CREATE TABLE IF NOT EXISTS study_outbox (
                user_id TEXT NOT NULL,
                session_id TEXT NOT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0,
                next_attempt_utc TEXT NOT NULL,
                last_error TEXT NOT NULL DEFAULT '',
                created_at_utc TEXT NOT NULL,
                PRIMARY KEY(user_id, session_id),
                FOREIGN KEY(user_id, session_id)
                    REFERENCES study_sessions_local(user_id, session_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_study_outbox_due
                ON study_outbox(user_id, next_attempt_utc, created_at_utc);

            CREATE TABLE IF NOT EXISTS study_sync_state (
                user_id TEXT PRIMARY KEY,
                server_cursor INTEGER NOT NULL DEFAULT 0,
                last_successful_sync_utc TEXT NULL,
                profile_id TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS active_study_drafts (
                user_id TEXT PRIMARY KEY,
                payload_json TEXT NOT NULL,
                saved_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS learning_profile_cache (
                user_id TEXT PRIMARY KEY,
                calculated_at_utc TEXT NOT NULL,
                payload_json TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS v2_app_metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS v2_migration_ledger (
                user_id TEXT NOT NULL,
                migration_key TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL,
                PRIMARY KEY(user_id, migration_key)
            );

            DROP TABLE IF EXISTS telegram_outbox;
            DROP TABLE IF EXISTS telegram_recipients;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        var profileColumn = connection.CreateCommand();
        profileColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('study_sync_state') WHERE name='profile_id';";
        if (Convert.ToInt32(await profileColumn.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 0)
        {
            var addProfileColumn = connection.CreateCommand();
            addProfileColumn.CommandText = "ALTER TABLE study_sync_state ADD COLUMN profile_id TEXT NULL;";
            await addProfileColumn.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<Guid> GetOrCreateDeviceIdAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var read = connection.CreateCommand();
        read.CommandText = "SELECT value FROM v2_app_metadata WHERE key='device_id';";
        var existing = await read.ExecuteScalarAsync(cancellationToken) as string;
        if (Guid.TryParse(existing, out var deviceId) && deviceId != Guid.Empty)
            return deviceId;

        deviceId = Guid.NewGuid();
        var write = connection.CreateCommand();
        write.CommandText = """
            INSERT INTO v2_app_metadata(key, value) VALUES ('device_id', $value)
            ON CONFLICT(key) DO NOTHING;
            """;
        write.Parameters.AddWithValue("$value", deviceId.ToString("D"));
        await write.ExecuteNonQueryAsync(cancellationToken);
        existing = await read.ExecuteScalarAsync(cancellationToken) as string;
        return Guid.TryParse(existing, out var persisted) ? persisted : deviceId;
    }

    public async Task MergeUserScopeAsync(
        Guid sourceUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (sourceUserId == Guid.Empty || targetUserId == Guid.Empty || sourceUserId == targetUserId)
            return;
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
              FROM study_sessions_local source
              JOIN study_sessions_local target
                ON target.user_id=$target AND target.session_id=source.session_id
             WHERE source.user_id=$source
               AND target.payload_sha256 <> source.payload_sha256;
            """;
        command.Parameters.AddWithValue("$source", sourceUserId.ToString("D"));
        command.Parameters.AddWithValue("$target", targetUserId.ToString("D"));
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 0)
            throw new InvalidDataException("Local history contains a session integrity conflict.");

        command.CommandText = """
            INSERT OR IGNORE INTO study_sessions_local(
                user_id, session_id, payload_json, payload_sha256, server_seq, origin, created_at_utc)
            SELECT $target, session_id, payload_json, payload_sha256, server_seq, origin, created_at_utc
              FROM study_sessions_local WHERE user_id=$source;

            INSERT OR IGNORE INTO study_outbox(
                user_id, session_id, attempt_count, next_attempt_utc, last_error, created_at_utc)
            SELECT $target, session_id, attempt_count, next_attempt_utc, last_error, created_at_utc
              FROM study_outbox WHERE user_id=$source;

            INSERT OR IGNORE INTO active_study_drafts(user_id, payload_json, saved_at_utc)
            SELECT $target, payload_json, saved_at_utc FROM active_study_drafts WHERE user_id=$source;

            INSERT OR IGNORE INTO learning_profile_cache(user_id, calculated_at_utc, payload_json)
            SELECT $target, calculated_at_utc, payload_json FROM learning_profile_cache WHERE user_id=$source;

            INSERT OR IGNORE INTO v2_migration_ledger(user_id, migration_key, applied_at_utc)
            SELECT $target, migration_key, applied_at_utc
              FROM v2_migration_ledger WHERE user_id=$source;

            DELETE FROM study_outbox WHERE user_id=$source;
            DELETE FROM study_sessions_local WHERE user_id=$source;
            DELETE FROM active_study_drafts WHERE user_id=$source;
            DELETE FROM learning_profile_cache WHERE user_id=$source;
            DELETE FROM study_sync_state WHERE user_id=$source;
            DELETE FROM v2_migration_ledger WHERE user_id=$source;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveCompletedSessionAsync(
        Guid userId,
        StudySessionEnvelope original,
        CancellationToken cancellationToken = default)
    {
        var session = Normalize(original);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await InsertSessionAsync(connection, transaction, userId, session, serverSequence: null, "local", cancellationToken);
        var outbox = connection.CreateCommand();
        outbox.Transaction = transaction;
        outbox.CommandText = """
            INSERT INTO study_outbox (
                user_id, session_id, attempt_count, next_attempt_utc, last_error, created_at_utc)
            VALUES ($user, $session, 0, $now, '', $now)
            ON CONFLICT(user_id, session_id) DO NOTHING;
            """;
        outbox.Parameters.AddWithValue("$user", userId.ToString("D"));
        outbox.Parameters.AddWithValue("$session", session.SessionId.ToString("D"));
        outbox.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await outbox.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StudySessionEnvelope>> GetSessionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var result = new List<StudySessionEnvelope>();
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload_json
            FROM study_sessions_local
            WHERE user_id=$user
            ORDER BY created_at_utc, session_id;
            """;
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(DeserializeSession(reader.GetString(0)));
        return result;
    }

    public async Task<IReadOnlyList<StudyOutboxItem>> GetPendingOutboxAsync(
        Guid userId,
        int limit,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var result = new List<StudyOutboxItem>();
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT outbox.session_id, session.payload_json, outbox.attempt_count,
                   outbox.next_attempt_utc, outbox.last_error
            FROM study_outbox outbox
            JOIN study_sessions_local session
              ON session.user_id=outbox.user_id AND session.session_id=outbox.session_id
            WHERE outbox.user_id=$user AND outbox.next_attempt_utc <= $now
            ORDER BY outbox.created_at_utc, outbox.session_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        command.Parameters.AddWithValue("$now", nowUtc.ToString("O"));
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new StudyOutboxItem(
                Guid.Parse(reader.GetString(0)),
                DeserializeSession(reader.GetString(1)),
                reader.GetInt32(2),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                reader.GetString(4)));
        }
        return result;
    }

    public async Task MarkOutboxSucceededAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM study_outbox WHERE user_id=$user AND session_id=$session;";
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        command.Parameters.AddWithValue("$session", sessionId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkOutboxFailedAsync(
        Guid userId,
        Guid sessionId,
        int previousAttemptCount,
        string error,
        DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE study_outbox
            SET attempt_count=$attempts, next_attempt_utc=$next, last_error=$error
            WHERE user_id=$user AND session_id=$session;
            """;
        command.Parameters.AddWithValue("$attempts", previousAttemptCount + 1);
        command.Parameters.AddWithValue("$next", nextAttemptAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$error", (error ?? string.Empty)[..Math.Min(error?.Length ?? 0, 500)]);
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        command.Parameters.AddWithValue("$session", sessionId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task PrepareProfileSyncAsync(
        Guid userId,
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || profileId == Guid.Empty)
            throw new ArgumentException("User and profile identifiers must not be empty.");

        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO study_sync_state(user_id, server_cursor, last_successful_sync_utc, profile_id)
            VALUES ($user, 0, NULL, $profile)
            ON CONFLICT(user_id) DO UPDATE SET
                server_cursor=CASE
                    WHEN study_sync_state.profile_id IS NULL OR study_sync_state.profile_id <> excluded.profile_id
                    THEN 0 ELSE study_sync_state.server_cursor END,
                last_successful_sync_utc=CASE
                    WHEN study_sync_state.profile_id IS NULL OR study_sync_state.profile_id <> excluded.profile_id
                    THEN NULL ELSE study_sync_state.last_successful_sync_utc END,
                profile_id=excluded.profile_id;
            """;
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        command.Parameters.AddWithValue("$profile", profileId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> GetServerCursorAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(server_cursor,0) FROM study_sync_state WHERE user_id=$user;";
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public async Task ApplyRemotePageAsync(
        Guid userId,
        IReadOnlyList<RemoteStudySession> items,
        long newCursor,
        CancellationToken cancellationToken = default)
    {
        if (items.Any(item => item.ServerSequence > newCursor))
            throw new InvalidDataException("Remote cursor is behind a supplied session.");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var item in items.OrderBy(item => item.ServerSequence))
            await InsertSessionAsync(
                connection,
                transaction,
                userId,
                Normalize(item.Session),
                item.ServerSequence,
                "remote",
                cancellationToken);

        var cursor = connection.CreateCommand();
        cursor.Transaction = transaction;
        cursor.CommandText = """
            INSERT INTO study_sync_state(user_id, server_cursor)
            VALUES ($user, $cursor)
            ON CONFLICT(user_id) DO UPDATE SET
                server_cursor=MAX(study_sync_state.server_cursor, excluded.server_cursor);
            """;
        cursor.Parameters.AddWithValue("$user", userId.ToString("D"));
        cursor.Parameters.AddWithValue("$cursor", newCursor);
        await cursor.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveDraftAsync(Guid userId, ActiveSessionDraft draft, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO active_study_drafts(user_id, payload_json, saved_at_utc)
            VALUES ($user, $payload, $saved)
            ON CONFLICT(user_id) DO UPDATE SET payload_json=excluded.payload_json, saved_at_utc=excluded.saved_at_utc;
            """;
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(draft, JsonOptions));
        command.Parameters.AddWithValue("$saved", draft.SavedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ActiveSessionDraft?> GetDraftAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM active_study_drafts WHERE user_id=$user;";
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return payload is null ? null : JsonSerializer.Deserialize<ActiveSessionDraft>(payload, JsonOptions);
    }

    public async Task DeleteDraftAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM active_study_drafts WHERE user_id=$user;";
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveLearningProfileAsync(
        Guid userId,
        LearningProfile profile,
        CancellationToken cancellationToken = default)
    {
        var cache = new ProfileCache(profile.CalculatedAtUtc, profile.Questions.Values.OrderBy(item => item.QuestionId).ToArray());
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO learning_profile_cache(user_id, calculated_at_utc, payload_json)
            VALUES ($user, $calculated, $payload)
            ON CONFLICT(user_id) DO UPDATE SET
                calculated_at_utc=excluded.calculated_at_utc, payload_json=excluded.payload_json;
            """;
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        command.Parameters.AddWithValue("$calculated", profile.CalculatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(cache, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<LearningProfile?> GetLearningProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM learning_profile_cache WHERE user_id=$user;";
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        var cache = payload is null ? null : JsonSerializer.Deserialize<ProfileCache>(payload, JsonOptions);
        return cache is null ? null : new LearningProfile(cache.CalculatedAtUtc, cache.Questions);
    }

    public async Task SetLastSuccessfulSyncAsync(
        Guid userId,
        DateTimeOffset syncedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO study_sync_state(user_id, server_cursor, last_successful_sync_utc)
            VALUES ($user, 0, $synced)
            ON CONFLICT(user_id) DO UPDATE SET last_successful_sync_utc=excluded.last_successful_sync_utc;
            """;
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        command.Parameters.AddWithValue("$synced", syncedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DateTimeOffset?> GetLastSuccessfulSyncAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT last_successful_sync_utc FROM study_sync_state WHERE user_id=$user;";
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        var value = await command.ExecuteScalarAsync(cancellationToken) as string;
        return string.IsNullOrWhiteSpace(value)
            ? null
            : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<LegacyMigrationResult> MigrateLegacyAsync(
        Guid userId,
        Guid deviceId,
        string bankVersion,
        string bankSha256,
        string rulesProfile,
        CancellationToken cancellationToken = default)
    {
        const string migrationKey = "legacy-v1-study-history";
        await using var connection = await OpenAsync(cancellationToken);
        if (await MigrationAppliedAsync(connection, userId, migrationKey, cancellationToken))
            return new LegacyMigrationResult(_backupCreatedDuringInitialization, 0, 0, true);

        var sessions = new List<StudySessionEnvelope>();
        if (await TableExistsAsync(connection, "exam_attempts", cancellationToken) &&
            await TableExistsAsync(connection, "exam_responses", cancellationToken))
        {
            sessions.AddRange(await ReadLegacyExamSessionsAsync(
                connection, deviceId, bankVersion, bankSha256, rulesProfile, cancellationToken));
        }

        var aggregateCount = 0;
        if (await TableExistsAsync(connection, "training_question_stats", cancellationToken))
        {
            var legacy = await ReadLegacyTrainingSessionAsync(
                connection, userId, deviceId, bankVersion, bankSha256, rulesProfile, cancellationToken);
            if (legacy is not null)
            {
                aggregateCount = legacy.LegacyAggregates.Count;
                sessions.Add(legacy);
            }
        }

        foreach (var session in sessions)
            await SaveCompletedSessionAsync(userId, session, cancellationToken);

        var ledger = connection.CreateCommand();
        ledger.CommandText = """
            INSERT INTO v2_migration_ledger(user_id, migration_key, applied_at_utc)
            VALUES ($user, $key, $now)
            ON CONFLICT(user_id, migration_key) DO NOTHING;
            """;
        ledger.Parameters.AddWithValue("$user", userId.ToString("D"));
        ledger.Parameters.AddWithValue("$key", migrationKey);
        ledger.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await ledger.ExecuteNonQueryAsync(cancellationToken);
        return new LegacyMigrationResult(
            _backupCreatedDuringInitialization,
            sessions.Count(session => session.Mode == StudyMode.Exam),
            aggregateCount,
            false);
    }

    private async Task<IReadOnlyList<StudySessionEnvelope>> ReadLegacyExamSessionsAsync(
        SqliteConnection connection,
        Guid deviceId,
        string bankVersion,
        string bankSha256,
        string fallbackRulesProfile,
        CancellationToken cancellationToken)
    {
        var sessions = new List<StudySessionEnvelope>();
        var attempts = connection.CreateCommand();
        attempts.CommandText = """
            SELECT id, started_at_utc, ended_at_utc, status, outcome, elapsed_seconds, rules_profile
            FROM exam_attempts
            WHERE mode='Exam' AND category IN ('AB','A/B','ABM','A/B/M')
              AND ended_at_utc IS NOT NULL
              AND status IN ('Passed','Failed','Interrupted')
            ORDER BY started_at_utc, id;
            """;
        var rows = new List<LegacyAttempt>();
        await using (var reader = await attempts.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!Guid.TryParse(reader.GetString(0), out var sessionId))
                    continue;
                rows.Add(new LegacyAttempt(
                    sessionId,
                    DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                    DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt64(5),
                    reader.IsDBNull(6) ? fallbackRulesProfile : reader.GetString(6)));
            }
        }

        foreach (var attempt in rows)
        {
            var responses = connection.CreateCommand();
            responses.CommandText = """
                SELECT stage, sequence_number, snapshot_json, selected_answer, correct_answer,
                       is_correct, answer_time_ms, answered_at_utc
                FROM exam_responses
                WHERE attempt_id=$attempt
                ORDER BY CASE stage WHEN 'Main' THEN 0 ELSE 1 END, sequence_number;
                """;
            responses.Parameters.AddWithValue("$attempt", attempt.SessionId.ToString("D"));
            var answers = new List<StudyAnswerEvent>();
            await using var reader = await responses.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var question = JsonSerializer.Deserialize<Question>(reader.GetString(2), JsonOptions);
                if (question is null || question.Id is < 1 or > 800)
                    continue;
                answers.Add(new StudyAnswerEvent
                {
                    SequenceNumber = answers.Count + 1,
                    QuestionId = question.Id,
                    TicketNumber = question.TicketNumber,
                    QuestionNumber = question.QuestionNumber,
                    GroupId = question.GroupId,
                    ThematicBlockId = question.ThematicBlockId,
                    Stage = reader.GetString(0) == "Supplementary" ? StudyStage.Supplementary : StudyStage.Main,
                    SelectedAnswer = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    CorrectAnswer = reader.GetInt32(4),
                    IsCorrect = !reader.IsDBNull(5) && reader.GetInt32(5) == 1,
                    ResponseTimeMs = reader.IsDBNull(6) ? 0 : Math.Max(0, reader.GetInt64(6)),
                    AnsweredAtUtc = reader.IsDBNull(7)
                        ? null
                        : DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture)
                });
            }
            if (answers.Count == 0)
                continue;
            var answered = answers.Where(answer => answer.SelectedAnswer.HasValue).ToArray();
            sessions.Add(new StudySessionEnvelope
            {
                SessionId = attempt.SessionId,
                DeviceId = deviceId,
                DeviceKind = StudyDeviceKind.WindowsDesktop,
                Mode = StudyMode.Exam,
                StartedAtUtc = attempt.StartedAtUtc,
                CompletedAtUtc = attempt.EndedAtUtc,
                Outcome = attempt.Outcome switch
                {
                    "Passed" => StudyOutcome.Passed,
                    "Failed" => StudyOutcome.Failed,
                    _ => StudyOutcome.Abandoned
                },
                BankVersion = bankVersion,
                BankSha256 = bankSha256,
                RulesProfile = string.IsNullOrWhiteSpace(attempt.RulesProfile) ? fallbackRulesProfile : attempt.RulesProfile,
                OrderedQuestionIds = answers.Select(answer => answer.QuestionId).Distinct().ToArray(),
                Answers = answers,
                Summary = new StudySessionSummary
                {
                    QuestionCount = answers.Count,
                    AnsweredCount = answered.Length,
                    CorrectCount = answered.Count(answer => answer.IsCorrect),
                    ErrorCount = answered.Count(answer => !answer.IsCorrect),
                    ElapsedMs = Math.Max(0, attempt.ElapsedSeconds * 1000),
                    LongestCorrectStreak = LongestCorrectStreak(answers)
                }
            }.WithComputedHash());
        }
        return sessions;
    }

    private async Task<StudySessionEnvelope?> ReadLegacyTrainingSessionAsync(
        SqliteConnection connection,
        Guid userId,
        Guid deviceId,
        string bankVersion,
        string bankSha256,
        string rulesProfile,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT question_id, group_id, attempts, correct_answers, total_answer_ms, last_attempt_utc
            FROM training_question_stats
            WHERE category IN ('AB','A/B','ABM','A/B/M') AND question_id BETWEEN 1 AND 800
            ORDER BY question_id;
            """;
        var aggregates = new List<LegacyQuestionAggregate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            aggregates.Add(new LegacyQuestionAggregate
            {
                QuestionId = reader.GetInt64(0),
                GroupId = reader.GetInt32(1),
                AttemptCount = Math.Max(0, reader.GetInt32(2)),
                CorrectCount = Math.Max(0, reader.GetInt32(3)),
                TotalResponseTimeMs = Math.Max(0, reader.GetInt64(4)),
                LastAttemptAtUtc = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture)
            });
        }
        if (aggregates.Count == 0)
            return null;
        var completedAt = aggregates.Max(item => item.LastAttemptAtUtc);
        var startedAt = aggregates.Min(item => item.LastAttemptAtUtc);
        return new StudySessionEnvelope
        {
            SessionId = DeterministicGuid(userId, "legacy-training-v1"),
            DeviceId = deviceId,
            DeviceKind = StudyDeviceKind.WindowsDesktop,
            Mode = StudyMode.LegacyImport,
            StartedAtUtc = startedAt,
            CompletedAtUtc = completedAt,
            Outcome = StudyOutcome.Completed,
            BankVersion = bankVersion,
            BankSha256 = bankSha256,
            RulesProfile = rulesProfile,
            OrderedQuestionIds = aggregates.Select(item => item.QuestionId).ToArray(),
            Answers = [],
            LegacyAggregates = aggregates,
            Summary = new StudySessionSummary
            {
                QuestionCount = aggregates.Count,
                AnsweredCount = aggregates.Sum(item => item.AttemptCount),
                CorrectCount = aggregates.Sum(item => item.CorrectCount),
                ErrorCount = aggregates.Sum(item => item.AttemptCount - item.CorrectCount),
                ElapsedMs = aggregates.Sum(item => item.TotalResponseTimeMs),
                LongestCorrectStreak = 0
            }
        }.WithComputedHash();
    }

    private async Task InsertSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid userId,
        StudySessionEnvelope session,
        long? serverSequence,
        string origin,
        CancellationToken cancellationToken)
    {
        var existing = connection.CreateCommand();
        existing.Transaction = transaction;
        existing.CommandText = """
            SELECT payload_sha256 FROM study_sessions_local
            WHERE user_id=$user AND session_id=$session;
            """;
        existing.Parameters.AddWithValue("$user", userId.ToString("D"));
        existing.Parameters.AddWithValue("$session", session.SessionId.ToString("D"));
        var existingHash = await existing.ExecuteScalarAsync(cancellationToken) as string;
        if (existingHash is not null)
        {
            if (!string.Equals(existingHash, session.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                throw new StudySessionIntegrityException(session.SessionId);
            if (serverSequence.HasValue)
            {
                var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE study_sessions_local SET server_seq=COALESCE(server_seq,$server)
                    WHERE user_id=$user AND session_id=$session;
                    """;
                update.Parameters.AddWithValue("$server", serverSequence.Value);
                update.Parameters.AddWithValue("$user", userId.ToString("D"));
                update.Parameters.AddWithValue("$session", session.SessionId.ToString("D"));
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            return;
        }

        var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO study_sessions_local (
                user_id, session_id, payload_json, payload_sha256, server_seq, origin, created_at_utc)
            VALUES ($user, $session, $payload, $hash, $server, $origin, $created);
            """;
        insert.Parameters.AddWithValue("$user", userId.ToString("D"));
        insert.Parameters.AddWithValue("$session", session.SessionId.ToString("D"));
        insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(session, JsonOptions));
        insert.Parameters.AddWithValue("$hash", session.PayloadSha256);
        insert.Parameters.AddWithValue("$server", serverSequence.HasValue ? serverSequence.Value : DBNull.Value);
        insert.Parameters.AddWithValue("$origin", origin);
        insert.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> ContainsV2SchemaAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            return await TableExistsAsync(connection, "study_sessions_local", cancellationToken);
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private async Task<bool> CreateBackupIfMissingAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(BackupPath))
            return false;
        await using var source = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly;Pooling=False");
        await source.OpenAsync(cancellationToken);
        await using var destination = new SqliteConnection($"Data Source={BackupPath};Pooling=False");
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
        return true;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        // Pooling would keep study.db open after a completed operation. Besides making
        // backups and cleanup unreliable on Windows, that also blocks a clean app update.
        var connection = new SqliteConnection(
            $"Data Source={_databasePath};Cache=Shared;Foreign Keys=True;Pooling=False");
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<bool> MigrationAppliedAsync(
        SqliteConnection connection,
        Guid userId,
        string migrationKey,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM v2_migration_ledger WHERE user_id=$user AND migration_key=$key;
            """;
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        command.Parameters.AddWithValue("$key", migrationKey);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
    }

    private static StudySessionEnvelope Normalize(StudySessionEnvelope session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var normalized = string.IsNullOrWhiteSpace(session.PayloadSha256) ? session.WithComputedHash() : session;
        normalized.Validate();
        return normalized;
    }

    private static StudySessionEnvelope DeserializeSession(string payload)
    {
        var session = JsonSerializer.Deserialize<StudySessionEnvelope>(payload, JsonOptions)
                      ?? throw new InvalidDataException("A local study-session payload is empty.");
        session.Validate();
        return session;
    }

    private static int LongestCorrectStreak(IEnumerable<StudyAnswerEvent> answers)
    {
        var longest = 0;
        var current = 0;
        foreach (var answer in answers.Where(answer => answer.SelectedAnswer.HasValue))
        {
            current = answer.IsCorrect ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }
        return longest;
    }

    private static Guid DeterministicGuid(Guid namespaceId, string value)
    {
        var payload = Encoding.UTF8.GetBytes(namespaceId.ToString("N") + ":" + value);
        var hash = SHA256.HashData(payload);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16));
    }

    private sealed record ProfileCache(
        DateTimeOffset CalculatedAtUtc,
        IReadOnlyList<LearningQuestionProfile> Questions);

    private sealed record LegacyAttempt(
        Guid SessionId,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset EndedAtUtc,
        string Status,
        string Outcome,
        long ElapsedSeconds,
        string RulesProfile);
}
