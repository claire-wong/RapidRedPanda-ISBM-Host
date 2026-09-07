namespace RapidRedPanda.ISBM.Host.Services;

public interface IPublicationNotificationUriBuilder
{
    Uri Build(string listenerUrl, Guid sessionId, Guid messageId);
}

public sealed class PublicationNotificationUriBuilder : IPublicationNotificationUriBuilder
{
    public Uri Build(string listenerUrl, Guid sessionId, Guid messageId)
    {
        if (!Uri.TryCreate(listenerUrl, UriKind.Absolute, out Uri? listenerUri) ||
            !string.IsNullOrEmpty(listenerUri.Fragment))
        {
            throw new InvalidOperationException("Listener URL is not a valid absolute URL.");
        }

        UriBuilder builder = new(listenerUri)
        {
            Fragment = string.Empty
        };

        string basePath = builder.Path.TrimEnd('/');
        if (string.IsNullOrEmpty(basePath))
        {
            basePath = string.Empty;
        }

        builder.Path = string.Join(
            '/',
            new[]
            {
                basePath,
                "notifications",
                Uri.EscapeDataString(sessionId.ToString("D")),
                Uri.EscapeDataString(messageId.ToString("D"))
            }.Where(segment => !string.IsNullOrEmpty(segment)));

        return builder.Uri;
    }
}
