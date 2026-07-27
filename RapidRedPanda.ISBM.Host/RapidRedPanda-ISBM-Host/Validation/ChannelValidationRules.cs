namespace RapidRedPanda.ISBM.Host.Validation;

public static class ChannelValidationRules
{
    public const int ChannelUriMaxLength = 512;
    public const int DescriptionMaxLength = 2000;
    public const int UsernameMaxLength = 256;
    public const int PasswordHashMaxLength = 512;

    public static bool IsValidChannelUri(string? channelUri)
    {
        if (string.IsNullOrEmpty(channelUri))
        {
            return false;
        }

        if (channelUri.Length > ChannelUriMaxLength)
        {
            return false;
        }

        if (char.IsWhiteSpace(channelUri[0]) || char.IsWhiteSpace(channelUri[^1]))
        {
            return false;
        }

        foreach (char character in channelUri)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }
}
