using RapidRedPanda.ISBM.Host.Contracts;

namespace RapidRedPanda.ISBM.Host.Services;

public interface IUsernameTokenParser
{
    IReadOnlyList<UsernameTokenInput> Parse(IReadOnlyList<SecurityTokenRequest>? securityTokens);

    string NormalizeUsername(string username);
}

public sealed record UsernameTokenInput(string Username, string NormalizedUsername, string Password);
