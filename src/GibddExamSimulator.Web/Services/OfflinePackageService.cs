using Microsoft.JSInterop;
using GibddExamSimulator.Mobile.Shared.Services;

namespace GibddExamSimulator.Web.Services;

public sealed class OfflinePackageService(IJSRuntime javascript) : IMobileOfflinePackageService, IAsyncDisposable
{
    private DotNetObjectReference<OfflinePackageService>? _reference;
    public event Action<int, int>? ProgressChanged;
    public bool IsBundled => false;

    public async Task DownloadAsync(IEnumerable<string> imageUrls, CancellationToken cancellationToken = default)
    {
        _reference ??= DotNetObjectReference.Create(this);
        await javascript.InvokeVoidAsync(
            "gibddOffline.downloadImages",
            cancellationToken,
            imageUrls.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            _reference);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default) =>
        await javascript.InvokeVoidAsync("gibddOffline.clearImages", cancellationToken);

    public async Task CancelDownloadAsync(CancellationToken cancellationToken = default) =>
        await javascript.InvokeVoidAsync("gibddOffline.cancelDownload", cancellationToken);

    public async Task<MobileStorageEstimate?> EstimateAsync(CancellationToken cancellationToken = default) =>
        await javascript.InvokeAsync<MobileStorageEstimate?>("gibddOffline.estimateStorage", cancellationToken);

    public async Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        await javascript.InvokeAsync<int>("gibddOffline.countImages", cancellationToken);

    [JSInvokable]
    public Task ReportProgress(int completed, int total)
    {
        ProgressChanged?.Invoke(completed, total);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _reference?.Dispose();
        _reference = null;
        return ValueTask.CompletedTask;
    }
}
