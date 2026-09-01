using GibddExamSimulator.Application.Learning;
using GibddExamSimulator.Application.StudySessions;

namespace GibddExamSimulator.Application.Storage;

public sealed record AuthSession(
    Guid UserId,
    string Email,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc)
{
    public bool NeedsRefresh(DateTimeOffset nowUtc) => ExpiresAtUtc <= nowUtc.AddMinutes(2);
}

public sealed record StudyOutboxItem(
    Guid SessionId,
    StudySessionEnvelope Session,
    int AttemptCount,
    DateTimeOffset NextAttemptAtUtc,
    string LastError);

public sealed record RemoteStudySession(long ServerSequence, StudySessionEnvelope Session);

public sealed record RemoteStudyPage(IReadOnlyList<RemoteStudySession> Items, bool HasMore);

public enum UploadDisposition
{
    Uploaded,
    AlreadyExists,
    IntegrityConflict
}

public sealed record UploadResult(UploadDisposition Disposition, string Message = "");

public interface ILocalStudyStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<Guid> GetOrCreateDeviceIdAsync(CancellationToken cancellationToken = default);
    Task SaveCompletedSessionAsync(Guid userId, StudySessionEnvelope session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StudySessionEnvelope>> GetSessionsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StudyOutboxItem>> GetPendingOutboxAsync(Guid userId, int limit, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
    Task MarkOutboxSucceededAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
    Task MarkOutboxFailedAsync(Guid userId, Guid sessionId, int previousAttemptCount, string error, DateTimeOffset nextAttemptAtUtc, CancellationToken cancellationToken = default);
    Task<long> GetServerCursorAsync(Guid userId, CancellationToken cancellationToken = default);
    Task ApplyRemotePageAsync(Guid userId, IReadOnlyList<RemoteStudySession> items, long newCursor, CancellationToken cancellationToken = default);
    Task SaveDraftAsync(Guid userId, ActiveSessionDraft draft, CancellationToken cancellationToken = default);
    Task<ActiveSessionDraft?> GetDraftAsync(Guid userId, CancellationToken cancellationToken = default);
    Task DeleteDraftAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SaveLearningProfileAsync(Guid userId, LearningProfile profile, CancellationToken cancellationToken = default);
    Task<LearningProfile?> GetLearningProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SetLastSuccessfulSyncAsync(Guid userId, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default);
    Task<DateTimeOffset?> GetLastSuccessfulSyncAsync(Guid userId, CancellationToken cancellationToken = default);
}

public interface IAuthSessionStore
{
    Task<AuthSession?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AuthSession session, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed record LegacyMigrationResult(
    bool BackupCreated,
    int ExamSessionsImported,
    int LegacyTrainingQuestionsImported,
    bool AlreadyApplied);

public interface ILegacyStudyMigration
{
    Task<LegacyMigrationResult> MigrateLegacyAsync(
        Guid userId,
        Guid deviceId,
        string bankVersion,
        string bankSha256,
        string rulesProfile,
        CancellationToken cancellationToken = default);
}

public interface IAuthClient
{
    Task<AuthSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<AuthSession> RefreshAsync(AuthSession session, CancellationToken cancellationToken = default);
    Task SignOutAsync(AuthSession session, CancellationToken cancellationToken = default);
}

public interface IStudySessionRemote
{
    Task<UploadResult> UploadAsync(AuthSession auth, StudySessionEnvelope session, CancellationToken cancellationToken = default);
    Task<RemoteStudyPage> PullAsync(AuthSession auth, long afterServerSequence, int pageSize, CancellationToken cancellationToken = default);
}
