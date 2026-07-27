using Azure.Messaging.ServiceBus;

namespace RapidRedPanda.ISBM.Host.Services;

public sealed class AzureServiceBusPublicationTransport : IPublicationTransport
{
    private readonly ServiceBusClient _serviceBusClient;

    public AzureServiceBusPublicationTransport(ServiceBusClient serviceBusClient)
    {
        _serviceBusClient = serviceBusClient;
    }

    public async Task SendPublicationAsync(
        string topicName,
        ServiceBusMessage message,
        CancellationToken cancellationToken)
    {
        await using ServiceBusSender sender = _serviceBusClient.CreateSender(topicName);
        await sender.SendMessageAsync(message, cancellationToken);
    }
}
