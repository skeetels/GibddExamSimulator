namespace GibddExamSimulator.Sync;

public sealed record SupabaseClientOptions
{
    public required Uri ProjectUrl { get; init; }
    public required string PublishableKey { get; init; }
    public Uri? SyncApiBaseUrl { get; init; }
    public string EnvironmentId { get; init; } = "production";

    public void Validate()
    {
        if (!ProjectUrl.IsAbsoluteUri || ProjectUrl.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("SUPABASE_URL must be an absolute HTTPS URL.");
        if (string.IsNullOrWhiteSpace(PublishableKey) || PublishableKey.Length > 4096)
            throw new InvalidOperationException("SUPABASE_PUBLISHABLE_KEY is not configured.");
        if (PublishableKey.StartsWith("sb_secret_", StringComparison.OrdinalIgnoreCase) ||
            PublishableKey.Contains("service_role", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A secret/service-role Supabase key must never be used by a client application.");
        if (SyncApiBaseUrl is not null &&
            (!SyncApiBaseUrl.IsAbsoluteUri || SyncApiBaseUrl.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("SYNC_API_BASE_URL must be an absolute HTTPS URL.");
        if (string.IsNullOrWhiteSpace(EnvironmentId) || EnvironmentId.Length > 80)
            throw new InvalidOperationException("ENVIRONMENT_ID is not configured.");
    }

    public Uri Resolve(string relativePath) => new(ProjectUrl, relativePath.TrimStart('/'));

    public Uri ResolveSyncApi(string relativePath)
    {
        var root = SyncApiBaseUrl ?? Resolve("functions/v1/device-api/");
        var normalizedRoot = root.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? root
            : new Uri(root.AbsoluteUri + '/', UriKind.Absolute);
        return new Uri(normalizedRoot, relativePath.TrimStart('/'));
    }
}

public sealed class SupabaseProtocolException : InvalidOperationException
{
    public SupabaseProtocolException(string message, int? statusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
