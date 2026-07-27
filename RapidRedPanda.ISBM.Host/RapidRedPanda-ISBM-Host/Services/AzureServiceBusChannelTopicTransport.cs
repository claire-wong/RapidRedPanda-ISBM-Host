using Azure;
using Azure.Messaging.ServiceBus.Administration;

namespace RapidRedPanda.ISBM.Host.Services;

public sealed class AzureServiceBusChannelTopicTransport : IChannelTopicTransport
{
    private readonly ServiceBusAdministrationClient _administrationClient;
    private readonly ILogger<AzureServiceBusChannelTopicTransport> _logger;

    public AzureServiceBusChannelTopicTransport(
        ServiceBusAdministrationClient administrationClient,
        ILogger<AzureServiceBusChannelTopicTransport> logger)
    {
        _administrationClient = administrationClient;
        _logger = logger;
    }

    public async Task CreateTopicAsync(string topicName, CancellationToken cancellationToken)
    {
        try
        {
            await _administrationClient.CreateTopicAsync(topicName, cancellationToken);
        }
        catch (RequestFailedException exception) when (IsEntityAlreadyExists(exception))
        {
            _logger.LogInformation(
                "Azure Service Bus topic {AzureTopicName} already exists.",
                topicName);
        }
    }

    public async Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken)
    {
        return await _administrationClient.TopicExistsAsync(topicName, cancellationToken);
    }

    public async Task DeleteTopicAsync(string topicName, CancellationToken cancellationToken)
    {
        try
        {
            await _administrationClient.DeleteTopicAsync(topicName, cancellationToken);
        }
        catch (RequestFailedException exception) when (AzureServiceBusSubscriptionTransport.IsEntityNotFound(exception))
        {
            _logger.LogInformation(
                "Azure Service Bus topic {AzureTopicName} was already missing during delete.",
                topicName);
        }
    }

    public static bool IsEntityAlreadyExists(RequestFailedException exception)
    {
        return exception.Status == 409 ||
            string.Equals(exception.ErrorCode, "MessagingEntityAlreadyExists", StringComparison.OrdinalIgnoreCase);
    }
}
