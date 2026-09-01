using System.Text.Json;
using GibddExamSimulator.Sync;

namespace GibddExamSimulator.Configuration;

public sealed record ClientConfiguration
{
    public string SupabaseUrl { get; init; } = string.Empty;
    public string SupabasePublishableKey { get; init; } = string.Empty;
    public string GitHubRepository { get; init; } = string.Empty;
    public bool IsCloudConfigured => Uri.TryCreate(SupabaseUrl, UriKind.Absolute, out var uri) &&
                                     uri.Scheme == Uri.UriSchemeHttps &&
                                     !string.IsNullOrWhiteSpace(SupabasePublishableKey);

    public SupabaseClientOptions ToSupabaseOptions()
    {
        if (!Uri.TryCreate(SupabaseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("SUPABASE_URL is not configured.");
        return new SupabaseClientOptions
        {
            ProjectUrl = uri,
            PublishableKey = SupabasePublishableKey
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
            GitHubRepository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") ?? file.GitHubRepository
        };
    }
}
