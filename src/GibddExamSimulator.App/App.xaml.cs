using System.IO;
using System.Windows;
using System.Windows.Threading;
using GibddExamSimulator.ViewModels;

namespace GibddExamSimulator;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        var viewModel = new MainViewModel();
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();
        await viewModel.InitializeAsync();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteLocalCrashLog(e.Exception);
        MessageBox.Show(
            "Произошла непредвиденная ошибка. Локальные подтверждённые результаты не удалены.",
            "Тренажёр ГИБДД",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void WriteLocalCrashLog(Exception exception)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GibddExamSimulator",
                "Logs");
            Directory.CreateDirectory(root);
            var line = $"{DateTimeOffset.UtcNow:O} | {exception.GetType().Name} | {exception.Message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(root, "app.log"), line);
        }
        catch
        {
            // A logging failure must not replace the original UI error.
        }
    }
}
