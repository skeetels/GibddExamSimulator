namespace GibddExamSimulator.Application.Synchronization;

public sealed record PairingInvitation(Guid PairingId, string OneTimeSecret, string EnvironmentId)
{
    public static PairingInvitation Parse(string payload, string expectedEnvironmentId)
    {
        if (!Uri.TryCreate(payload?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("QR-код не относится к приложению. Покажите новый код на компьютере.");

        var query = ParseQuery(uri.Query);
        if (!query.TryGetValue("v", out var version) || version != "1" ||
            !query.TryGetValue("id", out var rawId) || !Guid.TryParse(rawId, out var pairingId) ||
            pairingId == Guid.Empty ||
            !query.TryGetValue("secret", out var secret) || secret.Length is < 40 or > 1024 ||
            !query.TryGetValue("env", out var environmentId) || string.IsNullOrWhiteSpace(environmentId))
            throw new InvalidDataException("QR-код повреждён или уже недействителен.");

        if (!string.Equals(environmentId, expectedEnvironmentId, StringComparison.Ordinal))
            throw new InvalidDataException("Этот QR-код выпущен для другой версии приложения.");
        if (secret.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new InvalidDataException("QR-код повреждён или уже недействителен.");
        return new PairingInvitation(pairingId, secret, environmentId);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
                continue;
            values[Uri.UnescapeDataString(pair[0])] = Uri.UnescapeDataString(pair[1].Replace('+', ' '));
        }
        return values;
    }
}
