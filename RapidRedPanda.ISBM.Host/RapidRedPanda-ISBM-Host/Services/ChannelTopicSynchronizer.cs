using Microsoft.Extensions.Options;
using RapidRedPanda.ISBM.Host.Data;

namespace RapidRedPanda.ISBM.Host.Services;

public sealed class ChannelTopicSynchronizer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IChannelTopicTransport _topicTransport;
    private readonly ChannelTopicSynchronizationOptions _options;
    private readonly ILogger<ChannelTopicSynchronizer> _logger;

    public ChannelTopicSynchronizer(
        IServiceScopeFactory scopeFactory,
        IChannelTopicTransport topicTransport,
        IOptions<ChannelTopicSynchronizationOptions> options,
        ILogger<ChannelTopicSynchronizer> logger)
    {
        _scopeFactory = scopeFactory;
        _topicTransport = topicTransport;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Channel topic synchronization is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SynchronizeOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Channel topic synchronization failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task SynchronizeOnceAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IChannelRepository channelRepository = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
        IReadOnlyList<ChannelTopicBindingData> channels =
            await channelRepository.GetActiveChannelTopicBindingsAsync(cancellationToken);

        foreach (ChannelTopicBindingData channel in channels)
        {
            bool topicExists = await _topicTransport.TopicExistsAsync(channel.AzureTopicName, cancellationToken);
            if (topicExists)
            {
                continue;
            }

            _logger.LogWarning(
                "Azure Service Bus topic is missing for active channel; recreating it. ChannelId: {ChannelId}. ChannelUri: {ChannelUri}. AzureTopicName: {AzureTopicName}.",
                channel.ChannelId,
                channel.ChannelUri,
                channel.AzureTopicName);

            await _topicTransport.CreateTopicAsync(channel.AzureTopicName, cancellationToken);
        }
    }
}
