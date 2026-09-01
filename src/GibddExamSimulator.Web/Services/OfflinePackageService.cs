using Microsoft.JSInterop;

namespace GibddExamSimulator.Web.Services;

public sealed class OfflinePackageService(IJSRuntime javascript) : IAsyncDisposable
{
    private DotNetObjectReference<OfflinePackageService>? _reference;
    public event Action<int, int>? ProgressChanged;

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

    public async Task<BrowserStorageEstimate?> EstimateAsync(CancellationToken cancellationToken = default) =>
        await javascript.InvokeAsync<BrowserStorageEstimate?>("gibddOffline.estimateStorage", cancellationToken);

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

public sealed record BrowserStorageEstimate(long Usage, long Quota, long Available);
