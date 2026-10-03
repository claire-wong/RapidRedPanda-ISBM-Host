using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Services;
using System.Net;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class PublicationNotificationClientTests
{
    [Fact]
    public async Task SendAsync_UsesPutJsonTopicsAndTreats204AsDelivered()
    {
        CapturingHandler handler = new(HttpStatusCode.NoContent);
        PublicationNotificationClient client = CreateClient(handler);
        ClaimedPublicationNotification notification = Notification();

        PublicationNotificationDeliveryResult result = await client.SendAsync(notification, CancellationToken.None);

        Assert.Equal(PublicationNotificationDeliveryOutcome.Delivered, result.Outcome);
        Assert.Equal(HttpMethod.Put, handler.Request?.Method);
        Assert.Equal($"/base/notifications/{notification.ConsumerSessionId:D}/{notification.MessageId:D}", handler.Request?.RequestUri?.AbsolutePath);
        Assert.Equal("application/json", handler.Request?.Content?.Headers.ContentType?.MediaType);
        Assert.Equal("""{"topics":["Topic-A","Topic-B"]}""", handler.RequestBody);
    }

    [Fact]
    public async Task SendAsync_RequestNotification_UsesProviderRequestSessionInSameContract()
    {
        CapturingHandler handler = new(HttpStatusCode.NoContent);
        PublicationNotificationClient client = CreateClient(handler);
        ClaimedPublicationNotification notification = Notification(
            NotificationServiceTypes.Request,
            consumerSessionId: null,
            providerRequestSessionId: Guid.NewGuid());

        PublicationNotificationDeliveryResult result = await client.SendAsync(notification, CancellationToken.None);

        Assert.Equal(PublicationNotificationDeliveryOutcome.Delivered, result.Outcome);
        Assert.Equal(HttpMethod.Put, handler.Request?.Method);
        Assert.Equal($"/base/notifications/{notification.ProviderRequestSessionId:D}/{notification.MessageId:D}", handler.Request?.RequestUri?.AbsolutePath);
        Assert.Equal("""{"topics":["Topic-A","Topic-B"]}""", handler.RequestBody);
        Assert.DoesNotContain("messageContent", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_ResponseNotification_UsesConsumerRequestSessionAndRequestMessageIdOnly()
    {
        CapturingHandler handler = new(HttpStatusCode.NoContent);
        PublicationNotificationClient client = CreateClient(handler);
        Guid requestMessageId = Guid.NewGuid();
        ClaimedPublicationNotification notification = Notification(
            NotificationServiceTypes.Response,
            consumerSessionId: null,
            providerRequestSessionId: null,
            requestMessageId: requestMessageId,
            consumerRequestSessionId: Guid.NewGuid());

        PublicationNotificationDeliveryResult result = await client.SendAsync(notification, CancellationToken.None);

        Assert.Equal(PublicationNotificationDeliveryOutcome.Delivered, result.Outcome);
        Assert.Equal(HttpMethod.Put, handler.Request?.Method);
        Assert.Equal($"/base/notifications/{notification.ConsumerRequestSessionId:D}/{notification.MessageId:D}", handler.Request?.RequestUri?.AbsolutePath);
        Assert.Equal($"{{\"requestMessageId\":\"{requestMessageId:D}\"}}", handler.RequestBody);
        Assert.DoesNotContain("topics", handler.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("messageContent", handler.RequestBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NotificationServiceTypes.Publication)]
    [InlineData(NotificationServiceTypes.Request)]
    public async Task SendAsync_TopicNotifications_NeverSerializeRequestMessageId(string serviceType)
    {
        CapturingHandler handler = new(HttpStatusCode.NoContent);
        PublicationNotificationClient client = CreateClient(handler);
        ClaimedPublicationNotification notification = Notification(
            serviceType,
            consumerSessionId: serviceType == NotificationServiceTypes.Publication ? Guid.NewGuid() : null,
            providerRequestSessionId: serviceType == NotificationServiceTypes.Request ? Guid.NewGuid() : null,
            requestMessageId: null);

        PublicationNotificationDeliveryResult result = await client.SendAsync(notification, CancellationToken.None);

        Assert.Equal(PublicationNotificationDeliveryOutcome.Delivered, result.Outcome);
        Assert.Contains("topics", handler.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("requestMessageId", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_NotifyListenerContractMatrix_IsServiceSpecific()
    {
        CapturingHandler publicationHandler = new(HttpStatusCode.NoContent);
        CapturingHandler requestHandler = new(HttpStatusCode.NoContent);
        ClaimedPublicationNotification publication = Notification(NotificationServiceTypes.Publication);
        ClaimedPublicationNotification request = Notification(
            NotificationServiceTypes.Request,
            consumerSessionId: null,
            providerRequestSessionId: Guid.NewGuid());

        await CreateClient(publicationHandler).SendAsync(publication, CancellationToken.None);
        await CreateClient(requestHandler).SendAsync(request, CancellationToken.None);

        Assert.Equal($"/base/notifications/{publication.ConsumerSessionId:D}/{publication.MessageId:D}", publicationHandler.Request?.RequestUri?.AbsolutePath);
        Assert.Equal("""{"topics":["Topic-A","Topic-B"]}""", publicationHandler.RequestBody);
        Assert.DoesNotContain("requestMessageId", publicationHandler.RequestBody, StringComparison.Ordinal);

        Assert.Equal($"/base/notifications/{request.ProviderRequestSessionId:D}/{request.MessageId:D}", requestHandler.Request?.RequestUri?.AbsolutePath);
        Assert.Equal("""{"topics":["Topic-A","Topic-B"]}""", requestHandler.RequestBody);
        Assert.DoesNotContain("requestMessageId", requestHandler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_MalformedRequestNotification_IsTerminalFailure()
    {
        PublicationNotificationClient client = CreateClient(new CapturingHandler(HttpStatusCode.NoContent));
        ClaimedPublicationNotification notification = Notification(
            NotificationServiceTypes.Request,
            consumerSessionId: null,
            providerRequestSessionId: null);

        PublicationNotificationDeliveryResult result = await client.SendAsync(notification, CancellationToken.None);

        Assert.Equal(PublicationNotificationDeliveryOutcome.TerminalFailure, result.Outcome);
    }

    [Fact]
    public async Task SendAsync_MalformedResponseNotification_IsTerminalFailure()
    {
        PublicationNotificationClient client = CreateClient(new CapturingHandler(HttpStatusCode.NoContent));
        ClaimedPublicationNotification notification = Notification(
            NotificationServiceTypes.Response,
            consumerSessionId: null,
            providerRequestSessionId: null,
            requestMessageId: null,
            consumerRequestSessionId: Guid.NewGuid());

        PublicationNotificationDeliveryResult result = await client.SendAsync(notification, CancellationToken.None);

        Assert.Equal(PublicationNotificationDeliveryOutcome.TerminalFailure, result.Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task SendAsync_Non204TerminalStatuses_AreTerminalFailures(HttpStatusCode statusCode)
    {
        PublicationNotificationClient client = CreateClient(new CapturingHandler(statusCode));

        PublicationNotificationDeliveryResult result = await client.SendAsync(Notification(), CancellationToken.None);

        Assert.Equal(PublicationNotificationDeliveryOutcome.TerminalFailure, result.Outcome);
        Assert.Equal((int)statusCode, result.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData((HttpStatusCode)429)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task SendAsync_TransientStatuses_AreRetryable(HttpStatusCode statusCode)
    {
        PublicationNotificationClient client = CreateClient(new CapturingHandler(statusCode));

        PublicationNotificationDeliveryResult result = await client.SendAsync(Notification(), CancellationToken.None);

        Assert.Equal(PublicationNotificationDeliveryOutcome.RetryableFailure, result.Outcome);
        Assert.Equal((int)statusCode, result.StatusCode);
    }

    [Fact]
    public async Task SendAsync_HttpRequestException_IsRetryable()
    {
        PublicationNotificationClient client = CreateClient(new ThrowingHandler(new HttpRequestException("connection failed")));

        PublicationNotificationDeliveryResult result = await client.SendAsync(Notification(), CancellationToken.None);

        Assert.Equal(PublicationNotificationDeliveryOutcome.RetryableFailure, result.Outcome);
        Assert.Null(result.StatusCode);
    }

    [Fact]
    public async Task SendAsync_HostShutdownCancellation_IsNotTerminal()
    {
        PublicationNotificationClient client = CreateClient(new CancellationHandler());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        PublicationNotificationDeliveryResult result = await client.SendAsync(Notification(), cancellation.Token);

        Assert.Equal(PublicationNotificationDeliveryOutcome.ShutdownCanceled, result.Outcome);
    }

    private static PublicationNotificationClient CreateClient(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        return new PublicationNotificationClient(
            new SingleHttpClientFactory(httpClient),
            new PublicationNotificationUriBuilder());
    }

    private static ClaimedPublicationNotification Notification(
        string serviceType = NotificationServiceTypes.Publication,
        Guid? consumerSessionId = null,
        Guid? providerRequestSessionId = null,
        Guid? requestMessageId = null,
        Guid? consumerRequestSessionId = null,
        IReadOnlyList<string>? topics = null)
    {
        return new ClaimedPublicationNotification(
            Guid.NewGuid(),
            serviceType,
            Guid.NewGuid(),
            requestMessageId,
            consumerSessionId ?? (serviceType == NotificationServiceTypes.Publication ? Guid.NewGuid() : null),
            providerRequestSessionId,
            consumerRequestSessionId,
            "https://listener.example.com/base",
            topics ?? ["Topic-A", "Topic-B"],
            1,
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(1),
            IsEligible: true,
            SuppressionReason: null,
            IsDeliveryWindowExpired: false);
    }

    private sealed class SingleHttpClientFactory(HttpClient httpClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(PublicationNotificationClient.HttpClientName, name);
            return httpClient;
        }
    }

    private sealed class CapturingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode);
        }
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw exception;
        }
    }
}
