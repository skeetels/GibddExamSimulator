using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GibddExamSimulator.Mobile.Shared.Services;

public sealed record MobileReleaseUpdate(Version Version, Uri DownloadUri, Uri ReleaseUri, string Notes);

public sealed partial class MobileReleaseUpdateService(HttpClient? httpClient = null)
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(25) };

    public async Task<MobileReleaseUpdate?> CheckAsync(
        string repository,
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        repository = repository.Trim();
        if (!RepositoryPattern().IsMatch(repository))
            throw new InvalidOperationException("Репозиторий обновлений не настроен.");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{repository}/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("GibddExamSimulator-Android-Updater/2.0.1");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Сервер обновлений вернул HTTP {(int)response.StatusCode}.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var normalized = tag.TrimStart('v', 'V').Split('-', '+')[0];
        if (!Version.TryParse(normalized, out var available) || available <= currentVersion)
            return null;

        Uri? apk = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (!name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                continue;
            var value = asset.GetProperty("browser_download_url").GetString();
            if (Uri.TryCreate(value, UriKind.Absolute, out var candidate) && candidate.Scheme == Uri.UriSchemeHttps)
            {
                apk = candidate;
                if (name.Contains("android", StringComparison.OrdinalIgnoreCase))
                    break;
            }
        }
        if (apk is null)
            throw new InvalidDataException("В новом релизе отсутствует Android APK.");

        var releaseValue = root.GetProperty("html_url").GetString();
        if (!Uri.TryCreate(releaseValue, UriKind.Absolute, out var release) || release.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Релиз содержит некорректную ссылку.");
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty;
        return new MobileReleaseUpdate(available, apk, release, notes);
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();
}
