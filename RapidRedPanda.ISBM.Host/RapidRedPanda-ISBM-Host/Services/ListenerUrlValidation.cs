namespace RapidRedPanda.ISBM.Host.Services;

public static class ListenerUrlValidation
{
    public const int MaxLength = 2048;

    public static bool TryNormalize(string? listenerUrl, out string? normalizedListenerUrl)
    {
        normalizedListenerUrl = null;

        if (listenerUrl is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(listenerUrl) ||
            listenerUrl.Length != listenerUrl.Trim().Length)
        {
            return false;
        }

        if (!Uri.TryCreate(listenerUrl, UriKind.Absolute, out Uri? uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        string absoluteUri = uri.AbsoluteUri;
        if (absoluteUri.Length > MaxLength)
        {
            return false;
        }

        normalizedListenerUrl = absoluteUri;
        return true;
    }
}
