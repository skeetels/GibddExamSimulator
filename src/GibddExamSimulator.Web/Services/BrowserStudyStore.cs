using GibddExamSimulator.Application.Learning;
using GibddExamSimulator.Application.Storage;
using GibddExamSimulator.Application.StudySessions;
using GibddExamSimulator.Application.Synchronization;
using Microsoft.JSInterop;

namespace GibddExamSimulator.Web.Services;

public sealed class BrowserStudyStore(IJSRuntime javascript) :
    ILocalStudyStore,
    IAuthSessionStore,
    IDeviceLinkStateStore,
    ILocalUserScopeMigration
{
    private const string DeviceKey = "device-id";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var initialized = await javascript.InvokeAsync<bool>(
            "gibddStorage.initialize",
            cancellationToken);
        if (!initialized)
            throw new InvalidOperationException("Локальное хранилище браузера не открылось.");
    }

    public async Task<Guid> GetOrCreateDeviceIdAsync(CancellationToken cancellationToken = default)
    {
        var value = await javascript.InvokeAsync<string?>("gibddStorage.get", cancellationToken, "meta", DeviceKey);
        if (Guid.TryParse(value, out var existing) && existing != Guid.Empty)
            return existing;
        var created = Guid.NewGuid();
        await javascript.InvokeVoidAsync("gibddStorage.put", cancellationToken, "meta", DeviceKey, created.ToString("D"));
        return created;
    }

    public async Task MergeUserScopeAsync(
        Guid sourceUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (sourceUserId == Guid.Empty || targetUserId == Guid.Empty || sourceUserId == targetUserId)
            return;
        var sessions = await GetAllAsync<SessionRecord>("sessions", cancellationToken);
        var target = sessions.Where(item => item.UserId == targetUserId).ToDictionary(item => item.SessionId);
        foreach (var source in sessions.Where(item => item.UserId == sourceUserId))
        {
            if (target.TryGetValue(source.SessionId, out var existing) &&
                !string.Equals(existing.PayloadSha256, source.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                throw new StudySessionIntegrityException(source.SessionId);
        }
        await javascript.InvokeVoidAsync(
            "gibddStorage.mergeUserScope",
            cancellationToken,
            sourceUserId.ToString("D"),
            targetUserId.ToString("D"));
    }

    public async Task SaveCompletedSessionAsync(
        Guid userId,
        StudySessionEnvelope original,
        CancellationToken cancellationToken = default)
    {
        var session = string.IsNullOrWhiteSpace(original.PayloadSha256) ? original.WithComputedHash() : original;
        session.Validate();
        var key = SessionKey(userId, session.SessionId);
        var existing = await GetSessionRecordAsync(key, cancellationToken);
        if (existing is not null && !string.Equals(existing.PayloadSha256, session.PayloadSha256, StringComparison.OrdinalIgnoreCase))
            throw new StudySessionIntegrityException(session.SessionId);
        var now = DateTimeOffset.UtcNow;
        var record = new SessionRecord(key, userId, session.SessionId, session.PayloadSha256, null, "local", now, session);
        var outbox = new OutboxRecord(key, userId, session.SessionId, 0, now, string.Empty, now, session);
        await javascript.InvokeVoidAsync(
            "gibddStorage.saveCompletedSession",
            cancellationToken,
            record,
            outbox);
    }

    public async Task<IReadOnlyList<StudySessionEnvelope>> GetSessionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var records = await GetAllAsync<SessionRecord>("sessions", cancellationToken);
        return records.Where(record => record.UserId == userId)
            .OrderBy(record => record.CreatedAtUtc)
            .ThenBy(record => record.SessionId)
            .Select(record => record.Session)
            .ToArray();
    }

    public async Task<IReadOnlyList<StudyOutboxItem>> GetPendingOutboxAsync(
        Guid userId,
        int limit,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var records = await GetAllAsync<OutboxRecord>("outbox", cancellationToken);
        return records.Where(record => record.UserId == userId && record.NextAttemptAtUtc <= nowUtc)
            .OrderBy(record => record.NextAttemptAtUtc)
            .ThenBy(record => record.CreatedAtUtc)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(record => new StudyOutboxItem(
                record.SessionId,
                record.Session,
                record.AttemptCount,
                record.NextAttemptAtUtc,
                record.LastError))
            .ToArray();
    }

    public async Task MarkOutboxSucceededAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        await javascript.InvokeVoidAsync("gibddStorage.delete", cancellationToken, "outbox", SessionKey(userId, sessionId));

    public async Task MarkOutboxFailedAsync(
        Guid userId,
        Guid sessionId,
        int previousAttemptCount,
        string error,
        DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken = default)
    {
        var key = SessionKey(userId, sessionId);
        var record = await javascript.InvokeAsync<OutboxRecord?>("gibddStorage.get", cancellationToken, "outbox", key);
        if (record is null)
            return;
        var updated = record with
        {
            AttemptCount = Math.Max(record.AttemptCount, previousAttemptCount) + 1,
            NextAttemptAtUtc = nextAttemptAtUtc,
            LastError = (error ?? string.Empty)[..Math.Min(error?.Length ?? 0, 1000)]
        };
        await javascript.InvokeVoidAsync("gibddStorage.put", cancellationToken, "outbox", key, updated);
    }

    public async Task<long> GetServerCursorAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await GetSyncRecordAsync(userId, cancellationToken))?.ServerCursor ?? 0;

    public async Task ApplyRemotePageAsync(
        Guid userId,
        IReadOnlyList<RemoteStudySession> items,
        long newCursor,
        CancellationToken cancellationToken = default)
    {
        var current = (await GetAllAsync<SessionRecord>("sessions", cancellationToken))
            .Where(record => record.UserId == userId)
            .ToDictionary(record => record.SessionId);
        var now = DateTimeOffset.UtcNow;
        var records = new List<SessionRecord>(items.Count);
        foreach (var item in items)
        {
            item.Session.Validate();
            if (current.TryGetValue(item.Session.SessionId, out var existing) &&
                !string.Equals(existing.PayloadSha256, item.Session.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                throw new StudySessionIntegrityException(item.Session.SessionId);
            records.Add(new SessionRecord(
                SessionKey(userId, item.Session.SessionId),
                userId,
                item.Session.SessionId,
                item.Session.PayloadSha256,
                item.ServerSequence,
                "remote",
                now,
                item.Session));
        }
        var previous = await GetSyncRecordAsync(userId, cancellationToken);
        var sync = new SyncRecord(userId, newCursor, previous?.LastSuccessfulSyncUtc);
        await javascript.InvokeVoidAsync(
            "gibddStorage.applyRemotePage",
            cancellationToken,
            records,
            userId.ToString("D"),
            sync);
    }

    public async Task SaveDraftAsync(Guid userId, ActiveSessionDraft draft, CancellationToken cancellationToken = default) =>
        await javascript.InvokeVoidAsync("gibddStorage.put", cancellationToken, "drafts", userId.ToString("D"), draft);

    public async Task<ActiveSessionDraft?> GetDraftAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await javascript.InvokeAsync<ActiveSessionDraft?>("gibddStorage.get", cancellationToken, "drafts", userId.ToString("D"));

    public async Task DeleteDraftAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await javascript.InvokeVoidAsync("gibddStorage.delete", cancellationToken, "drafts", userId.ToString("D"));

    public async Task SaveLearningProfileAsync(
        Guid userId,
        LearningProfile profile,
        CancellationToken cancellationToken = default)
    {
        var cache = new ProfileCache(profile.CalculatedAtUtc, profile.Questions.Values.OrderBy(item => item.QuestionId).ToArray());
        await javascript.InvokeVoidAsync("gibddStorage.put", cancellationToken, "profiles", userId.ToString("D"), cache);
    }

    public async Task<LearningProfile?> GetLearningProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var cache = await javascript.InvokeAsync<ProfileCache?>("gibddStorage.get", cancellationToken, "profiles", userId.ToString("D"));
        return cache is null ? null : new LearningProfile(cache.CalculatedAtUtc, cache.Questions);
    }

    public async Task SetLastSuccessfulSyncAsync(
        Guid userId,
        DateTimeOffset syncedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var previous = await GetSyncRecordAsync(userId, cancellationToken);
        var record = new SyncRecord(userId, previous?.ServerCursor ?? 0, syncedAtUtc);
        await javascript.InvokeVoidAsync("gibddStorage.put", cancellationToken, "sync", userId.ToString("D"), record);
    }

    public async Task<DateTimeOffset?> GetLastSuccessfulSyncAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await GetSyncRecordAsync(userId, cancellationToken))?.LastSuccessfulSyncUtc;

    public async Task<AuthSession?> LoadAsync(CancellationToken cancellationToken = default) =>
        await javascript.InvokeAsync<AuthSession?>("gibddStorage.secureGet", cancellationToken, "auth", "current");

    public async Task SaveAsync(AuthSession session, CancellationToken cancellationToken = default) =>
        await javascript.InvokeVoidAsync("gibddStorage.securePut", cancellationToken, "auth", "current", session);

    public async Task ClearAsync(CancellationToken cancellationToken = default) =>
        await javascript.InvokeVoidAsync("gibddStorage.secureDelete", cancellationToken, "auth", "current");

    async Task<DeviceLinkState?> IDeviceLinkStateStore.LoadAsync(CancellationToken cancellationToken) =>
        await javascript.InvokeAsync<DeviceLinkState?>("gibddStorage.get", cancellationToken, "meta", "device-link");

    async Task IDeviceLinkStateStore.SaveAsync(DeviceLinkState state, CancellationToken cancellationToken) =>
        await javascript.InvokeVoidAsync("gibddStorage.put", cancellationToken, "meta", "device-link", state);

    async Task IDeviceLinkStateStore.ClearAsync(CancellationToken cancellationToken) =>
        await javascript.InvokeVoidAsync("gibddStorage.delete", cancellationToken, "meta", "device-link");

    private async Task<SessionRecord?> GetSessionRecordAsync(string key, CancellationToken cancellationToken) =>
        await javascript.InvokeAsync<SessionRecord?>("gibddStorage.get", cancellationToken, "sessions", key);

    private async Task<SyncRecord?> GetSyncRecordAsync(Guid userId, CancellationToken cancellationToken) =>
        await javascript.InvokeAsync<SyncRecord?>("gibddStorage.get", cancellationToken, "sync", userId.ToString("D"));

    private async Task<T[]> GetAllAsync<T>(string store, CancellationToken cancellationToken) =>
        await javascript.InvokeAsync<T[]>("gibddStorage.getAll", cancellationToken, store) ?? [];

    private static string SessionKey(Guid userId, Guid sessionId) => $"{userId:D}:{sessionId:D}";

    private sealed record SessionRecord(
        string Key,
        Guid UserId,
        Guid SessionId,
        string PayloadSha256,
        long? ServerSequence,
        string Origin,
        DateTimeOffset CreatedAtUtc,
        StudySessionEnvelope Session);

    private sealed record OutboxRecord(
        string Key,
        Guid UserId,
        Guid SessionId,
        int AttemptCount,
        DateTimeOffset NextAttemptAtUtc,
        string LastError,
        DateTimeOffset CreatedAtUtc,
        StudySessionEnvelope Session);

    private sealed record SyncRecord(Guid UserId, long ServerCursor, DateTimeOffset? LastSuccessfulSyncUtc);
    private sealed record ProfileCache(DateTimeOffset CalculatedAtUtc, IReadOnlyList<LearningQuestionProfile> Questions);
}
