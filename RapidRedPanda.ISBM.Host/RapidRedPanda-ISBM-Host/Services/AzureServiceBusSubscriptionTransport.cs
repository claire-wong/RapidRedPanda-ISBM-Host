using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using System.Collections.Concurrent;

namespace RapidRedPanda.ISBM.Host.Services;

public sealed class AzureServiceBusSubscriptionTransport : ISubscriptionTransport
{
    private const int MaxMalformedMessagesPerRead = 5;

    private readonly ServiceBusClient _serviceBusClient;
    private readonly ServiceBusAdministrationClient _administrationClient;
    private readonly ILogger<AzureServiceBusSubscriptionTransport> _logger;
    private readonly ConcurrentDictionary<Guid, PendingPublication> _pendingPublications = new();

    public AzureServiceBusSubscriptionTransport(
        ServiceBusClient serviceBusClient,
        ServiceBusAdministrationClient administrationClient,
        ILogger<AzureServiceBusSubscriptionTransport> logger)
    {
        _serviceBusClient = serviceBusClient;
        _administrationClient = administrationClient;
        _logger = logger;
    }

    public async Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken)
    {
        try
        {
            return await _administrationClient.TopicExistsAsync(topicName, cancellationToken);
        }
        catch (RequestFailedException exception)
        {
            _logger.LogWarning(
                exception,
                "Azure Service Bus topic existence check failed for topic {AzureTopicName}. Status: {AzureStatus}. ErrorCode: {AzureErrorCode}.",
                topicName,
                exception.Status,
                exception.ErrorCode);
            throw;
        }
    }

    public async Task CreateSubscriptionAsync(
        string topicName,
        string subscriptionName,
        IReadOnlyList<string> consumerTopics,
        CancellationToken cancellationToken)
    {
        string topicSelectionFilter = TopicSelectionRules.BuildAzureSqlFilter(consumerTopics);
        CreateSubscriptionOptions options = new(topicName, subscriptionName)
        {
            MaxDeliveryCount = 10,
            DeadLetteringOnMessageExpiration = true
        };

        bool subscriptionCreated = false;
        try
        {
            await _administrationClient.CreateSubscriptionAsync(options, cancellationToken);
            subscriptionCreated = true;

            CreateRuleOptions ruleOptions = new(
                "ISBMTopicSelection",
                new SqlRuleFilter(topicSelectionFilter));

            await _administrationClient.CreateRuleAsync(topicName, subscriptionName, ruleOptions, cancellationToken);
            await DeleteDefaultRuleIfPresentAsync(topicName, subscriptionName, cancellationToken);
        }
        catch
        {
            if (subscriptionCreated)
            {
                try
                {
                    await _administrationClient.DeleteSubscriptionAsync(topicName, subscriptionName, cancellationToken);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogError(
                        cleanupException,
                        "Azure Service Bus subscription cleanup failed after filtered-rule creation failure. AzureTopicName: {AzureTopicName}. AzureSubscriptionName: {AzureSubscriptionName}.",
                        topicName,
                        subscriptionName);
                }
            }

            throw;
        }
    }

    public async Task DeleteSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
    {
        await DisposePendingPublicationsForSubscriptionAsync(topicName, subscriptionName);

        try
        {
            await _administrationClient.DeleteSubscriptionAsync(topicName, subscriptionName, cancellationToken);
        }
        catch (RequestFailedException exception) when (IsEntityNotFound(exception))
        {
            _logger.LogInformation(
                "Azure Service Bus subscription {AzureSubscriptionName} under topic {AzureTopicName} was already missing during close.",
                subscriptionName,
                topicName);
        }
    }

    public async Task<IReadOnlyList<string>> GetSubscriptionNamesAsync(string topicName, CancellationToken cancellationToken)
    {
        List<string> subscriptionNames = [];

        await foreach (SubscriptionProperties subscription in _administrationClient.GetSubscriptionsAsync(topicName, cancellationToken))
        {
            subscriptionNames.Add(subscription.SubscriptionName);
        }

        return subscriptionNames;
    }

    public async Task<TransportPublication?> ReadPublicationAsync(
        Guid sessionId,
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken)
    {
        if (_pendingPublications.TryGetValue(sessionId, out PendingPublication? pendingPublication))
        {
            return pendingPublication.Publication;
        }

        ServiceBusReceiver receiver = _serviceBusClient.CreateReceiver(
            topicName,
            subscriptionName,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });

        for (int attempt = 0; attempt < MaxMalformedMessagesPerRead; attempt++)
        {
            ServiceBusReceivedMessage? message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(1), cancellationToken);
            if (message is null)
            {
                await receiver.DisposeAsync();
                return null;
            }

            if (TryCreateTransportPublication(message, out TransportPublication? publication))
            {
                PendingPublication pending = new(topicName, subscriptionName, receiver, message, publication!);
                if (_pendingPublications.TryAdd(sessionId, pending))
                {
                    return publication;
                }

                await receiver.AbandonMessageAsync(message, cancellationToken: cancellationToken);
                await receiver.DisposeAsync();
                return _pendingPublications[sessionId].Publication;
            }

            _logger.LogWarning(
                "Dead-lettering malformed Azure Service Bus publication message for consumer read. SessionId: {SessionId}. AzureTopicName: {AzureTopicName}. AzureSubscriptionName: {AzureSubscriptionName}. MessageId: {AzureMessageId}.",
                sessionId,
                topicName,
                subscriptionName,
                message.MessageId);

            await receiver.DeadLetterMessageAsync(
                message,
                "MalformedISBMPublication",
                "Required ISBM publication metadata was missing or invalid.",
                cancellationToken);
        }

        await receiver.DisposeAsync();
        _logger.LogError(
            "Too many malformed Azure Service Bus publication messages encountered during one read operation. SessionId: {SessionId}. AzureTopicName: {AzureTopicName}. AzureSubscriptionName: {AzureSubscriptionName}.",
            sessionId,
            topicName,
            subscriptionName);
        throw new InvalidOperationException("Too many malformed publication messages.");
    }

    public Task<TransportPublication?> GetOutstandingPublicationAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            _pendingPublications.TryGetValue(sessionId, out PendingPublication? pendingPublication)
                ? pendingPublication.Publication
                : null);
    }

    public async Task RemovePublicationAsync(
        Guid sessionId,
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken)
    {
        if (_pendingPublications.TryRemove(sessionId, out PendingPublication? pendingPublication))
        {
            try
            {
                await pendingPublication.Receiver.CompleteMessageAsync(pendingPublication.Message, cancellationToken);
            }
            finally
            {
                await pendingPublication.Receiver.DisposeAsync();
            }

            return;
        }

        ServiceBusReceiver receiver = _serviceBusClient.CreateReceiver(
            topicName,
            subscriptionName,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });

        try
        {
            ServiceBusReceivedMessage? message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(1), cancellationToken);
            if (message is not null)
            {
                await receiver.CompleteMessageAsync(message, cancellationToken);
            }
        }
        finally
        {
            await receiver.DisposeAsync();
        }
    }

    public static bool IsEntityNotFound(RequestFailedException exception)
    {
        return exception.Status == 404 ||
            string.Equals(exception.ErrorCode, "MessagingEntityNotFound", StringComparison.OrdinalIgnoreCase);
    }

    private async Task DeleteDefaultRuleIfPresentAsync(
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken)
    {
        try
        {
            await _administrationClient.DeleteRuleAsync(topicName, subscriptionName, RuleProperties.DefaultRuleName, cancellationToken);
        }
        catch (RequestFailedException exception) when (IsEntityNotFound(exception))
        {
            _logger.LogInformation(
                "Azure Service Bus default subscription rule was already absent. AzureTopicName: {AzureTopicName}. AzureSubscriptionName: {AzureSubscriptionName}.",
                topicName,
                subscriptionName);
        }
    }

    private async Task DisposePendingPublicationsForSubscriptionAsync(string topicName, string subscriptionName)
    {
        foreach (KeyValuePair<Guid, PendingPublication> item in _pendingPublications)
        {
            PendingPublication pending = item.Value;
            if (string.Equals(pending.TopicName, topicName, StringComparison.Ordinal) &&
                string.Equals(pending.SubscriptionName, subscriptionName, StringComparison.Ordinal) &&
                _pendingPublications.TryRemove(item.Key, out PendingPublication? removed))
            {
                await removed.Receiver.DisposeAsync();
            }
        }
    }

    private static bool TryCreateTransportPublication(
        ServiceBusReceivedMessage message,
        out TransportPublication? publication)
    {
        publication = null;

        if (!TryReadPublicationId(message, out Guid publicationId) ||
            !TryReadTopics(message, out IReadOnlyList<string>? topics))
        {
            return false;
        }

        publication = new TransportPublication(publicationId, topics!, message.ContentType, message.Body);
        return true;
    }

    private static bool TryReadPublicationId(ServiceBusReceivedMessage message, out Guid publicationId)
    {
        publicationId = Guid.Empty;

        if (!message.ApplicationProperties.TryGetValue(PublicationRoutingProperties.PublicationId, out object? rawPublicationId) ||
            rawPublicationId is not string publicationIdValue)
        {
            return false;
        }

        return Guid.TryParse(publicationIdValue, out publicationId);
    }

    private static bool TryReadTopics(ServiceBusReceivedMessage message, out IReadOnlyList<string>? topics)
    {
        topics = null;

        if (!message.ApplicationProperties.TryGetValue(PublicationRoutingProperties.TopicCount, out object? rawTopicCount) ||
            !TryReadInt32(rawTopicCount, out int topicCount) ||
            topicCount < 1 ||
            topicCount > PublicationRoutingProperties.MaxPublicationTopics)
        {
            return false;
        }

        List<string> topicValues = [];
        for (int index = 0; index < topicCount; index++)
        {
            if (!message.ApplicationProperties.TryGetValue(PublicationRoutingProperties.TopicPropertyName(index), out object? rawTopic) ||
                rawTopic is not string topic ||
                string.IsNullOrEmpty(topic))
            {
                return false;
            }

            topicValues.Add(topic);
        }

        topics = topicValues;
        return true;
    }

    private static bool TryReadInt32(object value, out int result)
    {
        switch (value)
        {
            case int intValue:
                result = intValue;
                return true;
            case long longValue when longValue >= int.MinValue && longValue <= int.MaxValue:
                result = (int)longValue;
                return true;
            case string stringValue:
                return int.TryParse(stringValue, out result);
            default:
                result = 0;
                return false;
        }
    }

    private sealed record PendingPublication(
        string TopicName,
        string SubscriptionName,
        ServiceBusReceiver Receiver,
        ServiceBusReceivedMessage Message,
        TransportPublication Publication);
}
