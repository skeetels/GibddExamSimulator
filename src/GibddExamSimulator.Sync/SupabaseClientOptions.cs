namespace GibddExamSimulator.Sync;

public sealed record SupabaseClientOptions
{
    public required Uri ProjectUrl { get; init; }
    public required string PublishableKey { get; init; }

    public void Validate()
    {
        if (!ProjectUrl.IsAbsoluteUri || ProjectUrl.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("SUPABASE_URL must be an absolute HTTPS URL.");
        if (string.IsNullOrWhiteSpace(PublishableKey) || PublishableKey.Length > 4096)
            throw new InvalidOperationException("SUPABASE_PUBLISHABLE_KEY is not configured.");
        if (PublishableKey.StartsWith("sb_secret_", StringComparison.OrdinalIgnoreCase) ||
            PublishableKey.Contains("service_role", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A secret/service-role Supabase key must never be used by a client application.");
    }

    public Uri Resolve(string relativePath) => new(ProjectUrl, relativePath.TrimStart('/'));
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
