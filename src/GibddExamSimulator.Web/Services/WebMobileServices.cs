using System.Net.Http.Json;
using GibddExamSimulator.Application.StudySessions;
using GibddExamSimulator.Mobile.Shared.Services;

namespace GibddExamSimulator.Web.Services;

public sealed class WebMobileConfigurationProvider(HttpClient httpClient) : IMobileConfigurationProvider
{
    public async Task<MobileClientConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<MobileClientConfiguration>("client-settings.json", cancellationToken) ?? new();
}

public sealed class PwaMobilePlatform : IMobilePlatform
{
    public StudyDeviceKind DeviceKind => StudyDeviceKind.MobilePwa;
    public string DeviceLabel => "Телефон / PWA";
    public string AppVersion => "2.0.1";
    public bool SupportsInstallableUpdates => false;
    public Task OpenUriAsync(Uri uri) => Task.CompletedTask;
}
