using Azure;
using RapidRedPanda.ISBM.Host.Data;

namespace RapidRedPanda.ISBM.Host.Services;

public interface IChannelLifecycleService
{
    Task<CreateChannelResult> CreateChannelAsync(
        string channelUri,
        string channelType,
        string? description,
        IReadOnlyList<ChannelCreateToken> securityTokens,
        CancellationToken cancellationToken);

    Task<DeleteChannelResult> DeleteChannelAsync(
        string channelUri,
        CancellationToken cancellationToken);
}

public sealed class ChannelLifecycleService : IChannelLifecycleService
{
    private readonly IChannelRepository _channelRepository;
    private readonly IChannelTopicTransport _topicTransport;
    private readonly ILogger<ChannelLifecycleService> _logger;

    public ChannelLifecycleService(
        IChannelRepository channelRepository,
        IChannelTopicTransport topicTransport,
        ILogger<ChannelLifecycleService> logger)
    {
        _channelRepository = channelRepository;
        _topicTransport = topicTransport;
        _logger = logger;
    }

    public async Task<CreateChannelResult> CreateChannelAsync(
        string channelUri,
        string channelType,
        string? description,
        IReadOnlyList<ChannelCreateToken> securityTokens,
        CancellationToken cancellationToken)
    {
        Guid channelId = Guid.NewGuid();
        string azureTopicName = ChannelTransportNames.TopicNameForChannel(channelId);
        bool topicCreatedOrAlreadyExisted = false;

        try
        {
            await _topicTransport.CreateTopicAsync(azureTopicName, cancellationToken);
            topicCreatedOrAlreadyExisted = true;
        }
        catch (RequestFailedException exception)
        {
            _logger.LogError(
                exception,
                "Azure Service Bus topic creation failed while creating channel. ChannelId: {ChannelId}. ChannelUri: {ChannelUri}. AzureTopicName: {AzureTopicName}. Status: {AzureStatus}. ErrorCode: {AzureErrorCode}.",
                channelId,
                channelUri,
                azureTopicName,
                exception.Status,
                exception.ErrorCode);
            return CreateChannelResult.Failure(CreateChannelFailureKind.DatabaseFailure, "Azure topic creation failed while creating channel.");
        }

        CreateChannelResult createResult = await _channelRepository.CreateChannelAsync(
            new ChannelCreateRequestData(
                channelId,
                channelUri,
                channelType,
                description,
                securityTokens,
                azureTopicName),
            cancellationToken);

        if (createResult.Succeeded)
        {
            return createResult;
        }

        if (topicCreatedOrAlreadyExisted)
        {
            try
            {
                _logger.LogWarning(
                    "Attempting Azure Service Bus topic cleanup after SQL channel creation failure. ChannelId: {ChannelId}. ChannelUri: {ChannelUri}. AzureTopicName: {AzureTopicName}.",
                    channelId,
                    channelUri,
                    azureTopicName);
                await _topicTransport.DeleteTopicAsync(azureTopicName, cancellationToken);
                _logger.LogInformation(
                    "Azure Service Bus topic cleanup succeeded after SQL channel creation failure. ChannelId: {ChannelId}. AzureTopicName: {AzureTopicName}.",
                    channelId,
                    azureTopicName);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Azure Service Bus topic cleanup failed after SQL channel creation failure. ChannelId: {ChannelId}. ChannelUri: {ChannelUri}. AzureTopicName: {AzureTopicName}.",
                    channelId,
                    channelUri,
                    azureTopicName);
            }
        }

        return createResult;
    }

    public async Task<DeleteChannelResult> DeleteChannelAsync(string channelUri, CancellationToken cancellationToken)
    {
        GetChannelTopicBindingResult bindingResult = await _channelRepository.GetChannelTopicBindingAsync(channelUri, cancellationToken);
        if (!bindingResult.Succeeded || bindingResult.Channel is null)
        {
            return bindingResult.FailureKind switch
            {
                GetChannelTopicBindingFailureKind.NotFound => DeleteChannelResult.Failure(DeleteChannelFailureKind.NotFound, "Channel was not found."),
                _ => DeleteChannelResult.Failure(DeleteChannelFailureKind.DatabaseFailure, "Database failure while retrieving channel topic binding.")
            };
        }

        ChannelTopicBindingData channel = bindingResult.Channel;

        try
        {
            await _topicTransport.DeleteTopicAsync(channel.AzureTopicName, cancellationToken);
        }
        catch (RequestFailedException exception)
        {
            _logger.LogError(
                exception,
                "Azure Service Bus topic deletion failed while deleting channel. ChannelId: {ChannelId}. ChannelUri: {ChannelUri}. AzureTopicName: {AzureTopicName}. Status: {AzureStatus}. ErrorCode: {AzureErrorCode}.",
                channel.ChannelId,
                channel.ChannelUri,
                channel.AzureTopicName,
                exception.Status,
                exception.ErrorCode);
            return DeleteChannelResult.Failure(DeleteChannelFailureKind.DatabaseFailure, "Azure topic deletion failed while deleting channel.");
        }

        return await _channelRepository.DeleteChannelAsync(channelUri, cancellationToken);
    }
}
