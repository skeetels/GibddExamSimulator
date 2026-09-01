using GibddExamSimulator.Application.StudySessions;
using GibddExamSimulator.Models;

namespace GibddExamSimulator.Mobile.Shared.Services;

public sealed record MobileQuestionBankManifest
{
    public int SchemaVersion { get; init; }
    public string BankVersion { get; init; } = string.Empty;
    public string BankSha256 { get; init; } = string.Empty;
    public int QuestionCount { get; init; }
    public int TicketCount { get; init; }
    public int BlockCount { get; init; }
    public int ImageCount { get; init; }
    public long ImageBytes { get; init; }
}

public sealed record MobileQuestionBank(
    MobileQuestionBankManifest Manifest,
    IReadOnlyList<Question> Questions);

public sealed record MobileStorageEstimate(long Usage, long Quota, long Available);

public interface IMobileQuestionBankLoader
{
    Task<MobileQuestionBank> LoadAsync(CancellationToken cancellationToken = default);
}

public interface IMobileConfigurationProvider
{
    Task<MobileClientConfiguration> LoadAsync(CancellationToken cancellationToken = default);
}

public interface IMobileOfflinePackageService
{
    event Action<int, int>? ProgressChanged;
    bool IsBundled { get; }
    Task DownloadAsync(IEnumerable<string> imageUrls, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    Task CancelDownloadAsync(CancellationToken cancellationToken = default);
    Task<MobileStorageEstimate?> EstimateAsync(CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}

public interface IMobilePlatform
{
    StudyDeviceKind DeviceKind { get; }
    string DeviceLabel { get; }
    string AppVersion { get; }
    bool SupportsInstallableUpdates { get; }
    Task OpenUriAsync(Uri uri);
}

public interface IMobileQrScanner
{
    bool IsSupported { get; }
    Task<string> ScanAsync(CancellationToken cancellationToken = default);
}
