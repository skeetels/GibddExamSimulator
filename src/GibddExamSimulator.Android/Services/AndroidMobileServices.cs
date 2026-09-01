using System.Text.Json;
using GibddExamSimulator.Application.StudySessions;
using GibddExamSimulator.Mobile.Shared.Services;

namespace GibddExamSimulator.Android;

public sealed class AndroidConfigurationProvider : IMobileConfigurationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<MobileClientConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync("client-settings.json");
        return await JsonSerializer.DeserializeAsync<MobileClientConfiguration>(stream, JsonOptions, cancellationToken) ?? new();
    }
}

public sealed class AndroidQuestionBankLoader : IMobileQuestionBankLoader
{
    public async Task<MobileQuestionBank> LoadAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await ReadPackageFileAsync("wwwroot/question-bank/ab/bank-manifest.json", cancellationToken);
        var questions = await ReadPackageFileAsync("wwwroot/question-bank/ab/official-questions.json", cancellationToken);
        return MobileQuestionBankParser.Parse(manifest, questions);
    }

    private static async Task<byte[]> ReadPackageFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync(path);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }
}

public sealed class AndroidOfflinePackageService : IMobileOfflinePackageService
{
    public event Action<int, int>? ProgressChanged;
    public bool IsBundled => true;

    public Task DownloadAsync(IEnumerable<string> imageUrls, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var total = imageUrls.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        ProgressChanged?.Invoke(total, total);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CancelDownloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<MobileStorageEstimate?> EstimateAsync(CancellationToken cancellationToken = default) => Task.FromResult<MobileStorageEstimate?>(null);
    public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(548);
}

public sealed class AndroidMobilePlatform : IMobilePlatform
{
    public StudyDeviceKind DeviceKind => StudyDeviceKind.AndroidApp;
    public string DeviceLabel => "Телефон / APK";
    public string AppVersion => AppInfo.Current.VersionString;
    public bool SupportsInstallableUpdates => true;
    public async Task OpenUriAsync(Uri uri) => await Launcher.Default.OpenAsync(uri);
}
