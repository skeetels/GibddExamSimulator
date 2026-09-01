using System.Net.Http.Json;
using GibddExamSimulator.Application.Learning;
using GibddExamSimulator.Application.Storage;
using GibddExamSimulator.Application.StudySessions;
using GibddExamSimulator.Application.Synchronization;
using GibddExamSimulator.ExamEngine;
using GibddExamSimulator.Models;
using GibddExamSimulator.Sync;

namespace GibddExamSimulator.Web.Services;

public sealed class MobileAppState(
    HttpClient httpClient,
    BrowserStudyStore store,
    WebQuestionBankLoader bankLoader,
    OfflinePackageService offlinePackage)
{
    private const string RulesProfile = "ru-theory-mvd80-2025-05-26";
    private readonly LearningProfileBuilder _profileBuilder = new();
    private readonly TrainingQuestionPlanner _trainingPlanner = new();
    private readonly QuestionSelector _questionSelector = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private SupabaseAuthClient? _authClient;
    private SyncCoordinator? _sync;
    private AuthSession? _auth;
    private bool _completing;
    private bool _offlineDownloadCancellationRequested;

    public event Action? Changed;
    public bool IsInitialized { get; private set; }
    public string InitializationStatus { get; private set; } = "Проверяем 800 вопросов AB…";
    public string InitializationError { get; private set; } = string.Empty;
    public MobileClientConfiguration Configuration { get; private set; } = new();
    public WebQuestionBank? Bank { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid UserId { get; private set; }
    public bool IsCloudConfigured => Configuration.IsCloudConfigured;
    public bool IsAuthenticated => _auth is not null;
    public bool CanStudy => UserId != Guid.Empty;
    public string AccountCaption => _auth?.Email ?? (IsCloudConfigured ? "Вход не выполнен" : "Локальный режим");
    public string DeviceCaption => DeviceId == Guid.Empty
        ? "Телефон / PWA"
        : $"Телефон / PWA · {DeviceId.ToString("N")[..6].ToUpperInvariant()}";
    public string Status { get; private set; } = string.Empty;
    public string SyncStatus { get; private set; } = string.Empty;
    public ActiveSessionDraft? SavedDraft { get; private set; }
    public MobileSessionController? ActiveSession { get; private set; }
    public StudySessionEnvelope? LastCompletedSession { get; private set; }
    public int SessionCount { get; private set; }
    public int PassedExamCount { get; private set; }
    public double AccuracyPercent { get; private set; }
    public string WeakTickets { get; private set; } = "Недостаточно данных";
    public int OfflineImageCount { get; private set; }
    public int OfflineCompleted { get; private set; }
    public int OfflineTotal { get; private set; }
    public bool IsOfflineDownloadRunning { get; private set; }
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
            Configuration = await httpClient.GetFromJsonAsync<MobileClientConfiguration>("client-settings.json") ?? new();
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
                _sync = new SyncCoordinator(store, store, _authClient, new SupabaseStudySessionRemote(options));
                _auth = await store.LoadAsync();
                if (_auth is not null)
                {
                    UserId = _auth.UserId;
                    await SyncAsync();
                }
                else
                {
                    Status = "Войдите в выданный аккаунт для общей истории на телефоне и ПК.";
                }
            }
            else
            {
                UserId = DeviceId;
                Status = "Локальный офлайн-режим. Telegram-отчёты начнут отправляться после настройки Supabase.";
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

    public async Task<bool> SignInAsync(string email, string password)
    {
        if (_authClient is null)
        {
            Status = "Облачная конфигурация не задана.";
            Notify();
            return false;
        }
        try
        {
            Status = "Вход…";
            Notify();
            _auth = await _authClient.SignInWithPasswordAsync(email, password);
            await store.SaveAsync(_auth);
            UserId = _auth.UserId;
            SavedDraft = await store.GetDraftAsync(UserId);
            await SyncAsync();
            await RefreshStatisticsAsync();
            Status = string.Empty;
            Notify();
            return true;
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            Notify();
            return false;
        }
    }

    public async Task SignOutAsync()
    {
        try
        {
            if (_auth is not null && _authClient is not null)
                await _authClient.SignOutAsync(_auth);
        }
        catch
        {
            // Local token removal still proceeds if the network is unavailable.
        }
        await store.ClearAsync();
        _auth = null;
        UserId = Guid.Empty;
        ActiveSession = null;
        SavedDraft = null;
        LastCompletedSession = null;
        Status = "Вы вышли из аккаунта.";
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
            questions);
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
        ActiveSession = MobileSessionController.CreateTraining(DeviceId, mode, questions);
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
        ActiveSession = MobileSessionController.Restore(DeviceId, SavedDraft, byId);
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
            _auth = await store.LoadAsync();
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
                ? "Результат сохранён. Telegram-отчёт отправится автоматически при синхронизации."
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
            throw new InvalidOperationException("Сначала войдите в аккаунт и дождитесь загрузки комплекта AB.");
    }

    private void OnOfflineProgress(int completed, int total)
    {
        OfflineCompleted = completed;
        OfflineTotal = total;
        Notify();
    }

    private void Notify() => Changed?.Invoke();
}
