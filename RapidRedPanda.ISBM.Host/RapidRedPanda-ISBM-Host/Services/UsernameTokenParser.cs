using System.Globalization;
using System.Text.Json;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Validation;

namespace RapidRedPanda.ISBM.Host.Services;

public sealed class UsernameTokenParser : IUsernameTokenParser
{
    private const string UsernameTokenType = "UsernameToken";

    public IReadOnlyList<UsernameTokenInput> Parse(IReadOnlyList<SecurityTokenRequest>? securityTokens)
    {
        if (securityTokens is null || securityTokens.Count == 0)
        {
            return [];
        }

        Dictionary<string, UsernameTokenInput> distinctTokens = new(StringComparer.Ordinal);

        foreach (SecurityTokenRequest? tokenElement in securityTokens)
        {
            if (tokenElement is null)
            {
                throw new SecurityTokenValidationException("Each security token must be an object.");
            }

            UsernameTokenInput token = ParseUsernameToken(tokenElement);

            if (distinctTokens.TryGetValue(token.NormalizedUsername, out UsernameTokenInput? existingToken))
            {
                if (!string.Equals(existingToken.Password, token.Password, StringComparison.Ordinal))
                {
                    throw new SecurityTokenValidationException("Duplicate UsernameToken entries for the same username must not use different passwords.");
                }

                continue;
            }

            distinctTokens.Add(token.NormalizedUsername, token);
        }

        return distinctTokens.Values.ToArray();
    }

    public string NormalizeUsername(string username)
    {
        return username.Trim().ToUpper(CultureInfo.InvariantCulture);
    }

    private UsernameTokenInput ParseUsernameToken(SecurityTokenRequest tokenElement)
    {
        bool hasNestedUsernameToken = TryGetSingleNestedUsernameToken(tokenElement, out JsonElement nestedUsernameTokenElement);
        bool hasFlatShape = tokenElement.Type is not null ||
            tokenElement.Username is not null ||
            tokenElement.Password is not null;

        if (hasNestedUsernameToken && hasFlatShape)
        {
            throw new SecurityTokenValidationException("Security token entries must not mix flat and nested UsernameToken shapes.");
        }

        if (hasNestedUsernameToken)
        {
            return ParseNestedUsernameToken(nestedUsernameTokenElement);
        }

        if (tokenElement.ExtensionData is { Count: > 0 })
        {
            throw new SecurityTokenValidationException("Unsupported security token shape.");
        }

        return ParseFlatUsernameToken(tokenElement);
    }

    private UsernameTokenInput ParseFlatUsernameToken(SecurityTokenRequest tokenElement)
    {
        string tokenType = ReadRequiredString(tokenElement.Type, "type");
        if (!string.Equals(tokenType, UsernameTokenType, StringComparison.Ordinal))
        {
            throw new SecurityTokenValidationException("Only UsernameToken security tokens are supported in phase one.");
        }

        string username = ReadRequiredString(tokenElement.Username, "username");
        string password = ReadRequiredString(tokenElement.Password, "password");

        return CreateUsernameToken(username, password);
    }

    private UsernameTokenInput ParseNestedUsernameToken(JsonElement usernameTokenElement)
    {
        if (usernameTokenElement.ValueKind != JsonValueKind.Object)
        {
            throw new SecurityTokenValidationException("UsernameToken must be an object.");
        }

        string username = ReadRequiredString(usernameTokenElement, "username");
        string password = ReadRequiredString(usernameTokenElement, "password");

        return CreateUsernameToken(username, password);
    }

    private UsernameTokenInput CreateUsernameToken(string username, string password)
    {
        string normalizedUsername = NormalizeUsername(username);

        if (normalizedUsername.Length == 0)
        {
            throw new SecurityTokenValidationException("UsernameToken username must not be empty.");
        }

        if (username.Trim().Length > ChannelValidationRules.UsernameMaxLength || normalizedUsername.Length > ChannelValidationRules.UsernameMaxLength)
        {
            throw new SecurityTokenValidationException("UsernameToken username is too long.");
        }

        if (password.Length == 0)
        {
            throw new SecurityTokenValidationException("UsernameToken password must not be empty.");
        }

        return new UsernameTokenInput(username.Trim(), normalizedUsername, password);
    }

    private static string ReadRequiredString(string? value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SecurityTokenValidationException($"UsernameToken {propertyName} must be a non-empty string.");
        }

        return value;
    }

    private static string ReadRequiredString(JsonElement parent, string propertyName)
    {
        if (!TryGetPropertyCaseInsensitive(parent, propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            throw new SecurityTokenValidationException($"UsernameToken {propertyName} must be a non-empty string.");
        }

        string? text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new SecurityTokenValidationException($"UsernameToken {propertyName} must be a non-empty string.");
        }

        return text;
    }

    private static bool TryGetSingleNestedUsernameToken(SecurityTokenRequest tokenElement, out JsonElement usernameTokenElement)
    {
        usernameTokenElement = default;

        if (tokenElement.ExtensionData is null || tokenElement.ExtensionData.Count == 0)
        {
            return false;
        }

        JsonElement? nested = null;
        foreach ((string key, JsonElement value) in tokenElement.ExtensionData)
        {
            if (!string.Equals(key, UsernameTokenType, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityTokenValidationException("Unsupported security token shape.");
            }

            if (nested is not null)
            {
                throw new SecurityTokenValidationException("Security token entries must not contain duplicate UsernameToken properties.");
            }

            nested = value;
        }

        if (nested is null)
        {
            return false;
        }

        usernameTokenElement = nested.Value;
        return true;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement parent, string propertyName, out JsonElement value)
    {
        foreach (JsonProperty property in parent.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
