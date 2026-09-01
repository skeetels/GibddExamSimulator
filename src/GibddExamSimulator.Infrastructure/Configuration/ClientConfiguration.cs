using System.Text.Json;
using GibddExamSimulator.Sync;

namespace GibddExamSimulator.Configuration;

public sealed record ClientConfiguration
{
    public int ConfigVersion { get; init; } = 1;
    public string EnvironmentId { get; init; } = string.Empty;
    public string RepositoryOwner { get; init; } = string.Empty;
    public string RepositoryName { get; init; } = string.Empty;
    public string RepositoryUrl { get; init; } = string.Empty;
    public string ReleaseManifestUrl { get; init; } = string.Empty;
    public string PagesBaseUrl { get; init; } = string.Empty;
    public string SyncApiBaseUrl { get; init; } = string.Empty;
    public string SupabaseUrl { get; init; } = string.Empty;
    public string SupabasePublishableKey { get; init; } = string.Empty;
    public string TelegramBotUsername { get; init; } = string.Empty;
    public string ConfigSha256 { get; init; } = string.Empty;
    public string GitHubRepository => string.IsNullOrWhiteSpace(RepositoryOwner) || string.IsNullOrWhiteSpace(RepositoryName)
        ? string.Empty
        : $"{RepositoryOwner}/{RepositoryName}";
    public bool IsCloudConfigured => Uri.TryCreate(SupabaseUrl, UriKind.Absolute, out var uri) &&
                                      uri.Scheme == Uri.UriSchemeHttps &&
                                      Uri.TryCreate(SyncApiBaseUrl, UriKind.Absolute, out var syncUri) &&
                                      syncUri.Scheme == Uri.UriSchemeHttps &&
                                      !string.IsNullOrWhiteSpace(SupabasePublishableKey) &&
                                      !string.IsNullOrWhiteSpace(EnvironmentId);

    public SupabaseClientOptions ToSupabaseOptions()
    {
        if (!Uri.TryCreate(SupabaseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("SUPABASE_URL is not configured.");
        return new SupabaseClientOptions
        {
            ProjectUrl = uri,
            PublishableKey = SupabasePublishableKey,
            SyncApiBaseUrl = new Uri(SyncApiBaseUrl, UriKind.Absolute),
            EnvironmentId = EnvironmentId
        };
    }
}

public sealed class ClientConfigurationLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _baseDirectory;

    public ClientConfigurationLoader(string baseDirectory)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory);
    }

    public async Task<ClientConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_baseDirectory, "Configuration", "client-settings.json");
        ClientConfiguration file = new();
        if (File.Exists(path))
        {
            await using var stream = File.OpenRead(path);
            file = await JsonSerializer.DeserializeAsync<ClientConfiguration>(stream, JsonOptions, cancellationToken)
                   ?? new ClientConfiguration();
        }

        return file with
        {
            SupabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? file.SupabaseUrl,
            SupabasePublishableKey = Environment.GetEnvironmentVariable("SUPABASE_PUBLISHABLE_KEY") ?? file.SupabasePublishableKey,
            SyncApiBaseUrl = Environment.GetEnvironmentVariable("SYNC_API_BASE_URL") ?? file.SyncApiBaseUrl,
            EnvironmentId = Environment.GetEnvironmentVariable("DEPLOYMENT_ENVIRONMENT_ID") ?? file.EnvironmentId
        };
    }
}
