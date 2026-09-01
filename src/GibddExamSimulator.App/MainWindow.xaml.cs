using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GibddExamSimulator.ViewModels;

namespace GibddExamSimulator;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal)
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };
    private MainViewModel? _subscribedViewModel;
    private ExamTerminalWindow? _examTerminal;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedViewModel is not null)
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribedViewModel = e.NewValue as MainViewModel;
        if (_subscribedViewModel is not null)
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        SynchronizeTerminalWindow();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Page))
            Dispatcher.BeginInvoke(SynchronizeTerminalWindow);
    }

    private void SynchronizeTerminalWindow()
    {
        if (ViewModel is null)
            return;
        var terminalPage = ViewModel.Page is PageKind.Exam or PageKind.Supplementary;
        if (terminalPage)
        {
            if (_examTerminal is null)
            {
                _examTerminal = new ExamTerminalWindow { DataContext = ViewModel };
                _examTerminal.Closed += OnExamTerminalClosed;
                _examTerminal.Show();
            }
            Hide();
            _examTerminal.Activate();
            return;
        }

        if (_examTerminal is not null)
        {
            var terminal = _examTerminal;
            _examTerminal = null;
            terminal.Closed -= OnExamTerminalClosed;
            terminal.CloseFromHost();
        }
        if (!IsVisible)
            Show();
        Activate();
    }

    private void OnExamTerminalClosed(object? sender, EventArgs e)
    {
        if (sender == _examTerminal)
            _examTerminal = null;
        if (!IsVisible)
            Show();
        Activate();
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
            return;
        LoginButton.IsEnabled = false;
        try
        {
            await ViewModel.SignInAsync(LoginPasswordBox.Password);
        }
        finally
        {
            LoginPasswordBox.Clear();
            LoginButton.IsEnabled = true;
        }
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (ViewModel is not null)
            await ViewModel.TickAsync();
    }

    private void PreviousQuestion_Click(object sender, RoutedEventArgs e) =>
        ViewModel?.HandleNavigationShortcut(-1);

    private void NextQuestion_Click(object sender, RoutedEventArgs e) =>
        ViewModel?.HandleNavigationShortcut(1);

    private void BackToResult_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
            ViewModel.ShowResultPage();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null)
            return;
        if (ViewModel.Page is PageKind.Exam or PageKind.Supplementary)
            return;
        var answer = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 1,
            Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3,
            Key.D4 or Key.NumPad4 => 4,
            Key.D5 or Key.NumPad5 => 5,
            _ => 0
        };
        if (answer > 0)
        {
            ViewModel.HandleDigitShortcut(answer);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (ViewModel.Page == PageKind.Login)
                LoginButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else
                ViewModel.HandleConfirmShortcut();
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            ViewModel.HandleNavigationShortcut(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            ViewModel.HandleNavigationShortcut(1);
            e.Handled = true;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (ViewModel?.HasRunningExam != true)
            return;
        var answer = MessageBox.Show(
            "Экзамен ещё не завершён. Закрыть программу и прервать попытку?",
            "Прерывание экзамена",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        ViewModel.InterruptActiveExam();
        _examTerminal?.CloseFromHost();
    }
}
