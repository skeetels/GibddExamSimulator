using GibddExamSimulator.Sync;

namespace GibddExamSimulator.Mobile.Shared.Services;

public sealed record MobileClientConfiguration
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
            throw new InvalidOperationException("SUPABASE_URL не настроен.");
        return new SupabaseClientOptions
        {
            ProjectUrl = uri,
            PublishableKey = SupabasePublishableKey,
            SyncApiBaseUrl = new Uri(SyncApiBaseUrl, UriKind.Absolute),
            EnvironmentId = EnvironmentId
        };
    }
}
