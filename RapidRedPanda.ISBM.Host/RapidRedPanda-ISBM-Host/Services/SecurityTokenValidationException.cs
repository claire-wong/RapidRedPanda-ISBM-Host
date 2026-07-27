namespace RapidRedPanda.ISBM.Host.Services;

public sealed class SecurityTokenValidationException : Exception
{
    public SecurityTokenValidationException(string message)
        : base(message)
    {
    }
}
