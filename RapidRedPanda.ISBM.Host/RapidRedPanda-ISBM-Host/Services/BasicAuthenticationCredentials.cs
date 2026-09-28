using System.Net.Http.Headers;
using System.Text;
using RapidRedPanda.ISBM.Host.Data;

namespace RapidRedPanda.ISBM.Host.Services;

public static class BasicAuthenticationCredentials
{
    public static ChannelCredentials? Read(HttpRequest request, IUsernameTokenParser usernameTokenParser)
    {
        string? value = request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value) ||
            !AuthenticationHeaderValue.TryParse(value, out AuthenticationHeaderValue? header) ||
            !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter))
        {
            return null;
        }

        try
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
            int separator = decoded.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
            {
                return null;
            }

            string username = decoded[..separator];
            string password = decoded[(separator + 1)..];
            return string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)
                ? null
                : new ChannelCredentials(usernameTokenParser.NormalizeUsername(username), password);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
