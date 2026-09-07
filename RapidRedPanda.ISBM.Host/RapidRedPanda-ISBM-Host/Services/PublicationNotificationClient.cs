using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using RapidRedPanda.ISBM.Host.Data;

namespace RapidRedPanda.ISBM.Host.Services;

public interface IPublicationNotificationClient
{
    Task<PublicationNotificationDeliveryResult> SendAsync(
        ClaimedPublicationNotification notification,
        CancellationToken cancellationToken);
}

public enum PublicationNotificationDeliveryOutcome
{
    Delivered,
    RetryableFailure,
    TerminalFailure,
    ShutdownCanceled
}

public sealed record PublicationNotificationDeliveryResult(
    PublicationNotificationDeliveryOutcome Outcome,
    int? StatusCode,
    string? Error)
{
    public static PublicationNotificationDeliveryResult Delivered(int statusCode)
    {
        return new PublicationNotificationDeliveryResult(PublicationNotificationDeliveryOutcome.Delivered, statusCode, null);
    }

    public static PublicationNotificationDeliveryResult RetryableFailure(int? statusCode, string error)
    {
        return new PublicationNotificationDeliveryResult(PublicationNotificationDeliveryOutcome.RetryableFailure, statusCode, error);
    }

    public static PublicationNotificationDeliveryResult TerminalFailure(int? statusCode, string error)
    {
        return new PublicationNotificationDeliveryResult(PublicationNotificationDeliveryOutcome.TerminalFailure, statusCode, error);
    }

    public static PublicationNotificationDeliveryResult ShutdownCanceled()
    {
        return new PublicationNotificationDeliveryResult(PublicationNotificationDeliveryOutcome.ShutdownCanceled, null, "Dispatcher shutdown canceled HTTP delivery.");
    }
}

public sealed class PublicationNotificationClient : IPublicationNotificationClient
{
    public const string HttpClientName = "IsbmPublicationNotification";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPublicationNotificationUriBuilder _uriBuilder;

    public PublicationNotificationClient(
        IHttpClientFactory httpClientFactory,
        IPublicationNotificationUriBuilder uriBuilder)
    {
        _httpClientFactory = httpClientFactory;
        _uriBuilder = uriBuilder;
    }

    public async Task<PublicationNotificationDeliveryResult> SendAsync(
        ClaimedPublicationNotification notification,
        CancellationToken cancellationToken)
    {
        Uri callbackUri;
        try
        {
            Guid recipientSessionId = notification.ServiceType switch
            {
                NotificationServiceTypes.Publication => notification.ConsumerSessionId
                    ?? throw new InvalidOperationException("Publication notification is missing a consumer session ID."),
                NotificationServiceTypes.Request => notification.ProviderRequestSessionId
                    ?? throw new InvalidOperationException("Request notification is missing a provider request session ID."),
                _ => throw new InvalidOperationException("Notification service type is not supported.")
            };

            callbackUri = _uriBuilder.Build(
                notification.ListenerUrl,
                recipientSessionId,
                notification.MessageId);
        }
        catch (Exception exception) when (exception is UriFormatException or InvalidOperationException)
        {
            return PublicationNotificationDeliveryResult.TerminalFailure(null, "Invalid callback URI.");
        }

        HttpClient httpClient = _httpClientFactory.CreateClient(HttpClientName);
        using HttpRequestMessage request = new(HttpMethod.Put, callbackUri)
        {
            Content = JsonContent.Create(new PublicationNotificationRequest(notification.Topics))
        };
        request.Headers.UserAgent.ParseAdd("RapidRedPanda.ISBM.Host");

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            int statusCode = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return PublicationNotificationDeliveryResult.Delivered(statusCode);
            }

            if (IsRetryableStatusCode(response.StatusCode))
            {
                return PublicationNotificationDeliveryResult.RetryableFailure(statusCode, $"HTTP {statusCode}.");
            }

            return PublicationNotificationDeliveryResult.TerminalFailure(statusCode, $"HTTP {statusCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PublicationNotificationDeliveryResult.ShutdownCanceled();
        }
        catch (TaskCanceledException)
        {
            return PublicationNotificationDeliveryResult.RetryableFailure(null, "HTTP request timed out.");
        }
        catch (HttpRequestException exception)
        {
            return PublicationNotificationDeliveryResult.RetryableFailure(null, $"HTTP request failed: {exception.GetType().Name}.");
        }
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
    {
        int code = (int)statusCode;
        return statusCode is HttpStatusCode.RequestTimeout or (HttpStatusCode)429 ||
            code is >= 500 and <= 599;
    }

    private sealed record PublicationNotificationRequest(
        [property: JsonPropertyName("topics")] IReadOnlyList<string> Topics);
}
