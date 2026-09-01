using GibddExamSimulator.Mobile.Shared.Services;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace GibddExamSimulator.Android;

public sealed class AndroidQrScanner : IMobileQrScanner
{
    public bool IsSupported => true;

    public async Task<string> ScanAsync(CancellationToken cancellationToken = default)
    {
        var permission = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (permission != PermissionStatus.Granted)
            permission = await Permissions.RequestAsync<Permissions.Camera>();
        if (permission != PermissionStatus.Granted)
            throw new InvalidOperationException("Доступ к камере не выдан. Разрешите камеру и нажмите «Открыть камеру» ещё раз.");

        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
            var root = window?.Page ?? throw new InvalidOperationException("Не удалось открыть камеру.");
            var page = new QrScannerPage(completion);
            await root.Navigation.PushModalAsync(page, animated: true);
        });

        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return await completion.Task;
    }

    private sealed class QrScannerPage : ContentPage
    {
        private readonly TaskCompletionSource<string> _completion;
        private readonly CameraBarcodeReaderView _camera;
        private bool _completed;

        public QrScannerPage(TaskCompletionSource<string> completion)
        {
            _completion = completion;
            Title = "Сканирование QR-кода";
            BackgroundColor = Colors.Black;
            _camera = new CameraBarcodeReaderView
            {
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Options = new BarcodeReaderOptions
                {
                    Formats = BarcodeFormats.TwoDimensional,
                    AutoRotate = true,
                    Multiple = false,
                    TryHarder = true
                }
            };
            _camera.BarcodesDetected += OnBarcodesDetected;

            var hint = new Label
            {
                Text = "Наведите камеру на QR-код на экране компьютера",
                TextColor = Colors.White,
                FontSize = 19,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(24, 18)
            };
            var cancel = new Button
            {
                Text = "Отмена",
                BackgroundColor = Color.FromArgb("#F2F2F2"),
                TextColor = Color.FromArgb("#0F3452"),
                Margin = new Thickness(24, 12, 24, 26),
                HeightRequest = 54
            };
            cancel.Clicked += async (_, _) => await CancelAsync();

            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto)
                },
                Children = { hint, _camera, cancel }
            };
            Grid.SetRow(_camera, 1);
            Grid.SetRow(cancel, 2);
        }

        private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs args)
        {
            var value = args.Results.FirstOrDefault()?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(value) || _completed)
                return;
            _completed = true;
            _camera.IsDetecting = false;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                _completion.TrySetResult(value);
                await Navigation.PopModalAsync(animated: true);
            });
        }

        private async Task CancelAsync()
        {
            if (_completed)
                return;
            _completed = true;
            _camera.IsDetecting = false;
            _completion.TrySetCanceled();
            await Navigation.PopModalAsync(animated: true);
        }

        protected override bool OnBackButtonPressed()
        {
            _ = CancelAsync();
            return true;
        }

        protected override void OnDisappearing()
        {
            _camera.BarcodesDetected -= OnBarcodesDetected;
            if (!_completed)
                _completion.TrySetCanceled();
            base.OnDisappearing();
        }
    }
}
