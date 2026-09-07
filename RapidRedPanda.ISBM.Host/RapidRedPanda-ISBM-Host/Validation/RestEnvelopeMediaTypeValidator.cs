using System.Net.Http.Headers;

namespace RapidRedPanda.ISBM.Host.Validation;

internal static class RestEnvelopeMediaTypeValidator
{
    public static bool IsJsonEnvelopeMediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType) ||
            !MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? mediaTypeHeader) ||
            string.IsNullOrWhiteSpace(mediaTypeHeader.MediaType))
        {
            return false;
        }

        string mediaType = mediaTypeHeader.MediaType;
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
            mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase) &&
            mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }
}
