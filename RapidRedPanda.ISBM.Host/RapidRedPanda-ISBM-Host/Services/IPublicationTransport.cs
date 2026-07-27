using Azure.Messaging.ServiceBus;

namespace RapidRedPanda.ISBM.Host.Services;

public interface IPublicationTransport
{
    Task SendPublicationAsync(
        string topicName,
        ServiceBusMessage message,
        CancellationToken cancellationToken);
}
