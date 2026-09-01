using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GibddExamSimulator.Application.Storage;

namespace GibddExamSimulator.Sync;

public sealed class SupabaseAuthClient : IAuthClient
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseClientOptions _options;
    private readonly TimeProvider _timeProvider;

    public SupabaseAuthClient(
        SupabaseClientOptions options,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null)
    {
        options.Validate();
        _options = options;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AuthSession> SignInWithPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Введите email и пароль.");
        using var request = CreateRequest(
            HttpMethod.Post,
            "auth/v1/token?grant_type=password",
            new { email = email.Trim(), password });
        return await SendTokenRequestAsync(request, cancellationToken);
    }

    public async Task<AuthSession> RefreshAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(session.RefreshToken))
            throw new InvalidOperationException("Сессия входа истекла. Войдите повторно.");
        using var request = CreateRequest(
            HttpMethod.Post,
            "auth/v1/token?grant_type=refresh_token",
            new { refresh_token = session.RefreshToken });
        return await SendTokenRequestAsync(request, cancellationToken);
    }

    public async Task SignOutAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        using var request = CreateRequest(HttpMethod.Post, "auth/v1/logout", payload: null);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            throw await CreateProtocolExceptionAsync(response, "Не удалось завершить облачную сессию.", cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, object? payload)
    {
        var request = new HttpRequestMessage(method, _options.Resolve(relativePath));
        request.Headers.Add("apikey", _options.PublishableKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (payload is not null)
            request.Content = JsonContent.Create(payload);
        return request;
    }

    private async Task<AuthSession> SendTokenRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreateProtocolExceptionAsync(response, "Не удалось выполнить вход в Supabase.", cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var accessToken = RequiredString(root, "access_token");
        var refreshToken = RequiredString(root, "refresh_token");
        var expiresIn = root.TryGetProperty("expires_in", out var expiresValue) && expiresValue.TryGetInt32(out var seconds)
            ? Math.Clamp(seconds, 60, 86_400)
            : 3600;
        if (!root.TryGetProperty("user", out var user) ||
            !Guid.TryParse(RequiredString(user, "id"), out var userId))
            throw new SupabaseProtocolException("Supabase returned an invalid user identifier.");
        var email = user.TryGetProperty("email", out var emailValue) ? emailValue.GetString() ?? string.Empty : string.Empty;
        return new AuthSession(
            userId,
            email,
            accessToken,
            refreshToken,
            _timeProvider.GetUtcNow().AddSeconds(expiresIn));
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || string.IsNullOrWhiteSpace(value.GetString()))
            throw new SupabaseProtocolException($"Supabase response does not contain {propertyName}.");
        return value.GetString()!;
    }

    internal static async Task<SupabaseProtocolException> CreateProtocolExceptionAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken)
    {
        string? detail = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            foreach (var name in new[] { "error_description", "msg", "message", "error" })
            {
                if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    detail = value.GetString();
                    break;
                }
            }
        }
        catch (JsonException)
        {
            // The bounded fallback avoids echoing an arbitrary server body.
        }
        return new SupabaseProtocolException(
            string.IsNullOrWhiteSpace(detail) ? fallback : detail,
            (int)response.StatusCode);
    }
}
