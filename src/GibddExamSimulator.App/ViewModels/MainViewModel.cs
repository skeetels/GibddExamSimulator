using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GibddExamSimulator.Application.Learning;
using GibddExamSimulator.Application.Storage;
using GibddExamSimulator.Application.StudySessions;
using GibddExamSimulator.Application.Synchronization;
using GibddExamSimulator.Configuration;
using GibddExamSimulator.Database;
using GibddExamSimulator.ExamEngine;
using GibddExamSimulator.Infrastructure.Storage;
using GibddExamSimulator.Models;
using GibddExamSimulator.Services;
using GibddExamSimulator.Sync;

namespace GibddExamSimulator.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string RulesProfile = "ru-theory-mvd80-2025-05-26";
    private static readonly Brush NavyBrush = FrozenBrush(15, 52, 82);
    private static readonly Brush TealBrush = FrozenBrush(0, 137, 123);
    private static readonly Brush RedBrush = FrozenBrush(179, 38, 30);
    private static readonly Brush PaleBlueBrush = FrozenBrush(218, 234, 246);
    private static readonly Brush LegacySelectedBrush = FrozenBrush(221, 234, 246);
    private static readonly Brush LegacyConfirmedBrush = FrozenBrush(187, 210, 232);
    private static readonly Brush LegacyViewedBrush = FrozenBrush(255, 249, 218);
    private static readonly Brush LegacyAnsweredBrush = FrozenBrush(224, 224, 224);
    private static readonly Brush LegacyServiceBlueBrush = FrozenBrush(18, 90, 156);
    private static readonly Brush PaleYellowBrush = FrozenBrush(255, 243, 189);
    private static readonly Brush MutedBrush = FrozenBrush(238, 241, 243);
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly LearningProfileBuilder _profileBuilder = new();
    private readonly QuestionSelector _selector = new();
    private PageKind _page = PageKind.Loading;
    private string _loadingText = "Проверка комплекта экзаменационных вопросов…";
    private string _email = string.Empty;
    private string _loginStatus = string.Empty;
    private string _candidateName = "Кандидат";
    private string _accountCaption = string.Empty;
    private string _cloudStatus = string.Empty;
    private string _syncStatus = string.Empty;
    private string _homeStatistics = string.Empty;
    private string _updateStatus = string.Empty;
    private bool _hasAvailableUpdate;
    private string _deviceCaption = "ПК";
    private string _readyTitle = string.Empty;
    private string _readyDetails = string.Empty;
    private string _stageCaption = string.Empty;
    private string _questionCounter = string.Empty;
    private string _questionTicketCaption = string.Empty;
    private string _questionText = string.Empty;
    private string _remainingTime = "20:00";
    private string _questionTitle = string.Empty;
    private string _examStatusMessage = "Выберите вопрос из перечня.";
    private bool _isExamOverview = true;
    private ImageSource? _currentImage;
    private bool _canConfirm;
    private string _supplementaryTitle = string.Empty;
    private string _supplementaryDetails = string.Empty;
    private string _resultTitle = string.Empty;
    private string _resultDetails = string.Empty;
    private Brush _resultBrush = NavyBrush;
    private string _telegramDeliveryStatus = string.Empty;
    private bool _hasReviewErrors;
    private string _reviewPosition = string.Empty;
    private string _reviewHeading = string.Empty;
    private string _reviewQuestionText = string.Empty;
    private string _reviewSelectedAnswer = string.Empty;
    private string _reviewCorrectAnswer = string.Empty;
    private string _reviewExplanation = string.Empty;
    private string _reviewResponseTime = string.Empty;
    private ImageSource? _reviewImage;
    private ClientConfiguration _configuration = new();
    private QuestionBankPackage? _bank;
    private DesktopStudyStore? _store;
    private WindowsProtectedAuthSessionStore? _authStore;
    private SupabaseAuthClient? _authClient;
    private SyncCoordinator? _sync;
    private AuthSession? _auth;
    private Guid _deviceId;
    private Guid _userId;
    private ExamEngine.ExamEngine? _engine;
    private IReadOnlyList<Question> _readyQuestions = [];
    private IReadOnlyList<Question> _supplementaryQuestions = [];
    private ExamResult? _lastResult;
    private StudySessionEnvelope? _lastEnvelope;
    private readonly List<ReviewErrorItem> _reviewErrors = [];
    private readonly Dictionary<string, ImageSource?> _examImageCache = new(StringComparer.OrdinalIgnoreCase);
    private int _reviewIndex;
    private UpdateCheckResult? _availableUpdate;
    private string _localDataRoot = string.Empty;
    private bool _handlingTransition;

    public MainViewModel()
    {
        PrepareExamCommand = new AsyncRelayCommand(PrepareExamAsync);
        BeginExamCommand = new RelayCommand(BeginExam);
        NavigateQuestionCommand = new RelayCommand<int>(NavigateQuestion);
        OpenOverviewQuestionCommand = new RelayCommand<int>(OpenOverviewQuestion);
        SelectAnswerCommand = new RelayCommand<int>(SelectAnswer);
        ConfirmAnswerCommand = new AsyncRelayCommand(ConfirmAnswerAsync);
        ReturnToOverviewCommand = new RelayCommand(ShowExamOverview);
        PreviousQuestionCommand = new RelayCommand(() => NavigateExamRelative(-1));
        NextQuestionCommand = new RelayCommand(() => NavigateExamRelative(1));
        StartSupplementaryCommand = new RelayCommand(StartSupplementary);
        ReturnHomeCommand = new AsyncRelayCommand(ReturnHomeAsync);
        ReviewErrorsCommand = new RelayCommand(OpenReview);
        PreviousReviewCommand = new RelayCommand(() => NavigateReview(-1));
        NextReviewCommand = new RelayCommand(() => NavigateReview(1));
        SyncNowCommand = new AsyncRelayCommand(() => SyncNowAsync(showProgress: true));
        SignOutCommand = new AsyncRelayCommand(SignOutAsync);
        CheckUpdatesCommand = new AsyncRelayCommand(() => CheckForUpdatesAsync(silent: false));
        InstallUpdateCommand = new AsyncRelayCommand(InstallAvailableUpdateAsync);
        ExitCommand = new RelayCommand(() => System.Windows.Application.Current.Shutdown());
    }

    public ObservableCollection<QuestionNavigationItem> QuestionNavigation { get; } = [];
    public ObservableCollection<AnswerChoiceItem> AnswerChoices { get; } = [];
    public ObservableCollection<ExamQuestionPreviewItem> OverviewQuestions { get; } = [];

    public PageKind Page { get => _page; private set => SetProperty(ref _page, value); }
    public string LoadingText { get => _loadingText; private set => SetProperty(ref _loadingText, value); }
    public string Email { get => _email; set => SetProperty(ref _email, value); }
    public string LoginStatus { get => _loginStatus; private set => SetProperty(ref _loginStatus, value); }
    public string CandidateName { get => _candidateName; set => SetProperty(ref _candidateName, value); }
    public string AccountCaption { get => _accountCaption; private set => SetProperty(ref _accountCaption, value); }
    public string CloudStatus { get => _cloudStatus; private set => SetProperty(ref _cloudStatus, value); }
    public string SyncStatus { get => _syncStatus; private set => SetProperty(ref _syncStatus, value); }
    public string HomeStatistics { get => _homeStatistics; private set => SetProperty(ref _homeStatistics, value); }
    public string UpdateStatus { get => _updateStatus; private set => SetProperty(ref _updateStatus, value); }
    public bool HasAvailableUpdate { get => _hasAvailableUpdate; private set => SetProperty(ref _hasAvailableUpdate, value); }
    public string DeviceCaption { get => _deviceCaption; private set => SetProperty(ref _deviceCaption, value); }
    public string ReadyTitle { get => _readyTitle; private set => SetProperty(ref _readyTitle, value); }
    public string ReadyDetails { get => _readyDetails; private set => SetProperty(ref _readyDetails, value); }
    public string StageCaption { get => _stageCaption; private set => SetProperty(ref _stageCaption, value); }
    public string QuestionCounter { get => _questionCounter; private set => SetProperty(ref _questionCounter, value); }
    public string QuestionTicketCaption { get => _questionTicketCaption; private set => SetProperty(ref _questionTicketCaption, value); }
    public string QuestionText { get => _questionText; private set => SetProperty(ref _questionText, value); }
    public string RemainingTime { get => _remainingTime; private set => SetProperty(ref _remainingTime, value); }
    public string QuestionTitle { get => _questionTitle; private set => SetProperty(ref _questionTitle, value); }
    public string ExamStatusMessage { get => _examStatusMessage; private set => SetProperty(ref _examStatusMessage, value); }
    public bool IsExamOverview { get => _isExamOverview; private set => SetProperty(ref _isExamOverview, value); }
    public string TerminalText => $"Терминал {_engine?.Session?.Candidate.TerminalNumber ?? 6}";
    public string CandidateText => _engine?.Session is null
        ? string.Empty
        : $"{_engine.Session.Candidate.FullName}, Категория {_engine.Session.Candidate.Category}";
    public string ExamOverviewHeaderText => _engine?.Session is null
        ? "ПЕРЕЧЕНЬ ВОПРОСОВ"
        : $"ПЕРЕЧЕНЬ ВОПРОСОВ — отвечено {_engine.Session.ActiveQuestions.Count(item => item.ConfirmedAnswer.HasValue)} из {_engine.Session.ActiveQuestions.Count}";
    public int ExamOverviewColumnCount => Math.Max(1, (OverviewQuestions.Count + 4) / 5);
    public bool IsDemoQuestion => false;
    public ImageSource? CurrentImage
    {
        get => _currentImage;
        private set
        {
            if (SetProperty(ref _currentImage, value))
                OnPropertyChanged(nameof(HasQuestionImage));
        }
    }
    public bool HasQuestionImage => CurrentImage is not null;
    public bool CanConfirm { get => _canConfirm; private set => SetProperty(ref _canConfirm, value); }
    public string SupplementaryTitle { get => _supplementaryTitle; private set => SetProperty(ref _supplementaryTitle, value); }
    public string SupplementaryDetails { get => _supplementaryDetails; private set => SetProperty(ref _supplementaryDetails, value); }
    public string ResultTitle { get => _resultTitle; private set => SetProperty(ref _resultTitle, value); }
    public string ResultDetails { get => _resultDetails; private set => SetProperty(ref _resultDetails, value); }
    public Brush ResultBrush { get => _resultBrush; private set => SetProperty(ref _resultBrush, value); }
    public string TelegramDeliveryStatus { get => _telegramDeliveryStatus; private set => SetProperty(ref _telegramDeliveryStatus, value); }
    public bool HasReviewErrors { get => _hasReviewErrors; private set => SetProperty(ref _hasReviewErrors, value); }
    public string ReviewPosition { get => _reviewPosition; private set => SetProperty(ref _reviewPosition, value); }
    public string ReviewHeading { get => _reviewHeading; private set => SetProperty(ref _reviewHeading, value); }
    public string ReviewQuestionText { get => _reviewQuestionText; private set => SetProperty(ref _reviewQuestionText, value); }
    public string ReviewSelectedAnswer { get => _reviewSelectedAnswer; private set => SetProperty(ref _reviewSelectedAnswer, value); }
    public string ReviewCorrectAnswer { get => _reviewCorrectAnswer; private set => SetProperty(ref _reviewCorrectAnswer, value); }
    public string ReviewExplanation { get => _reviewExplanation; private set => SetProperty(ref _reviewExplanation, value); }
    public string ReviewResponseTime { get => _reviewResponseTime; private set => SetProperty(ref _reviewResponseTime, value); }
    public ImageSource? ReviewImage
    {
        get => _reviewImage;
        private set
        {
            if (SetProperty(ref _reviewImage, value))
                OnPropertyChanged(nameof(HasReviewImage));
        }
    }
    public bool HasReviewImage => ReviewImage is not null;
    public bool HasRunningExam => _engine?.Session?.Status == AttemptStatus.InProgress;

    public ICommand PrepareExamCommand { get; }
    public ICommand BeginExamCommand { get; }
    public ICommand NavigateQuestionCommand { get; }
    public ICommand OpenOverviewQuestionCommand { get; }
    public ICommand SelectAnswerCommand { get; }
    public ICommand ConfirmAnswerCommand { get; }
    public ICommand ReturnToOverviewCommand { get; }
    public ICommand PreviousQuestionCommand { get; }
    public ICommand NextQuestionCommand { get; }
    public ICommand StartSupplementaryCommand { get; }
    public ICommand ReturnHomeCommand { get; }
    public ICommand ReviewErrorsCommand { get; }
    public ICommand PreviousReviewCommand { get; }
    public ICommand NextReviewCommand { get; }
    public ICommand SyncNowCommand { get; }
    public ICommand SignOutCommand { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand InstallUpdateCommand { get; }
    public ICommand ExitCommand { get; }

    public async Task InitializeAsync()
    {
        try
        {
            LoadingText = "Проверка комплекта из 800 вопросов категории AB…";
            _configuration = await new ClientConfigurationLoader(AppContext.BaseDirectory).LoadAsync();
            var bankRoot = Path.Combine(AppContext.BaseDirectory, "QuestionBank");
            _bank = await new QuestionBankPackageLoader().LoadAsync(bankRoot);

            var overriddenDataRoot = Environment.GetEnvironmentVariable("GIBDD_LOCAL_DATA_ROOT");
            _localDataRoot = string.IsNullOrWhiteSpace(overriddenDataRoot)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GibddExamSimulator")
                : Path.GetFullPath(overriddenDataRoot);
            Directory.CreateDirectory(_localDataRoot);
            var databasePath = Path.Combine(_localDataRoot, "Data", "questions.db");
            _store = new DesktopStudyStore(databasePath);
            await _store.InitializeAsync();
            _deviceId = await _store.GetOrCreateDeviceIdAsync();
            DeviceCaption = $"ПК · {_deviceId:N}"[..11].ToUpperInvariant();
            _authStore = new WindowsProtectedAuthSessionStore(Path.Combine(_localDataRoot, "auth-session.bin"));

            if (_configuration.IsCloudConfigured)
            {
                var options = _configuration.ToSupabaseOptions();
                _authClient = new SupabaseAuthClient(options);
                _sync = new SyncCoordinator(
                    _store,
                    _authStore,
                    _authClient,
                    new SupabaseStudySessionRemote(options));
                _auth = await _authStore.LoadAsync();
                if (_auth is null)
                {
                    CloudStatus = "Войдите в выданный аккаунт. Самостоятельная регистрация отключена.";
                    Page = PageKind.Login;
                }
                else
                {
                    await ActivateUserAsync(_auth.UserId);
                    Page = PageKind.Home;
                    _ = SyncNowAsync(showProgress: false);
                }
            }
            else
            {
                _userId = _deviceId;
                AccountCaption = "Локальный режим";
                CloudStatus = "Облако пока не настроено. Экзамены работают офлайн; Telegram-отчёт останется в очереди до подключения Supabase.";
                await ActivateUserAsync(_userId);
                Page = PageKind.Home;
            }

            _ = CheckForUpdatesAsync(silent: true);
        }
        catch (Exception exception)
        {
            LoadingText = "Запуск остановлен: " + SafeMessage(exception);
            CloudStatus = LoadingText;
        }
    }

    public async Task SignInAsync(string password)
    {
        if (_authClient is null || _authStore is null)
        {
            LoginStatus = "Облачный адрес или публичный ключ Supabase не настроен.";
            return;
        }

        LoginStatus = "Проверка учётной записи…";
        try
        {
            _auth = await _authClient.SignInWithPasswordAsync(Email, password);
            await _authStore.SaveAsync(_auth);
            await ActivateUserAsync(_auth.UserId);
            LoginStatus = string.Empty;
            Page = PageKind.Home;
            await SyncNowAsync(showProgress: false);
        }
        catch (Exception exception)
        {
            LoginStatus = SafeMessage(exception);
        }
    }

    public async Task TickAsync()
    {
        if (Page != PageKind.Exam || _engine?.Session is null || _handlingTransition)
            return;
        _engine.Tick();
        RefreshExamPresentation();
        await HandleExamTransitionAsync();
    }

    public void HandleDigitShortcut(int number)
    {
        if (Page == PageKind.Exam && !IsExamOverview)
            SelectAnswer(number);
    }

    public void HandleConfirmShortcut()
    {
        if (Page != PageKind.Exam)
            return;
        if (IsExamOverview)
            OpenCurrentQuestion();
        else
            ConfirmAnswerCommand.Execute(null);
    }

    public void HandleNavigationShortcut(int offset)
    {
        if (Page != PageKind.Exam || _engine?.Session is null)
            return;
        if (IsExamOverview)
            NavigateOverviewRelative(offset);
        else
            NavigateExamRelative(offset);
    }

    public void HandleTerminalKey(Key key)
    {
        if (Page != PageKind.Exam)
            return;
        if (IsExamOverview)
        {
            if (key is Key.Enter or Key.Space)
                OpenCurrentQuestion();
            else if (key == Key.Left)
                NavigateOverviewRelative(-1);
            else if (key == Key.Right)
                NavigateOverviewRelative(1);
            return;
        }

        var answer = key switch
        {
            Key.D1 or Key.NumPad1 => 1,
            Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3,
            Key.D4 or Key.NumPad4 => 4,
            Key.D5 or Key.NumPad5 => 5,
            _ => 0
        };
        if (answer > 0)
            SelectAnswer(answer);
        else if (key == Key.Escape)
            ShowExamOverview();
        else if (key is Key.Enter or Key.Space)
            ConfirmAnswerCommand.Execute(null);
        else if (key == Key.Left)
            NavigateExamRelative(-1);
        else if (key == Key.Right)
            NavigateExamRelative(1);
    }

    public void ShowResultPage()
    {
        if (_lastResult is not null)
            Page = PageKind.Result;
    }

    public void InterruptActiveExam()
    {
        if (_engine?.Session?.Status == AttemptStatus.InProgress)
            _engine.Interrupt("Попытка прервана при закрытии программы.");
    }

    public async Task AbortActiveExamAsync()
    {
        InterruptActiveExam();
        await ReturnHomeAsync();
    }

    private async Task ActivateUserAsync(Guid userId)
    {
        if (_store is null || _bank is null)
            throw new InvalidOperationException("Локальное хранилище ещё не готово.");
        _userId = userId;
        AccountCaption = _auth is null ? "Локальный режим" : _auth.Email;
        var migration = await _store.MigrateLegacyAsync(
            userId,
            _deviceId,
            _bank.Manifest.BankVersion,
            _bank.Manifest.BankSha256,
            RulesProfile);
        if (!migration.AlreadyApplied && (migration.ExamSessionsImported > 0 || migration.LegacyTrainingQuestionsImported > 0))
        {
            SyncStatus = $"Импортировано старых экзаменов: {migration.ExamSessionsImported}; учебных показателей: {migration.LegacyTrainingQuestionsImported}.";
        }
        await RefreshHomeStatisticsAsync();
    }

    private async Task PrepareExamAsync()
    {
        if (_store is null || _bank is null || _userId == Guid.Empty)
            return;
        LoadingText = "Синхронизация истории и подбор сложных тематических блоков…";
        Page = PageKind.Loading;

        if (_sync is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _sync.SyncAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                SyncStatus = "Предэкзаменационная синхронизация не успела завершиться. Используется локальная история.";
            }
        }

        var sessions = await _store.GetSessionsAsync(_userId);
        var profile = _profileBuilder.Build(sessions, DateTimeOffset.UtcNow);
        await _store.SaveLearningProfileAsync(_userId, profile);
        _readyQuestions = _selector.SelectMainExam(
            _bank.Questions,
            "AB",
            profile.ToExamRiskProfile(CandidateName));
        ReadyTitle = "Экзамен готов";
        ReadyDetails =
            "20 вопросов · 20 минут · 4 тематических блока по 5 вопросов\n" +
            "При одной ошибке добавляется 5 вопросов и 5 минут; при двух ошибках в разных блоках — 10 вопросов и 10 минут. " +
            "Две ошибки в одном блоке, три ошибки всего или ошибка в дополнительном блоке означают результат «не сдан».";
        Page = PageKind.Ready;
    }

    private void BeginExam()
    {
        if (_readyQuestions.Count != ExamRules.MainQuestionCount)
            return;
        var candidate = new CandidateProfile
        {
            FullName = string.IsNullOrWhiteSpace(CandidateName) ? "Кандидат" : CandidateName.Trim(),
            Category = "AB",
            TerminalNumber = 6,
            Department = "Учебный экзаменационный терминал"
        };
        _engine = new ExamEngine.ExamEngine();
        _engine.Start(candidate, _readyQuestions);
        _examImageCache.Clear();
        IsExamOverview = true;
        ExamStatusMessage = "Выберите вопрос из перечня. Все вопросы доступны в произвольном порядке.";
        Page = PageKind.Exam;
        RefreshExamPresentation();
    }

    private void NavigateQuestion(int index)
    {
        if (_engine?.NavigateTo(index) == true)
        {
            IsExamOverview = false;
            ExamStatusMessage = _engine.Session!.CurrentQuestion!.ConfirmedAnswer.HasValue
                ? "Ответ на этот вопрос уже зафиксирован. Нажмите Esc для возврата к перечню."
                : "Выберите вариант ответа и нажмите «Ответить».";
            RefreshExamPresentation();
        }
    }

    private void OpenOverviewQuestion(int index) => NavigateQuestion(index);

    private void OpenCurrentQuestion()
    {
        if (_engine?.Session?.CurrentQuestion is null)
            return;
        IsExamOverview = false;
        ExamStatusMessage = _engine.Session.CurrentQuestion.ConfirmedAnswer.HasValue
            ? "Ответ на этот вопрос уже зафиксирован. Нажмите Esc для возврата к перечню."
            : "Выберите вариант ответа и нажмите «Ответить».";
        RefreshExamPresentation();
    }

    private void ShowExamOverview()
    {
        if (_engine?.Session is null)
            return;
        IsExamOverview = true;
        ExamStatusMessage = "Выберите вопрос из перечня. Все вопросы доступны в произвольном порядке.";
        RefreshExamPresentation();
    }

    private void NavigateExamRelative(int offset)
    {
        if (_engine?.Session is null)
            return;
        var target = Math.Clamp(
            _engine.Session.CurrentQuestionIndex + offset,
            0,
            _engine.Session.ActiveQuestions.Count - 1);
        NavigateQuestion(target);
    }

    private void NavigateOverviewRelative(int offset)
    {
        if (_engine?.Session is null)
            return;
        var target = Math.Clamp(
            _engine.Session.CurrentQuestionIndex + offset,
            0,
            _engine.Session.ActiveQuestions.Count - 1);
        if (_engine.NavigateTo(target))
        {
            ExamStatusMessage = $"Выбран вопрос {target + 1}. Нажмите Enter или пробел, чтобы открыть.";
            RefreshExamPresentation();
        }
    }

    private void SelectAnswer(int number)
    {
        if (_engine?.SelectAnswer(number) == true)
        {
            ExamStatusMessage = $"Выбран вариант {number}. Для фиксации нажмите «Ответить».";
            RefreshExamPresentation();
        }
    }

    private async Task ConfirmAnswerAsync()
    {
        if (_engine is null || _handlingTransition)
            return;
        var status = _engine.ConfirmAnswer();
        ExamStatusMessage = status switch
        {
            ConfirmAnswerStatus.NoAnswerSelected => "Сначала выберите вариант ответа.",
            ConfirmAnswerStatus.AlreadyConfirmed => "Ответ на этот вопрос уже зафиксирован и не может быть изменён.",
            ConfirmAnswerStatus.ExamNotRunning => "Этап экзамена завершён.",
            _ => "Ответ зафиксирован."
        };
        RefreshExamPresentation();
        await HandleExamTransitionAsync();
        if (Page == PageKind.Exam && status == ConfirmAnswerStatus.Accepted)
        {
            IsExamOverview = true;
            ExamStatusMessage = "Ответ зафиксирован. Выберите следующий вопрос из перечня.";
            RefreshExamPresentation();
        }
    }

    private async Task HandleExamTransitionAsync()
    {
        if (_engine?.Session is null || _handlingTransition)
            return;
        _handlingTransition = true;
        try
        {
            if (_engine.Session.Stage == ExamStage.SupplementaryBriefing)
            {
                if (_bank is null)
                    throw new InvalidOperationException("Комплект вопросов недоступен.");
                _supplementaryQuestions = _selector.SelectSupplementary(
                    _bank.Questions,
                    "AB",
                    _engine.Session.ErrorGroups,
                    _engine.Session.MainQuestions.Select(state => state.Question.Id).ToArray());
                SupplementaryTitle = _supplementaryQuestions.Count == 5
                    ? "Дополнительный блок: 5 вопросов"
                    : "Дополнительные блоки: 10 вопросов";
                SupplementaryDetails =
                    $"Ошибки допущены в тематических группах: {string.Join(", ", _engine.Session.ErrorGroups)}. " +
                    $"На выполнение даётся {_engine.Session.ErrorGroups.Count * 5} минут. В дополнительном блоке ошибки не допускаются.";
                Page = PageKind.Supplementary;
            }
            else if (_engine.Session.Stage == ExamStage.Completed)
            {
                await CompleteExamAsync();
            }
        }
        finally
        {
            _handlingTransition = false;
        }
    }

    private void StartSupplementary()
    {
        if (_engine is null || _supplementaryQuestions.Count == 0)
            return;
        _engine.StartSupplementary(_supplementaryQuestions);
        IsExamOverview = true;
        ExamStatusMessage = "Дополнительный блок начат. Выберите вопрос из перечня.";
        Page = PageKind.Exam;
        RefreshExamPresentation();
    }

    private void RefreshExamPresentation()
    {
        var session = _engine?.Session;
        var state = session?.CurrentQuestion;
        if (session is null || state is null || _bank is null)
            return;

        StageCaption = session.Stage == ExamStage.Supplementary ? "ДОПОЛНИТЕЛЬНЫЙ БЛОК" : "ОСНОВНОЙ ЭКЗАМЕН";
        QuestionCounter = $"Вопрос {session.CurrentQuestionIndex + 1} из {session.ActiveQuestions.Count}";
        QuestionTitle = $"ВОПРОС {state.SequenceNumber}";
        QuestionTicketCaption = $"Билет {state.Question.TicketNumber} · вопрос {state.Question.QuestionNumber} · блок {state.Question.ThematicBlockId}";
        QuestionText = state.Question.QuestionText;
        RemainingTime = DurationText(session.CurrentStageRemaining);
        CurrentImage = LoadExamImage(state.Question.ImagePath);
        CanConfirm = state.PendingAnswer.HasValue && !state.ConfirmedAnswer.HasValue;
        OnPropertyChanged(nameof(TerminalText));
        OnPropertyChanged(nameof(CandidateText));
        OnPropertyChanged(nameof(ExamOverviewHeaderText));
        OnPropertyChanged(nameof(IsDemoQuestion));

        QuestionNavigation.Clear();
        for (var index = 0; index < session.ActiveQuestions.Count; index++)
        {
            var item = session.ActiveQuestions[index];
            var navigation = new QuestionNavigationItem
            {
                Index = index,
                Number = (index + 1).ToString()
            };
            if (index == session.CurrentQuestionIndex)
            {
                navigation.Background = LegacyServiceBlueBrush;
                navigation.Foreground = Brushes.White;
                navigation.Border = Brushes.Black;
            }
            else if (item.ConfirmedAnswer.HasValue)
            {
                navigation.Background = FrozenBrush(210, 210, 210);
                navigation.Border = FrozenBrush(120, 120, 120);
            }
            else if (item.PendingAnswer.HasValue)
            {
                navigation.Background = LegacyViewedBrush;
                navigation.Border = LegacyServiceBlueBrush;
            }
            else if (item.Progress == QuestionProgress.Viewed)
            {
                navigation.Background = FrozenBrush(247, 241, 202);
            }
            QuestionNavigation.Add(navigation);
        }

        OverviewQuestions.Clear();
        var columnCount = Math.Max(1, (session.ActiveQuestions.Count + 4) / 5);
        for (var row = 0; row < 5; row++)
        {
            for (var column = 0; column < columnCount; column++)
            {
                var index = column * 5 + row;
                if (index >= session.ActiveQuestions.Count)
                    continue;
                var item = session.ActiveQuestions[index];
                OverviewQuestions.Add(new ExamQuestionPreviewItem
                {
                    Index = index,
                    Number = item.SequenceNumber,
                    QuestionText = item.Question.QuestionText,
                    Image = LoadExamImage(item.Question.ImagePath),
                    ProgressText = item.Progress switch
                    {
                        QuestionProgress.Answered => "ОТВЕЧЕН",
                        QuestionProgress.Viewed => "ОТКРЫТ",
                        _ => string.Empty
                    },
                    ProgressBrush = item.Progress == QuestionProgress.Answered ? Brushes.DarkGreen : Brushes.DimGray,
                    Border = item.Question.GroupId switch
                    {
                        1 => FrozenBrush(20, 130, 58),
                        2 => FrozenBrush(151, 39, 156),
                        3 => FrozenBrush(35, 83, 190),
                        _ => FrozenBrush(180, 150, 0)
                    },
                    Background = index == session.CurrentQuestionIndex
                        ? LegacySelectedBrush
                        : item.Progress switch
                        {
                            QuestionProgress.Answered => LegacyAnsweredBrush,
                            QuestionProgress.Viewed => LegacyViewedBrush,
                            _ => Brushes.White
                        }
                });
            }
        }
        OnPropertyChanged(nameof(ExamOverviewColumnCount));

        AnswerChoices.Clear();
        for (var index = 0; index < state.Question.Answers.Count; index++)
        {
            var number = index + 1;
            var choice = new AnswerChoiceItem
            {
                Number = number,
                Text = state.Question.Answers[index],
                IsEnabled = !state.ConfirmedAnswer.HasValue
            };
            if (state.PendingAnswer == number)
            {
                choice.Background = state.ConfirmedAnswer.HasValue ? LegacyConfirmedBrush : LegacySelectedBrush;
                choice.Border = LegacyServiceBlueBrush;
            }
            AnswerChoices.Add(choice);
        }
    }

    private ImageSource? LoadExamImage(string? imagePath)
    {
        if (_bank is null || string.IsNullOrWhiteSpace(imagePath))
            return null;
        if (_examImageCache.TryGetValue(imagePath, out var cached))
            return cached;
        var image = QuestionImageLoader.Load(_bank.RootDirectory, imagePath);
        _examImageCache[imagePath] = image;
        return image;
    }

    private async Task CompleteExamAsync()
    {
        if (_engine?.Session is null || _bank is null || _store is null)
            return;
        _lastResult = _engine.BuildResult();
        _lastEnvelope = ExamSessionEnvelopeFactory.Create(
            _engine.Session,
            _deviceId,
            StudyDeviceKind.WindowsDesktop,
            _bank.Manifest.BankVersion,
            _bank.Manifest.BankSha256,
            RulesProfile);
        await _store.SaveCompletedSessionAsync(_userId, _lastEnvelope);

        var sessions = await _store.GetSessionsAsync(_userId);
        var profile = _profileBuilder.Build(sessions, DateTimeOffset.UtcNow);
        await _store.SaveLearningProfileAsync(_userId, profile);
        BuildResultPresentation(_lastResult, _lastEnvelope);
        Page = PageKind.Result;
        _ = SynchronizeAfterResultAsync();
    }

    private void BuildResultPresentation(ExamResult result, StudySessionEnvelope envelope)
    {
        var passed = result.Outcome == ExamOutcome.Passed;
        ResultTitle = passed ? "ЭКЗАМЕН СДАН" : "ЭКЗАМЕН НЕ СДАН";
        ResultBrush = passed ? TealBrush : RedBrush;
        ResultDetails =
            $"Основной блок: {result.MainCorrectCount}/{result.MainQuestionCount}, ошибок {result.MainErrorCount}\n" +
            $"Дополнительный блок: {result.SupplementaryCorrectCount}/{result.SupplementaryQuestionCount}, ошибок {result.SupplementaryErrorCount}\n" +
            $"Время: {DurationText(result.Elapsed)}" +
            (string.IsNullOrWhiteSpace(result.FailureReason) ? string.Empty : $"\n{result.FailureReason}");
        TelegramDeliveryStatus = _sync is null
            ? $"Результат сохранён на {DeviceCaption}. Telegram-отчёт ожидает настройки облачной синхронизации."
            : $"Результат сохранён на {DeviceCaption}. Telegram-отчёт пользователю @skeetels отправляется автоматически.";

        _reviewErrors.Clear();
        foreach (var state in result.IncorrectAnswers)
        {
            var selected = state.ConfirmedAnswer.HasValue && state.ConfirmedAnswer.Value <= state.Question.Answers.Count
                ? $"{state.ConfirmedAnswer}. {state.Question.Answers[state.ConfirmedAnswer.Value - 1]}"
                : "Нет подтверждённого ответа";
            var correct = $"{state.Question.CorrectAnswer}. {state.Question.Answers[state.Question.CorrectAnswer - 1]}";
            _reviewErrors.Add(new ReviewErrorItem(
                state.SequenceNumber,
                $"Билет {state.Question.TicketNumber} · вопрос {state.Question.QuestionNumber} · блок {state.Question.ThematicBlockId}",
                state.Question.QuestionText,
                selected,
                correct,
                state.Question.Explanation,
                state.Question.ImagePath,
                Math.Max(0, (long)(state.AnswerTime ?? TimeSpan.Zero).TotalMilliseconds)));
        }
        HasReviewErrors = _reviewErrors.Count > 0;
    }

    private async Task SynchronizeAfterResultAsync()
    {
        var result = await SyncNowAsync(showProgress: false);
        if (result?.Status == SyncResultStatus.Succeeded)
        {
            TelegramDeliveryStatus = $"Синхронизация завершена. Автоматический Telegram-отчёт принят сервером для @skeetels с пометкой «{DeviceCaption}».";
        }
        else if (result is not null)
        {
            TelegramDeliveryStatus = result.Message + " Telegram-отчёт остаётся в автоматической очереди.";
        }
    }

    private void OpenReview()
    {
        if (_reviewErrors.Count == 0)
            return;
        _reviewIndex = 0;
        RefreshReview();
        Page = PageKind.Review;
    }

    private void NavigateReview(int offset)
    {
        if (_reviewErrors.Count == 0)
            return;
        _reviewIndex = Math.Clamp(_reviewIndex + offset, 0, _reviewErrors.Count - 1);
        RefreshReview();
    }

    private void RefreshReview()
    {
        if (_bank is null || _reviewErrors.Count == 0)
            return;
        var item = _reviewErrors[_reviewIndex];
        ReviewPosition = $"Ошибка {_reviewIndex + 1} из {_reviewErrors.Count}";
        ReviewHeading = item.Heading;
        ReviewQuestionText = item.QuestionText;
        ReviewSelectedAnswer = item.SelectedAnswer;
        ReviewCorrectAnswer = item.CorrectAnswer;
        ReviewExplanation = string.IsNullOrWhiteSpace(item.Explanation) ? "Пояснение отсутствует." : item.Explanation;
        ReviewResponseTime = $"Время ответа: {DurationText(TimeSpan.FromMilliseconds(item.ResponseTimeMs))}";
        ReviewImage = QuestionImageLoader.Load(_bank.RootDirectory, item.ImagePath);
    }

    private async Task ReturnHomeAsync()
    {
        _engine = null;
        _readyQuestions = [];
        _supplementaryQuestions = [];
        _examImageCache.Clear();
        OverviewQuestions.Clear();
        QuestionNavigation.Clear();
        AnswerChoices.Clear();
        IsExamOverview = true;
        CurrentImage = null;
        await RefreshHomeStatisticsAsync();
        Page = PageKind.Home;
    }

    private async Task<SyncResult?> SyncNowAsync(bool showProgress)
    {
        if (_sync is null || _store is null)
        {
            if (showProgress)
                SyncStatus = "Облачная синхронизация не настроена.";
            return null;
        }
        if (!await _syncGate.WaitAsync(0))
            return null;
        try
        {
            if (showProgress)
                SyncStatus = "Синхронизация…";
            var result = await _sync.SyncAsync();
            SyncStatus = result.Message;
            _auth = _authStore is null ? _auth : await _authStore.LoadAsync();
            await RefreshHomeStatisticsAsync();
            return result;
        }
        catch (Exception exception)
        {
            SyncStatus = "Синхронизация отложена: " + SafeMessage(exception);
            return new SyncResult(SyncResultStatus.Offline, SyncStatus);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task RefreshHomeStatisticsAsync()
    {
        if (_store is null || _userId == Guid.Empty)
            return;
        var exams = (await _store.GetSessionsAsync(_userId))
            .Where(session => session.Mode == StudyMode.Exam)
            .OrderBy(session => session.CompletedAtUtc)
            .ToArray();
        if (exams.Length == 0)
        {
            HomeStatistics = "Завершённых экзаменов пока нет. Первый экзамен сформирует начальный профиль сложности.";
            return;
        }
        var passed = exams.Count(session => session.Outcome == StudyOutcome.Passed);
        var errorTickets = exams
            .SelectMany(session => session.Answers)
            .Where(answer => !answer.IsCorrect)
            .GroupBy(answer => answer.TicketNumber)
            .Select(group => new { Ticket = group.Key, Errors = group.Count() })
            .OrderByDescending(item => item.Errors)
            .ThenBy(item => item.Ticket)
            .Take(3)
            .Select(item => $"билет {item.Ticket} — {item.Errors}")
            .ToArray();
        HomeStatistics =
            $"Экзаменов: {exams.Length} · сдано: {passed} · не сдано: {exams.Length - passed}" +
            (errorTickets.Length == 0 ? "\nОшибок пока нет." : "\nЧаще всего ошибки: " + string.Join("; ", errorTickets) + ".");
    }

    private async Task SignOutAsync()
    {
        if (_authStore is null)
            return;
        if (_auth is null && !_configuration.IsCloudConfigured)
        {
            SyncStatus = "Приложение работает локально: облачный аккаунт ещё не настроен.";
            return;
        }
        try
        {
            if (_auth is not null && _authClient is not null)
                await _authClient.SignOutAsync(_auth);
        }
        catch (Exception exception)
        {
            SyncStatus = "Серверный выход не подтверждён: " + SafeMessage(exception);
        }
        await _authStore.ClearAsync();
        _auth = null;
        _userId = Guid.Empty;
        AccountCaption = string.Empty;
        Page = PageKind.Login;
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (string.IsNullOrWhiteSpace(_configuration.GitHubRepository))
        {
            if (!silent)
                UpdateStatus = "Репозиторий обновлений ещё не указан в клиентской конфигурации.";
            return;
        }
        try
        {
            if (!silent)
                UpdateStatus = "Проверка обновлений…";
            var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(2, 0, 0);
            _availableUpdate = await new ApplicationUpdateService().CheckGitHubAsync(
                _configuration.GitHubRepository,
                version);
            HasAvailableUpdate = _availableUpdate is not null;
            UpdateStatus = _availableUpdate is null
                ? "Установлена актуальная версия."
                : $"Доступна версия {_availableUpdate.AvailableVersion}. Установка выполняется только после подтверждения.";
        }
        catch (Exception exception)
        {
            if (!silent)
                UpdateStatus = "Не удалось проверить обновления: " + SafeMessage(exception);
        }
    }

    private async Task InstallAvailableUpdateAsync()
    {
        if (_availableUpdate is null)
            return;
        if (MessageBox.Show(
                $"Скачать и запустить установщик версии {_availableUpdate.AvailableVersion}?",
                "Обновление программы",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            UpdateStatus = "Загрузка и проверка SHA-256 установщика…";
            var updater = new ApplicationUpdateService();
            var installer = await updater.DownloadVerifiedInstallerAsync(
                _availableUpdate.Manifest,
                Path.Combine(_localDataRoot, "Updates"));
            Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true });
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            UpdateStatus = "Обновление отменено: " + SafeMessage(exception);
        }
    }

    private static string DurationText(TimeSpan value) =>
        $"{Math.Max(0, (int)value.TotalMinutes):00}:{Math.Max(0, value.Seconds):00}";

    private static string SafeMessage(Exception exception) => exception switch
    {
        SupabaseProtocolException => exception.Message,
        InvalidDataException => exception.Message,
        InvalidOperationException => exception.Message,
        _ => "непредвиденная ошибка; подробности записаны в журнал приложения"
    };

    private static Brush FrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
