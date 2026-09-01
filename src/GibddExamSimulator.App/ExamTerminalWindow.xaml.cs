using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using GibddExamSimulator.ViewModels;

namespace GibddExamSimulator;

public partial class ExamTerminalWindow : Window
{
    private bool _closingFromHost;

    public ExamTerminalWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => EnterTrueFullscreen();
    }

    public void CloseFromHost()
    {
        _closingFromHost = true;
        Close();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.Page != PageKind.Exam)
            return;
        if (e.Key is Key.Enter or Key.Space or Key.Escape or Key.Left or Key.Right or
            >= Key.D1 and <= Key.D5 or >= Key.NumPad1 and <= Key.NumPad5)
        {
            viewModel.HandleTerminalKey(e.Key);
            e.Handled = true;
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closingFromHost || DataContext is not MainViewModel { HasRunningExam: true } viewModel)
            return;
        e.Cancel = true;
        var answer = MessageBox.Show(
            "Экзамен ещё не завершён. Прервать попытку и вернуться на главную?",
            "Прерывание экзамена",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes)
            await viewModel.AbortActiveExamAsync();
    }

    private void EnterTrueFullscreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            WindowState = WindowState.Maximized;
            return;
        }

        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ??
                        System.Windows.Media.Matrix.Identity;
        var topLeft = transform.Transform(new Point(info.Monitor.Left, info.Monitor.Top));
        var bottomRight = transform.Transform(new Point(info.Monitor.Right, info.Monitor.Bottom));
        WindowState = WindowState.Normal;
        Left = topLeft.X;
        Top = topLeft.Y;
        Width = bottomRight.X - topLeft.X;
        Height = bottomRight.Y - topLeft.Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
