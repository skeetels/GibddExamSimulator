using GibddExamSimulator.Application.Learning;
using GibddExamSimulator.Application.Storage;

namespace GibddExamSimulator.Application.Synchronization;

public enum SyncResultStatus
{
    Succeeded,
    NotAuthenticated,
    Offline,
    IntegrityConflict
}

public sealed record SyncResult(
    SyncResultStatus Status,
    string Message,
    int UploadedCount = 0,
    int DownloadedCount = 0,
    DateTimeOffset? CompletedAtUtc = null);

public sealed class SyncCoordinator
{
    private const int OutboxBatchSize = 20;
    private const int PullPageSize = 100;
    private readonly ILocalStudyStore _local;
    private readonly IAuthSessionStore _authStore;
    private readonly IAuthClient _authClient;
    private readonly IStudySessionRemote _remote;
    private readonly LearningProfileBuilder _profileBuilder;
    private readonly TimeProvider _timeProvider;

    public SyncCoordinator(
        ILocalStudyStore local,
        IAuthSessionStore authStore,
        IAuthClient authClient,
        IStudySessionRemote remote,
        LearningProfileBuilder? profileBuilder = null,
        TimeProvider? timeProvider = null)
    {
        _local = local;
        _authStore = authStore;
        _authClient = authClient;
        _remote = remote;
        _profileBuilder = profileBuilder ?? new LearningProfileBuilder();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        var auth = await _authStore.LoadAsync(cancellationToken);
        if (auth is null)
            return new SyncResult(SyncResultStatus.NotAuthenticated, "Связь с сервисом ещё не подготовлена.");

        try
        {
            var now = _timeProvider.GetUtcNow();
            if (auth.NeedsRefresh(now))
            {
                auth = await _authClient.RefreshAsync(auth, cancellationToken);
                await _authStore.SaveAsync(auth, cancellationToken);
            }

            var uploaded = 0;
            while (true)
            {
                var pending = await _local.GetPendingOutboxAsync(auth.UserId, OutboxBatchSize, now, cancellationToken);
                if (pending.Count == 0)
                    break;

                foreach (var item in pending.OrderBy(item => item.Session.CompletedAtUtc))
                {
                    UploadResult result;
                    try
                    {
                        result = await _remote.UploadAsync(auth, item.Session, cancellationToken);
                    }
                    catch (HttpRequestException)
                    {
                        await _local.MarkOutboxFailedAsync(
                            auth.UserId,
                            item.SessionId,
                            item.AttemptCount,
                            "Сеть недоступна. Повтор запланирован автоматически.",
                            now.Add(RetryDelay(item.SessionId, item.AttemptCount)),
                            cancellationToken);
                        return new SyncResult(
                            SyncResultStatus.Offline,
                            "Офлайн — результат будет отправлен позже.",
                            uploaded);
                    }
                    if (result.Disposition == UploadDisposition.IntegrityConflict)
                    {
                        await _local.MarkOutboxFailedAsync(
                            auth.UserId,
                            item.SessionId,
                            item.AttemptCount,
                            "Конфликт целостности sessionId.",
                            now.AddHours(12),
                            cancellationToken);
                        return new SyncResult(
                            SyncResultStatus.IntegrityConflict,
                            "Обнаружен конфликт целостности завершённой сессии. Данные не перезаписаны.",
                            uploaded);
                    }

                    await _local.MarkOutboxSucceededAsync(auth.UserId, item.SessionId, cancellationToken);
                    uploaded++;
                }

                if (pending.Count < OutboxBatchSize)
                    break;
            }

            var downloaded = 0;
            var cursor = await _local.GetServerCursorAsync(auth.UserId, cancellationToken);
            while (true)
            {
                var page = await _remote.PullAsync(auth, cursor, PullPageSize, cancellationToken);
                if (page.Items.Count == 0)
                    break;
                var ordered = page.Items.OrderBy(item => item.ServerSequence).ToArray();
                var nextCursor = ordered[^1].ServerSequence;
                await _local.ApplyRemotePageAsync(auth.UserId, ordered, nextCursor, cancellationToken);
                downloaded += ordered.Length;
                cursor = nextCursor;
                if (!page.HasMore || ordered.Length < PullPageSize)
                    break;
            }

            var completedAt = _timeProvider.GetUtcNow();
            var sessions = await _local.GetSessionsAsync(auth.UserId, cancellationToken);
            var profile = _profileBuilder.Build(sessions, completedAt);
            await _local.SaveLearningProfileAsync(auth.UserId, profile, cancellationToken);
            await _local.SetLastSuccessfulSyncAsync(auth.UserId, completedAt, cancellationToken);
            return new SyncResult(
                SyncResultStatus.Succeeded,
                "Синхронизация завершена.",
                uploaded,
                downloaded,
                completedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new SyncResult(
                SyncResultStatus.Offline,
                "Офлайн — результат будет отправлен позже.");
        }
        catch (TaskCanceledException)
        {
            return new SyncResult(
                SyncResultStatus.Offline,
                "Сервер не ответил вовремя. Данные сохранены на устройстве.");
        }
    }

    internal static TimeSpan RetryDelay(Guid sessionId, int previousAttemptCount)
    {
        var exponent = Math.Clamp(previousAttemptCount, 0, 8);
        var baseSeconds = Math.Min(900, 5 * (1 << exponent));
        var bytes = sessionId.ToByteArray();
        var jitterPercent = (bytes[0] % 41) - 20;
        return TimeSpan.FromSeconds(Math.Max(3, baseSeconds * (100 + jitterPercent) / 100d));
    }
}
