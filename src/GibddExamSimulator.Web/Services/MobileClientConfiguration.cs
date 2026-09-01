using GibddExamSimulator.Sync;

namespace GibddExamSimulator.Web.Services;

public sealed record MobileClientConfiguration
{
    public string SupabaseUrl { get; init; } = string.Empty;
    public string SupabasePublishableKey { get; init; } = string.Empty;
    public bool IsCloudConfigured => Uri.TryCreate(SupabaseUrl, UriKind.Absolute, out var uri) &&
                                     uri.Scheme == Uri.UriSchemeHttps &&
                                     !string.IsNullOrWhiteSpace(SupabasePublishableKey);

    public SupabaseClientOptions ToSupabaseOptions()
    {
        if (!Uri.TryCreate(SupabaseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("SUPABASE_URL не настроен.");
        return new SupabaseClientOptions { ProjectUrl = uri, PublishableKey = SupabasePublishableKey };
    }
}
