using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GibddExamSimulator.Services;

public sealed record UpdateManifest(
    string Version,
    string DownloadUrl,
    string Sha256,
    string ReleaseNotes,
    DateTimeOffset PublishedAtUtc,
    string ReleasePageUrl);

public sealed record UpdateCheckResult(Version CurrentVersion, Version AvailableVersion, UpdateManifest Manifest);

public sealed partial class ApplicationUpdateService
{
    private const long MaximumInstallerBytes = 300L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public ApplicationUpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
    }

    public async Task<UpdateCheckResult?> CheckGitHubAsync(
        string repository,
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        var normalizedRepository = ValidateRepository(repository);
        using var request = CreateGitHubRequest(
            HttpMethod.Get,
            $"https://api.github.com/repos/{normalizedRepository}/releases/latest");
        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        using var releaseJson = await ReadSuccessfulJsonAsync(response, "получить последнюю версию", cancellationToken);

        var tag = releaseJson.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
        var availableVersion = ParseVersion(tag);
        if (availableVersion <= currentVersion)
            return null;

        var releasePage = releaseJson.RootElement.TryGetProperty("html_url", out var pageValue)
            ? pageValue.GetString() ?? string.Empty
            : string.Empty;
        var releaseBody = releaseJson.RootElement.TryGetProperty("body", out var bodyValue)
            ? bodyValue.GetString() ?? string.Empty
            : string.Empty;
        string? manifestUrl = null;
        foreach (var asset in releaseJson.RootElement.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), "update-manifest.json", StringComparison.OrdinalIgnoreCase))
                continue;
            manifestUrl = asset.TryGetProperty("browser_download_url", out var manifestUrlValue)
                ? manifestUrlValue.GetString()
                : null;
            break;
        }

        UpdateManifest manifest;
        if (!string.IsNullOrWhiteSpace(manifestUrl))
        {
            ValidateHttpsUrl(manifestUrl);
            using var manifestRequest = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
            using var manifestResponse = await SendAsync(manifestRequest, cancellationToken);
            if (!manifestResponse.IsSuccessStatusCode)
                throw new InvalidOperationException($"Не удалось загрузить манифест обновления: HTTP {(int)manifestResponse.StatusCode}.");
            await using var stream = await manifestResponse.Content.ReadAsStreamAsync(cancellationToken);
            manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, JsonOptions, cancellationToken)
                       ?? throw new InvalidDataException("Манифест обновления пуст.");
        }
        else
        {
            manifest = BuildManifestFromReleaseAssets(releaseJson.RootElement, availableVersion, releaseBody, releasePage);
        }

        ValidateManifest(manifest, availableVersion);
        return new UpdateCheckResult(currentVersion, availableVersion, manifest);
    }

    public async Task<string> DownloadVerifiedInstallerAsync(
        UpdateManifest manifest,
        string updatesDirectory,
        CancellationToken cancellationToken = default)
    {
        ValidateManifest(manifest, ParseVersion(manifest.Version));
        Directory.CreateDirectory(updatesDirectory);
        var fileName = Path.GetFileName(new Uri(manifest.DownloadUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            fileName = $"GibddExamSimulator-Setup-{manifest.Version}-win-x64.exe";
        foreach (var invalid in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalid, '_');

        var target = Path.GetFullPath(Path.Combine(updatesDirectory, fileName));
        var root = Path.GetFullPath(updatesDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Недопустимое имя файла обновления.");

        if (File.Exists(target) && string.Equals(await ComputeSha256Async(target, cancellationToken), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            return target;

        var partial = target + ".partial";
        if (File.Exists(partial))
            File.Delete(partial);

        using var response = await _httpClient.GetAsync(manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Не удалось загрузить обновление: HTTP {(int)response.StatusCode}.");
        if (response.Content.Headers.ContentLength is > MaximumInstallerBytes)
            throw new InvalidDataException("Установщик обновления превышает допустимые 300 МБ.");

        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > MaximumInstallerBytes)
                    throw new InvalidDataException("Установщик обновления превышает допустимые 300 МБ.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch
        {
            if (File.Exists(partial))
                File.Delete(partial);
            throw;
        }

        var actualHash = await ComputeSha256Async(partial, cancellationToken);
        if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partial);
            throw new InvalidDataException("SHA-256 установщика не совпадает с опубликованным манифестом. Обновление отменено.");
        }

        File.Move(partial, target, true);
        return target;
    }

    public static Version ParseVersion(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
            normalized = normalized[1..];
        var separator = normalized.IndexOfAny(['-', '+']);
        if (separator >= 0)
            normalized = normalized[..separator];
        return Version.TryParse(normalized, out var version)
            ? version
            : throw new InvalidDataException($"Некорректная версия обновления: {value}");
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static UpdateManifest BuildManifestFromReleaseAssets(
        JsonElement release,
        Version version,
        string releaseNotes,
        string releasePage)
    {
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;
            var url = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
            var digest = asset.TryGetProperty("digest", out var digestValue)
                ? digestValue.GetString() ?? string.Empty
                : string.Empty;
            if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                continue;
            return new UpdateManifest(
                version.ToString(3),
                url,
                digest[7..],
                releaseNotes,
                DateTimeOffset.UtcNow,
                releasePage);
        }
        throw new InvalidDataException("В релизе нет update-manifest.json или установщика с SHA-256.");
    }

    private static void ValidateManifest(UpdateManifest manifest, Version expectedVersion)
    {
        var manifestVersion = ParseVersion(manifest.Version);
        if (manifestVersion != expectedVersion)
            throw new InvalidDataException("Версия манифеста не совпадает с версией релиза.");
        ValidateHttpsUrl(manifest.DownloadUrl);
        if (!Sha256Pattern().IsMatch(manifest.Sha256))
            throw new InvalidDataException("В манифесте отсутствует корректная контрольная сумма SHA-256.");
    }

    private static string ValidateRepository(string repository)
    {
        var value = repository.Trim();
        if (!RepositoryPattern().IsMatch(value))
            throw new InvalidOperationException("Репозиторий обновлений указывается в формате владелец/репозиторий.");
        return value;
    }

    private static void ValidateHttpsUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Обновления разрешено загружать только по HTTPS.");
    }

    private static HttpRequestMessage CreateGitHubRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("GibddExamSimulator-Updater/1.1");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Сервер обновлений не ответил вовремя.");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Нет связи с сервером обновлений.");
        }
    }

    private static async Task<JsonDocument> ReadSuccessfulJsonAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Не удалось {operation}: HTTP {(int)response.StatusCode}.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();

    [GeneratedRegex("^[0-9A-Fa-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
