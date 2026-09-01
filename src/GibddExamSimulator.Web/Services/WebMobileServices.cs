using System.Net.Http.Json;
using GibddExamSimulator.Application.StudySessions;
using GibddExamSimulator.Mobile.Shared.Services;
using Microsoft.JSInterop;

namespace GibddExamSimulator.Web.Services;

public sealed class WebMobileConfigurationProvider(HttpClient httpClient) : IMobileConfigurationProvider
{
    public async Task<MobileClientConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<MobileClientConfiguration>("client-settings.json", cancellationToken) ?? new();
}

public sealed class PwaMobilePlatform(IJSRuntime javascript) : IMobilePlatform
{
    public StudyDeviceKind DeviceKind => StudyDeviceKind.MobilePwa;
    public string DeviceLabel => "Телефон / PWA";
    public string AppVersion => "2.0.3";
    public bool SupportsInstallableUpdates => false;
    public async Task OpenUriAsync(Uri uri) =>
        await javascript.InvokeVoidAsync("open", uri.AbsoluteUri, "_blank", "noopener,noreferrer");
}

public sealed class WebQrScanner(IJSRuntime javascript) : IMobileQrScanner
{
    public bool IsSupported => true;

    public async Task<string> ScanAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await javascript.InvokeAsync<string>("gibddQr.scan", cancellationToken);
        }
        catch (JSException exception) when (exception.Message.Contains("camera_not_supported", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("На этом телефоне камера в браузере недоступна. Введите короткий код вручную.");
        }
        catch (JSException)
        {
            throw new InvalidOperationException("Камера не открылась. Разрешите доступ или введите короткий код вручную.");
        }
    }
}
