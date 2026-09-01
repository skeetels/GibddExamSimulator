using GibddExamSimulator.Application.Learning;
using GibddExamSimulator.Application.Storage;
using GibddExamSimulator.Application.StudySessions;
using GibddExamSimulator.Application.Synchronization;
using GibddExamSimulator.ExamEngine;
using GibddExamSimulator.Models;
using GibddExamSimulator.Sync;

namespace GibddExamSimulator.Mobile.Shared.Services;

public sealed class MobileAppState(
    ILocalStudyStore store,
    IAuthSessionStore authStore,
    IDeviceLinkStateStore linkStateStore,
    IMobileConfigurationProvider configurationProvider,
    IMobileQuestionBankLoader bankLoader,
    IMobileOfflinePackageService offlinePackage,
    IMobilePlatform platform,
    IMobileQrScanner qrScanner)
{
    private const string RulesProfile = "ru-theory-mvd80-2025-05-26";
    private readonly LearningProfileBuilder _profileBuilder = new();
    private readonly TrainingQuestionPlanner _trainingPlanner = new();
    private readonly QuestionSelector _questionSelector = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly MobileReleaseUpdateService _updateService = new();
    private SupabaseAuthClient? _authClient;
    private SupabaseDeviceApiRemote? _deviceApi;
    private DeviceConnectionCoordinator? _connection;
    private SyncCoordinator? _sync;
    private AuthSession? _auth;
    private bool _completing;
    private bool _offlineDownloadCancellationRequested;

    public event Action? Changed;
    public bool IsInitialized { get; private set; }
    public string InitializationStatus { get; private set; } = "Проверяем 800 вопросов AB…";
    public string InitializationError { get; private set; } = string.Empty;
    public MobileClientConfiguration Configuration { get; private set; } = new();
    public MobileQuestionBank? Bank { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid UserId { get; private set; }
    public DeviceLinkState LinkState { get; private set; } = new() { DeviceId = Guid.Empty };
    public bool IsCloudConfigured => Configuration.IsCloudConfigured;
    public bool IsAuthenticated => _auth is not null;
    public bool CanStudy => UserId != Guid.Empty;
    public bool NeedsPairing => IsCloudConfigured && !LinkState.HasPeerDevice && !LinkState.OnboardingSkipped;
    public bool IsLinked => LinkState.HasPeerDevice;
    public bool CameraIsSupported => qrScanner.IsSupported;
    public string AccountCaption => IsLinked ? "Устройства связаны" : "Только это устройство";
    public string DeviceCaption => DeviceId == Guid.Empty
        ? platform.DeviceLabel
        : $"{platform.DeviceLabel} · {DeviceId.ToString("N")[..6].ToUpperInvariant()}";
    public string Status { get; private set; } = string.Empty;
    public string SyncStatus { get; private set; } = string.Empty;
    public string PairingStatus { get; private set; } = string.Empty;
    public string ConnectedDevicesStatus { get; private set; } = "Список устройств появится после первой привязки.";
    public IReadOnlyList<PairedDevice> ConnectedDevices { get; private set; } = [];
    public ActiveSessionDraft? SavedDraft { get; private set; }
    public MobileSessionController? ActiveSession { get; private set; }
    public StudySessionEnvelope? LastCompletedSession { get; private set; }
    public int SessionCount { get; private set; }
    public int PassedExamCount { get; private set; }
    public double AccuracyPercent { get; private set; }
    public string WeakTickets { get; private set; } = "Недостаточно данных";
    public int OfflineImageCount { get; private set; }
    public bool ImagesAreBundled => offlinePackage.IsBundled;
    public int OfflineCompleted { get; private set; }
    public int OfflineTotal { get; private set; }
    public bool IsOfflineDownloadRunning { get; private set; }
    public string UpdateStatus { get; private set; } = string.Empty;
    public bool HasInstallableUpdate => _availableUpdate is not null;
    public bool SupportsInstallableUpdates => platform.SupportsInstallableUpdates;
    private MobileReleaseUpdate? _availableUpdate;
    public string OfflinePackageSizeCaption => Bank is null
        ? string.Empty
        : $"≈ {Math.Ceiling(Bank.Manifest.ImageBytes / 1024d / 1024d):0} МБ";

    public async Task InitializeAsync()
    {
        if (IsInitialized)
            return;
        try
        {
            InitializationStatus = "Открываем локальное хранилище…";
            Notify();
            await store.InitializeAsync();
            InitializationStatus = "Загружаем настройки подключения…";
            Notify();
            Configuration = await configurationProvider.LoadAsync();
            InitializationStatus = "Проверяем 800 вопросов AB…";
            Notify();
            Bank = await bankLoader.LoadAsync();
            InitializationStatus = "Готовим профиль этого устройства…";
            Notify();
            DeviceId = await store.GetOrCreateDeviceIdAsync();
            offlinePackage.ProgressChanged += OnOfflineProgress;
            OfflineImageCount = await offlinePackage.CountAsync();

            if (Configuration.IsCloudConfigured)
            {
                var options = Configuration.ToSupabaseOptions();
                _authClient = new SupabaseAuthClient(options);
                _deviceApi = new SupabaseDeviceApiRemote(options);
                _connection = new DeviceConnectionCoordinator(
                    authStore,
                    linkStateStore,
                    _authClient,
                    _deviceApi);
                _sync = new SyncCoordinator(store, authStore, _authClient, new SupabaseStudySessionRemote(options));
                try
                {
                    var connection = await _connection.InitializeAsync(
                        DeviceId,
                        platform.DeviceKind,
                        platform.DeviceLabel);
                    _auth = connection.Auth;
                    LinkState = connection.LinkState;
                    UserId = _auth.UserId;
                    if (store is ILocalUserScopeMigration migration)
                        await migration.MergeUserScopeAsync(DeviceId, UserId);
                    Status = connection.IsOffline
                        ? "Офлайн — результат будет отправлен позже."
                        : IsLinked
                            ? "Устройства связаны. Синхронизация выполняется автоматически."
                            : "Отсканируйте QR-код с компьютера один раз.";
                    if (!connection.IsOffline)
                    {
                        await SyncAsync();
                        _ = RefreshConnectedDevicesAsync();
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    UserId = DeviceId;
                    LinkState = await linkStateStore.LoadAsync() ?? new DeviceLinkState { DeviceId = DeviceId };
                    Status = "Офлайн — обучение доступно, связь создастся автоматически после появления сети.";
                }
            }
            else
            {
                UserId = DeviceId;
                LinkState = new DeviceLinkState { DeviceId = DeviceId };
                Status = "Офлайн — результаты надёжно сохраняются на этом устройстве.";
            }
            if (CanStudy)
            {
                SavedDraft = await store.GetDraftAsync(UserId);
                await RefreshStatisticsAsync();
            }
            IsInitialized = true;
            InitializationStatus = string.Empty;
        }
        catch (Exception exception)
        {
            InitializationError = exception.Message;
            IsInitialized = true;
            InitializationStatus = string.Empty;
        }
        Notify();
    }

    public async Task SkipPairingAsync()
    {
        if (_connection is null)
            return;
        LinkState = await _connection.SkipOnboardingAsync(LinkState);
        PairingStatus = "Телефон не подключён. Это можно сделать позже с главной страницы.";
        Notify();
    }

    public async Task<bool> ScanAndPairAsync(CancellationToken cancellationToken = default)
    {
        if (_deviceApi is null || _connection is null || _auth is null)
        {
            PairingStatus = "Нет сети. Повторите после подключения.";
            Notify();
            return false;
        }
        try
        {
            PairingStatus = "Открываем камеру…";
            Notify();
            var payload = await qrScanner.ScanAsync(cancellationToken);
            PairingStatus = "Связываем устройства…";
            Notify();
            var invitation = PairingInvitation.Parse(payload, Configuration.EnvironmentId);
            var completed = await _deviceApi.CompletePairingAsync(
                _auth,
                DeviceId,
                platform.DeviceKind,
                platform.DeviceLabel,
                invitation.PairingId,
                invitation.OneTimeSecret,
                cancellationToken);
            LinkState = await _connection.ApplyPairingAsync(LinkState, completed, cancellationToken);
            PairingStatus = "Устройства связаны";
            Status = "Теперь результаты будут синхронизироваться автоматически.";
            await SyncAsync(cancellationToken);
            await RefreshConnectedDevicesAsync(cancellationToken);
            Notify();
            return true;
        }
        catch (OperationCanceledException)
        {
            PairingStatus = "Сканирование отменено.";
            Notify();
            return false;
        }
        catch (Exception exception)
        {
            PairingStatus = exception.Message;
            Notify();
            return false;
        }
    }

    public async Task<bool> PairWithShortCodeAsync(
        string shortCode,
        CancellationToken cancellationToken = default)
    {
        if (_deviceApi is null || _connection is null || _auth is null)
            return false;
        try
        {
            PairingStatus = "Проверяем одноразовый код…";
            Notify();
            var completed = await _deviceApi.CompletePairingWithShortCodeAsync(
                _auth,
                DeviceId,
                platform.DeviceKind,
                platform.DeviceLabel,
                shortCode,
                cancellationToken);
            LinkState = await _connection.ApplyPairingAsync(LinkState, completed, cancellationToken);
            PairingStatus = "Устройства связаны";
            Status = "Теперь результаты будут синхронизироваться автоматически.";
            await SyncAsync(cancellationToken);
            await RefreshConnectedDevicesAsync(cancellationToken);
            Notify();
            return true;
        }
        catch (Exception exception)
        {
            PairingStatus = exception.Message;
            Notify();
            return false;
        }
    }

    public async Task ConnectTelegramAsync()
    {
        if (_deviceApi is null || _auth is null)
            return;
        try
        {
            var link = await _deviceApi.StartTelegramLinkAsync(_auth);
            await platform.OpenUriAsync(link.DeepLink);
            Status = "Завершите подключение в Telegram. Возвращаться в настройки не нужно.";
        }
        catch
        {
            Status = "Не удалось подключить Telegram. Повторите позже.";
        }
        Notify();
    }

    public async Task RefreshConnectedDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (_deviceApi is null || _auth is null)
        {
            ConnectedDevicesStatus = "Список обновится автоматически после подключения к интернету.";
            Notify();
            return;
        }
        try
        {
            ConnectedDevicesStatus = "Обновляем список устройств…";
            Notify();
            ConnectedDevices = await _deviceApi.ListDevicesAsync(_auth, cancellationToken);
            LinkState = LinkState with
            {
                HasPeerDevice = ConnectedDevices.Any(item => !item.IsCurrentDevice),
                LastValidatedAtUtc = DateTimeOffset.UtcNow
            };
            await linkStateStore.SaveAsync(LinkState, cancellationToken);
            ConnectedDevicesStatus = ConnectedDevices.Count == 0
                ? "Подключённых устройств пока нет."
                : $"Подключено устройств: {ConnectedDevices.Count}.";
        }
        catch (Exception exception)
        {
            ConnectedDevicesStatus = "Не удалось обновить список: " + exception.Message;
        }
        Notify();
    }

    public async Task RevokeDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        var selected = ConnectedDevices.FirstOrDefault(item => item.DeviceId == deviceId);
        if (selected is null || _deviceApi is null || _auth is null)
            return;
        if (selected.IsCurrentDevice)
        {
            await ResetSynchronizationAsync(cancellationToken);
            return;
        }
        try
        {
            await _deviceApi.RevokeDeviceAsync(_auth, deviceId, cancellationToken);
            ConnectedDevicesStatus = "Выбранное устройство отвязано.";
            await RefreshConnectedDevicesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            ConnectedDevicesStatus = "Не удалось отвязать устройство: " + exception.Message;
            Notify();
        }
    }

    public async Task ResetSynchronizationAsync(CancellationToken cancellationToken = default)
    {
        if (_deviceApi is null || _auth is null || _connection is null)
        {
            ConnectedDevicesStatus = "Для безопасного сброса синхронизации нужен интернет.";
            Notify();
            return;
        }
        try
        {
            await _deviceApi.RevokeDeviceAsync(_auth, DeviceId, cancellationToken);
            var previousUserId = UserId;
            await authStore.ClearAsync(cancellationToken);
            await linkStateStore.ClearAsync(cancellationToken);
            if (store is ILocalUserScopeMigration migration)
                await migration.MergeUserScopeAsync(previousUserId, DeviceId, cancellationToken);
            _auth = null;
            UserId = DeviceId;
            LinkState = new DeviceLinkState { DeviceId = DeviceId };
            ConnectedDevices = [];

            var connection = await _connection.InitializeAsync(
                DeviceId,
                platform.DeviceKind,
                platform.DeviceLabel,
                cancellationToken);
            _auth = connection.Auth;
            LinkState = connection.LinkState;
            UserId = _auth.UserId;
            if (store is ILocalUserScopeMigration newScopeMigration)
                await newScopeMigration.MergeUserScopeAsync(DeviceId, UserId, cancellationToken);
            SavedDraft = await store.GetDraftAsync(UserId, cancellationToken);
            await RefreshStatisticsAsync();
            PairingStatus = "Синхронизация сброшена. Отсканируйте новый QR-код с компьютера.";
            ConnectedDevicesStatus = "Это устройство готово к новой одноразовой привязке.";
            Status = "Локальная история сохранена.";
        }
        catch (Exception exception)
        {
            ConnectedDevicesStatus = "Сброс не выполнен: " + exception.Message;
        }
        Notify();
    }

    public async Task StartExamAsync(string candidateName = "Кандидат")
    {
        RequireStudyReady();
        await BoundedPreSessionSyncAsync();
        var profile = await RebuildProfileAsync();
        var questions = _questionSelector.SelectMainExam(
            Bank!.Questions,
            "AB",
            profile.ToExamRiskProfile(candidateName));
        ActiveSession = MobileSessionController.CreateExam(
            DeviceId,
            new CandidateProfile { FullName = candidateName, Category = "AB", Department = "Мобильный тренажёр" },
            questions,
            platform.DeviceKind);
        LastCompletedSession = null;
        await SaveDraftAsync();
        Notify();
    }

    public async Task StartTrainingAsync(StudyMode mode, int? ticketNumber = null)
    {
        RequireStudyReady();
        var profile = await RebuildProfileAsync();
        var count = mode switch
        {
            StudyMode.Ticket => 20,
            StudyMode.Marathon => 100,
            StudyMode.NoMistakeChallenge => 50,
            StudyMode.WeakTopics => 20,
            _ => 10
        };
        var questions = _trainingPlanner.Select(Bank!.Questions, profile, mode, count, ticketNumber);
        if (questions.Count == 0)
        {
            questions = _trainingPlanner.Select(Bank.Questions, profile, StudyMode.SmartTen, 10);
            Status = "Для выбранного режима пока недостаточно истории; подготовлены «Умные 10».";
            mode = StudyMode.SmartTen;
        }
        ActiveSession = MobileSessionController.CreateTraining(DeviceId, mode, questions, platform.DeviceKind);
        LastCompletedSession = null;
        await SaveDraftAsync();
        Notify();
    }

    public async Task<bool> ResumeDraftAsync()
    {
        RequireStudyReady();
        if (SavedDraft is null)
            return false;
        if (!string.Equals(SavedDraft.BankSha256, Bank!.Manifest.BankSha256, StringComparison.OrdinalIgnoreCase))
        {
            Status = "Комплект вопросов обновился; старый незавершённый черновик нельзя продолжить.";
            Notify();
            return false;
        }
        var byId = Bank.Questions.ToDictionary(question => question.Id);
        ActiveSession = MobileSessionController.Restore(DeviceId, SavedDraft, byId, platform.DeviceKind);
        SavedDraft = null;
        Notify();
        return true;
    }

    public async Task DiscardDraftAsync()
    {
        if (!CanStudy)
            return;
        await store.DeleteDraftAsync(UserId);
        SavedDraft = null;
        if (ActiveSession is not null && !ActiveSession.IsCompleted)
            ActiveSession = null;
        Notify();
    }

    public void NavigateQuestion(int index)
    {
        ActiveSession?.NavigateTo(index);
        Notify();
    }

    public void SelectAnswer(int answer)
    {
        ActiveSession?.SelectAnswer(answer);
        Notify();
    }

    public async Task ConfirmAnswerAsync()
    {
        if (ActiveSession?.ConfirmAnswer() != true)
            return;
        if (ActiveSession.IsCompleted)
            await CompleteActiveSessionAsync();
        else
            await SaveDraftAsync();
        Notify();
    }

    public async Task ContinueTrainingAsync()
    {
        if (ActiveSession?.ContinueTraining() != true)
            return;
        await SaveDraftAsync();
        Notify();
    }

    public async Task StartSupplementaryAsync()
    {
        if (ActiveSession?.NeedsSupplementary != true || Bank is null)
            return;
        var questions = _questionSelector.SelectSupplementary(
            Bank.Questions,
            "AB",
            ActiveSession.ExamErrorGroups,
            ActiveSession.ExamMainQuestionIds);
        ActiveSession.StartSupplementary(questions);
        await SaveDraftAsync();
        Notify();
    }

    public async Task TickAsync()
    {
        if (ActiveSession is null || ActiveSession.IsCompleted)
            return;
        ActiveSession.Tick();
        if (ActiveSession.IsCompleted)
            await CompleteActiveSessionAsync();
        Notify();
    }

    public async Task<SyncResult?> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (_sync is null || !CanStudy)
            return null;
        var entered = false;
        try
        {
            await _syncGate.WaitAsync(cancellationToken);
            entered = true;
            SyncStatus = "Синхронизация…";
            Notify();
            var result = await _sync.SyncAsync(cancellationToken);
            SyncStatus = result.Message;
            _auth = await authStore.LoadAsync();
            await RefreshStatisticsAsync();
            Notify();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SyncStatus = "Используется локальная история; облако ответило не вовремя.";
            Notify();
            return null;
        }
        catch (Exception exception)
        {
            SyncStatus = "Синхронизация отложена: " + exception.Message;
            Notify();
            return null;
        }
        finally
        {
            if (entered)
                _syncGate.Release();
        }
    }

    public async Task DownloadOfflinePackageAsync()
    {
        RequireStudyReady();
        if (IsOfflineDownloadRunning)
            return;
        IsOfflineDownloadRunning = true;
        _offlineDownloadCancellationRequested = false;
        OfflineCompleted = 0;
        var urls = Bank!.Questions
            .Where(question => question.ImagePath is not null)
            .Select(question => "question-bank/ab/" + question.ImagePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        OfflineTotal = urls.Length;
        Notify();
        try
        {
            var storage = await offlinePackage.EstimateAsync();
            if (storage is not null && storage.Available < Bank!.Manifest.ImageBytes + 5L * 1024 * 1024)
            {
                Status = $"Недостаточно свободного места для офлайн-пакета {OfflinePackageSizeCaption}.";
                return;
            }
            await offlinePackage.DownloadAsync(urls);
            OfflineImageCount = await offlinePackage.CountAsync();
            Status = "Офлайн-пакет изображений загружен.";
        }
        catch (Exception exception)
        {
            Status = _offlineDownloadCancellationRequested
                ? "Загрузка офлайн-изображений отменена. Уже сохранённые изображения останутся доступны."
                : "Загрузка офлайн-пакета остановлена: " + exception.Message;
        }
        finally
        {
            IsOfflineDownloadRunning = false;
            _offlineDownloadCancellationRequested = false;
            Notify();
        }
    }

    public async Task CancelOfflinePackageDownloadAsync()
    {
        if (!IsOfflineDownloadRunning)
            return;
        _offlineDownloadCancellationRequested = true;
        await offlinePackage.CancelDownloadAsync();
        Status = "Загрузка офлайн-изображений отменена. Уже сохранённые изображения останутся доступны.";
    }

    public async Task ClearOfflinePackageAsync()
    {
        await offlinePackage.ClearAsync();
        OfflineImageCount = 0;
        OfflineCompleted = 0;
        OfflineTotal = 0;
        Status = "Офлайн-изображения удалены. Оболочка приложения и тексты вопросов сохранены.";
        Notify();
    }

    public async Task ResumeAsync()
    {
        if (!IsInitialized)
            await InitializeAsync();
        else if (_connection is not null)
        {
            try
            {
                var previousUserId = UserId;
                var connection = await _connection.InitializeAsync(DeviceId, platform.DeviceKind, platform.DeviceLabel);
                _auth = connection.Auth;
                LinkState = connection.LinkState;
                UserId = _auth.UserId;
                if (store is ILocalUserScopeMigration migration)
                    await migration.MergeUserScopeAsync(previousUserId, UserId);
                if (!connection.IsOffline)
                    _ = RefreshConnectedDevicesAsync();
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                Status = "Офлайн — результат будет отправлен позже.";
                Notify();
            }
        }
        if (CanStudy)
            await SyncAsync();
    }

    public async Task CheckForUpdatesAsync()
    {
        if (!platform.SupportsInstallableUpdates)
            return;
        try
        {
            UpdateStatus = "Проверяем GitHub Release…";
            _availableUpdate = null;
            Notify();
            _availableUpdate = await _updateService.CheckAsync(
                Configuration.GitHubRepository,
                Version.Parse(platform.AppVersion));
            UpdateStatus = _availableUpdate is null
                ? $"Установлена актуальная версия {platform.AppVersion}."
                : $"Доступна версия {_availableUpdate.Version}. Откройте подписанный APK релиза.";
        }
        catch (Exception exception)
        {
            UpdateStatus = "Проверка обновлений отложена: " + exception.Message;
        }
        Notify();
    }

    public async Task OpenAvailableUpdateAsync()
    {
        if (_availableUpdate is null)
            return;
        await platform.OpenUriAsync(_availableUpdate.DownloadUri);
    }

    private async Task CompleteActiveSessionAsync()
    {
        if (_completing || ActiveSession is null || Bank is null)
            return;
        _completing = true;
        try
        {
            var envelope = ActiveSession.BuildEnvelope(
                Bank.Manifest.BankVersion,
                Bank.Manifest.BankSha256,
                RulesProfile);
            await store.SaveCompletedSessionAsync(UserId, envelope);
            await store.DeleteDraftAsync(UserId);
            SavedDraft = null;
            LastCompletedSession = envelope;
            await RebuildProfileAsync();
            await RefreshStatisticsAsync();
            Status = envelope.Mode == StudyMode.Exam
                ? "Результат сохранён и будет отправлен автоматически."
                : "Тренировка сохранена в единую историю.";
            _ = SyncAsync();
        }
        finally
        {
            _completing = false;
        }
    }

    private async Task SaveDraftAsync()
    {
        if (ActiveSession is null || Bank is null)
            return;
        var draft = ActiveSession.CreateDraft(Bank.Manifest.BankVersion, Bank.Manifest.BankSha256);
        await store.SaveDraftAsync(UserId, draft);
    }

    private async Task<LearningProfile> RebuildProfileAsync()
    {
        var sessions = await store.GetSessionsAsync(UserId);
        var profile = _profileBuilder.Build(sessions, DateTimeOffset.UtcNow);
        await store.SaveLearningProfileAsync(UserId, profile);
        return profile;
    }

    private async Task RefreshStatisticsAsync()
    {
        if (!CanStudy)
            return;
        var sessions = await store.GetSessionsAsync(UserId);
        SessionCount = sessions.Count;
        PassedExamCount = sessions.Count(session => session.Mode == StudyMode.Exam && session.Outcome == StudyOutcome.Passed);
        var answers = sessions.SelectMany(session => session.Answers).ToArray();
        AccuracyPercent = answers.Length == 0 ? 0 : answers.Count(answer => answer.IsCorrect) * 100.0 / answers.Length;
        var weak = answers.Where(answer => !answer.IsCorrect)
            .GroupBy(answer => answer.TicketNumber)
            .Select(group => new { Ticket = group.Key, Errors = group.Count() })
            .OrderByDescending(item => item.Errors)
            .ThenBy(item => item.Ticket)
            .Take(3)
            .Select(item => $"№{item.Ticket} ({item.Errors})")
            .ToArray();
        WeakTickets = weak.Length == 0 ? "Ошибок пока нет" : string.Join(", ", weak);
    }

    private async Task BoundedPreSessionSyncAsync()
    {
        if (_sync is null)
            return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await SyncAsync(timeout.Token);
    }

    private void RequireStudyReady()
    {
        if (!CanStudy || Bank is null)
            throw new InvalidOperationException("Дождитесь загрузки комплекта вопросов AB.");
    }

    private void OnOfflineProgress(int completed, int total)
    {
        OfflineCompleted = completed;
        OfflineTotal = total;
        Notify();
    }

    private void Notify() => Changed?.Invoke();
}
