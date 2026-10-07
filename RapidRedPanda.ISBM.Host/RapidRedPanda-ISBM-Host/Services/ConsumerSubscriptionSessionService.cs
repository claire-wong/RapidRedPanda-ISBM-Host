using Azure;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Validation;

namespace RapidRedPanda.ISBM.Host.Services;

public interface IConsumerSubscriptionSessionService
{
    Task<OpenSubscriptionSessionResult> OpenSubscriptionSessionAsync(
        string channelId,
        IReadOnlyList<string> topics,
        string? listenerUrl,
        string? filterExpressionsJson,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken);

    Task<CloseConsumerSubscriptionSessionResult> CloseSubscriptionSessionAsync(
        string sessionId,
        CancellationToken cancellationToken);

    Task<ReadPublicationResult> ReadPublicationAsync(
        string sessionId,
        CancellationToken cancellationToken);

    Task<RemovePublicationResult> RemovePublicationAsync(
        string sessionId,
        CancellationToken cancellationToken);
}

public enum OpenSubscriptionSessionFailureKind
{
    ParameterFault,
    ChannelFault,
    InvalidChannelType,
    SecurityTokenFault,
    OperationFault
}

public sealed record OpenSubscriptionSessionResult(
    bool Succeeded,
    Guid? SessionId,
    OpenSubscriptionSessionFailureKind? FailureKind)
{
    public static OpenSubscriptionSessionResult Success(Guid sessionId)
    {
        return new OpenSubscriptionSessionResult(true, sessionId, null);
    }

    public static OpenSubscriptionSessionResult Failure(OpenSubscriptionSessionFailureKind failureKind)
    {
        return new OpenSubscriptionSessionResult(false, null, failureKind);
    }
}

public enum CloseSubscriptionSessionFailureKind
{
    ParameterFault,
    SessionFault,
    OperationFault
}

public sealed record CloseConsumerSubscriptionSessionResult(
    bool Succeeded,
    CloseSubscriptionSessionFailureKind? FailureKind)
{
    public static CloseConsumerSubscriptionSessionResult Success()
    {
        return new CloseConsumerSubscriptionSessionResult(true, null);
    }

    public static CloseConsumerSubscriptionSessionResult Failure(CloseSubscriptionSessionFailureKind failureKind)
    {
        return new CloseConsumerSubscriptionSessionResult(false, failureKind);
    }
}

public enum ReadPublicationFailureKind
{
    ParameterFault,
    SessionFault,
    OperationFault
}

public sealed record ReadPublicationData(
    Guid MessageId,
    IReadOnlyList<string> Topics,
    InternalMessageContent MessageContent);

public sealed record ReadPublicationResult(
    bool Succeeded,
    bool HasPublication,
    ReadPublicationData? Publication,
    ReadPublicationFailureKind? FailureKind)
{
    public static ReadPublicationResult Success(ReadPublicationData publication)
    {
        return new ReadPublicationResult(true, true, publication, null);
    }

    public static ReadPublicationResult NoPublication()
    {
        return new ReadPublicationResult(true, false, null, null);
    }

    public static ReadPublicationResult Failure(ReadPublicationFailureKind failureKind)
    {
        return new ReadPublicationResult(false, false, null, failureKind);
    }
}

public enum RemovePublicationFailureKind
{
    ParameterFault,
    SessionFault,
    OperationFault
}

public sealed record RemovePublicationResult(
    bool Succeeded,
    RemovePublicationFailureKind? FailureKind)
{
    public static RemovePublicationResult Success()
    {
        return new RemovePublicationResult(true, null);
    }

    public static RemovePublicationResult Failure(RemovePublicationFailureKind failureKind)
    {
        return new RemovePublicationResult(false, failureKind);
    }
}

public sealed class ConsumerSubscriptionSessionService : IConsumerSubscriptionSessionService
{
    private readonly IChannelRepository _channelRepository;
    private readonly ISubscriptionSessionRepository _sessionRepository;
    private readonly IProviderPublicationRepository _publicationRepository;
    private readonly ISubscriptionTransport _subscriptionTransport;
    private readonly IChannelTopicTransport _topicTransport;
    private readonly IChannelSubscriptionReconciliationService _reconciliationService;
    private readonly ILogger<ConsumerSubscriptionSessionService> _logger;

    public ConsumerSubscriptionSessionService(
        IChannelRepository channelRepository,
        ISubscriptionSessionRepository sessionRepository,
        IProviderPublicationRepository publicationRepository,
        ISubscriptionTransport subscriptionTransport,
        IChannelTopicTransport topicTransport,
        IChannelSubscriptionReconciliationService reconciliationService,
        ILogger<ConsumerSubscriptionSessionService> logger)
    {
        _channelRepository = channelRepository;
        _sessionRepository = sessionRepository;
        _publicationRepository = publicationRepository;
        _subscriptionTransport = subscriptionTransport;
        _topicTransport = topicTransport;
        _reconciliationService = reconciliationService;
        _logger = logger;
    }

    public ConsumerSubscriptionSessionService(
        IChannelRepository channelRepository,
        ISubscriptionSessionRepository sessionRepository,
        IProviderPublicationRepository publicationRepository,
        ISubscriptionTransport subscriptionTransport,
        IChannelTopicTransport topicTransport,
        ILogger<ConsumerSubscriptionSessionService> logger)
        : this(
            channelRepository,
            sessionRepository,
            publicationRepository,
            subscriptionTransport,
            topicTransport,
            new NoOpChannelSubscriptionReconciliationService(),
            logger)
    {
    }

    public ConsumerSubscriptionSessionService(
        IChannelRepository channelRepository,
        ISubscriptionSessionRepository sessionRepository,
        ISubscriptionTransport subscriptionTransport,
        IChannelTopicTransport topicTransport,
        ILogger<ConsumerSubscriptionSessionService> logger)
        : this(
            channelRepository,
            sessionRepository,
            new NullProviderPublicationRepository(),
            subscriptionTransport,
            topicTransport,
            new NoOpChannelSubscriptionReconciliationService(),
            logger)
    {
    }

    public async Task<OpenSubscriptionSessionResult> OpenSubscriptionSessionAsync(
        string channelId,
        IReadOnlyList<string> topics,
        string? listenerUrl,
        string? filterExpressionsJson,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        if (!TryDecodeAndValidateChannelUri(channelId, out string decodedChannelUri))
        {
            return OpenSubscriptionSessionResult.Failure(OpenSubscriptionSessionFailureKind.ParameterFault);
        }

        if (!ListenerUrlValidation.TryNormalize(listenerUrl, out string? normalizedListenerUrl))
        {
            return OpenSubscriptionSessionResult.Failure(OpenSubscriptionSessionFailureKind.ParameterFault);
        }

        if (!TopicSelectionRules.TryValidateAndDeduplicate(topics, out IReadOnlyList<string>? requestedTopics))
        {
            return OpenSubscriptionSessionResult.Failure(OpenSubscriptionSessionFailureKind.ParameterFault);
        }

        try
        {
            _ = TopicSelectionRules.BuildAzureSqlFilter(requestedTopics!);
        }
        catch (ArgumentException)
        {
            return OpenSubscriptionSessionResult.Failure(OpenSubscriptionSessionFailureKind.ParameterFault);
        }

        GetChannelForSubscriptionResult channelResult = await _channelRepository.GetChannelForConsumerPublicationSubscriptionAsync(
            decodedChannelUri,
            credentials,
            cancellationToken);

        if (!channelResult.Succeeded || channelResult.Channel is null)
        {
            return channelResult.FailureKind switch
            {
                GetChannelForSubscriptionFailureKind.NotFound => OpenSubscriptionSessionResult.Failure(OpenSubscriptionSessionFailureKind.ChannelFault),
                GetChannelForSubscriptionFailureKind.SecurityTokenInvalid => OpenSubscriptionSessionResult.Failure(OpenSubscriptionSessionFailureKind.SecurityTokenFault),
                GetChannelForSubscriptionFailureKind.InvalidChannelType => OpenSubscriptionSessionResult.Failure(OpenSubscriptionSessionFailureKind.InvalidChannelType),
                _ => OpenSubscriptionSessionResult.Failure(OpenSubscriptionSessionFailureKind.OperationFault)
            };
        }

        ChannelSubscriptionData channel = channelResult.Channel;

        bool topicExists;
        try
        {
            topicExists = await _topicTransport.TopicExistsAsync(channel.AzureTopicName, cancellationToken);
        }
        catch (RequestFailedException exception)
        {
            _logger.LogError(
                exception,
                "Azure Service Bus topic check failed while opening subscription session. ChannelId: {ChannelId}. AzureTopicName: {AzureTopicName}. Status: {AzureStatus}. ErrorCode: {AzureErrorCode}.",
                channel.ChannelId,
                channel.AzureTopicName,
                exception.Status,
                exception.ErrorCode);
            return OpenSubscriptionSessionResult.Failure(OpenSubscriptionSessionFailureKind.OperationFault);
        }

        if (!topicExists)
        {
            _logger.LogWarning(
                "Azure Service Bus topic {AzureTopicName} for channel {ChannelId} was missing. Creating a replacement topic; previous broker messages and subscriptions cannot be recovered.",
                channel.AzureTopicName,
                channel.ChannelId);

            try
            {
                await _topicTransport.CreateTopicAsync(channel.AzureTopicName, cancellationToken);
            }
            catch (RequestFailedException exception)
            {
                _logger.LogError(
                    exception,
                    "Azure Service Bus topic repair failed while opening subscription session. ChannelId: {ChannelId}. AzureTopicName: {AzureTopicName}. Status: {AzureStatus}. ErrorCode: {AzureErrorCode}.",
                    channel.ChannelId,
                    channel.AzureTopicName,
                    exception.Status,
                    exception.ErrorCode);
                return OpenSubscriptionSessionResult.Failure(OpenSubscriptionSessionFailureKind.OperationFault);
            }
        }

        await ReconcileChannelSubscriptionsAsync(channel, cancellationToken);

        Guid sessionId = Guid.NewGuid();
        string azureSubscriptionName = ChannelTransportNames.SubscriptionNameForSession(sessionId);
        bool azureSubscriptionCreated = false;

        try
        {
            await _subscriptionTransport.CreateSubscriptionAsync(
                channel.AzureTopicName,
                azureSubscriptionName,
                requestedTopics!,
                cancellationToken);
            azureSubscriptionCreated = true;
        }
        catch (RequestFailedException exception)
        {
            _logger.LogError(
                exception,
                "Azure Service Bus subscription creation failed while opening subscription session. SessionId: {SessionId}. ChannelId: {ChannelId}. AzureTopicName: {AzureTopicName}. AzureSubscriptionName: {AzureSubscriptionName}. Status: {AzureStatus}. ErrorCode: {AzureErrorCode}.",
                sessionId,
                channel.ChannelId,
                channel.AzureTopicName,
                azureSubscriptionName,
                exception.Status,
                exception.ErrorCode);
            return OpenSubscriptionSessionResult.Failure(OpenSubscriptionSessionFailureKind.OperationFault);
        }

        InsertSubscriptionSessionResult insertResult = await _sessionRepository.InsertConsumerSubscriptionSessionAsync(
            new ConsumerSubscriptionSessionCreateData(
                sessionId,
                channel.ChannelId,
                channel.AzureTopicName,
                azureSubscriptionName,
                requestedTopics!,
                channel.AuthenticatedTokenUsername,
                normalizedListenerUrl,
                // FilterExpressions are accepted and persisted for ISBM protocol compatibility.
                // Backend filtering is not applied in this phase; delivery remains topic-based.
                filterExpressionsJson),
            cancellationToken);

        if (insertResult.Succeeded)
        {
            return OpenSubscriptionSessionResult.Success(sessionId);
        }

        if (azureSubscriptionCreated)
        {
            try
            {
                await _subscriptionTransport.DeleteSubscriptionAsync(
                    channel.AzureTopicName,
                    azureSubscriptionName,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Compensating cleanup failed after SQL insert failure while opening subscription session. SessionId: {SessionId}. ChannelId: {ChannelId}. AzureTopicName: {AzureTopicName}. AzureSubscriptionName: {AzureSubscriptionName}.",
                    sessionId,
                    channel.ChannelId,
                    channel.AzureTopicName,
                    azureSubscriptionName);
            }
        }

        return OpenSubscriptionSessionResult.Failure(OpenSubscriptionSessionFailureKind.OperationFault);
    }

    private async Task ReconcileChannelSubscriptionsAsync(
        ChannelSubscriptionData channel,
        CancellationToken cancellationToken)
    {
        try
        {
            ChannelSubscriptionReconciliationResult reconciliationResult =
                await _reconciliationService.ReconcileChannelSubscriptionsAsync(channel, cancellationToken);

            if (reconciliationResult.InspectedSubscriptions > 0)
            {
                _logger.LogInformation(
                    "Azure Service Bus subscription reconciliation completed for channel. ChannelId: {ChannelId}. AzureTopicName: {AzureTopicName}. Inspected: {Inspected}. PreservedOpen: {PreservedOpen}. PreservedNonGenerated: {PreservedNonGenerated}. DeletedOrphan: {DeletedOrphan}. DeletedClosed: {DeletedClosed}. DeleteFailures: {DeleteFailures}.",
                    channel.ChannelId,
                    channel.AzureTopicName,
                    reconciliationResult.InspectedSubscriptions,
                    reconciliationResult.PreservedOpenSubscriptions,
                    reconciliationResult.PreservedNonGeneratedSubscriptions,
                    reconciliationResult.DeletedOrphanSubscriptions,
                    reconciliationResult.DeletedClosedSubscriptions,
                    reconciliationResult.DeleteFailures);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Azure Service Bus subscription reconciliation failed before opening subscription session. ChannelId: {ChannelId}. AzureTopicName: {AzureTopicName}.",
                channel.ChannelId,
                channel.AzureTopicName);
        }
    }

    public async Task<CloseConsumerSubscriptionSessionResult> CloseSubscriptionSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sessionId, out Guid parsedSessionId))
        {
            return CloseConsumerSubscriptionSessionResult.Failure(CloseSubscriptionSessionFailureKind.ParameterFault);
        }

        GetSubscriptionSessionResult sessionResult = await _sessionRepository.GetSubscriptionSessionAsync(parsedSessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Session is null)
        {
            return sessionResult.FailureKind switch
            {
                RapidRedPanda.ISBM.Host.Data.GetSubscriptionSessionFailureKind.NotFound => CloseConsumerSubscriptionSessionResult.Failure(CloseSubscriptionSessionFailureKind.SessionFault),
                _ => CloseConsumerSubscriptionSessionResult.Failure(CloseSubscriptionSessionFailureKind.OperationFault)
            };
        }

        ConsumerSubscriptionSessionData session = sessionResult.Session;
        if (!string.Equals(session.SessionType, SubscriptionSessionTypes.ConsumerPublicationSubscription, StringComparison.Ordinal))
        {
            return CloseConsumerSubscriptionSessionResult.Failure(CloseSubscriptionSessionFailureKind.SessionFault);
        }

        if (!string.Equals(session.SessionState, SubscriptionSessionStates.Closed, StringComparison.Ordinal))
        {
            try
            {
                await _subscriptionTransport.DeleteSubscriptionAsync(
                    session.AzureTopicName,
                    session.AzureSubscriptionName,
                    cancellationToken);
            }
            catch (RequestFailedException exception)
            {
                _logger.LogError(
                    exception,
                    "Azure Service Bus subscription deletion failed while closing subscription session. SessionId: {SessionId}. ChannelId: {ChannelId}. AzureTopicName: {AzureTopicName}. AzureSubscriptionName: {AzureSubscriptionName}. Status: {AzureStatus}. ErrorCode: {AzureErrorCode}.",
                    session.SessionId,
                    session.ChannelId,
                    session.AzureTopicName,
                    session.AzureSubscriptionName,
                    exception.Status,
                    exception.ErrorCode);
                return CloseConsumerSubscriptionSessionResult.Failure(CloseSubscriptionSessionFailureKind.OperationFault);
            }
        }

        CloseSubscriptionSessionResult closeResult = await _sessionRepository.CloseSubscriptionSessionAsync(parsedSessionId, cancellationToken);
        if (closeResult.Succeeded)
        {
            ProviderPublicationRepositoryResult deletedRecipientsResult =
                await _publicationRepository.MarkRecipientsSubscriptionDeletedAsync(parsedSessionId, cancellationToken);
            if (!deletedRecipientsResult.Succeeded)
            {
                return CloseConsumerSubscriptionSessionResult.Failure(CloseSubscriptionSessionFailureKind.OperationFault);
            }

            return CloseConsumerSubscriptionSessionResult.Success();
        }

        return closeResult.FailureKind switch
        {
            RapidRedPanda.ISBM.Host.Data.CloseSubscriptionSessionFailureKind.NotFound => CloseConsumerSubscriptionSessionResult.Failure(CloseSubscriptionSessionFailureKind.SessionFault),
            _ => CloseConsumerSubscriptionSessionResult.Failure(CloseSubscriptionSessionFailureKind.OperationFault)
        };
    }

    public async Task<ReadPublicationResult> ReadPublicationAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        GetOpenConsumerSessionResult sessionResult = await GetOpenConsumerSubscriptionSessionAsync(sessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Session is null)
        {
            return sessionResult.FailureKind switch
            {
                OpenConsumerSessionFailureKind.ParameterFault => ReadPublicationResult.Failure(ReadPublicationFailureKind.ParameterFault),
                OpenConsumerSessionFailureKind.SessionFault => ReadPublicationResult.Failure(ReadPublicationFailureKind.SessionFault),
                _ => ReadPublicationResult.Failure(ReadPublicationFailureKind.OperationFault)
            };
        }

        ConsumerSubscriptionSessionData session = sessionResult.Session;

        ReadProviderPublicationResult durableOutstanding = await _publicationRepository.GetOutstandingPublicationAsync(
            session.SessionId,
            cancellationToken);
        if (!durableOutstanding.Succeeded)
        {
            return ReadPublicationResult.Failure(ReadPublicationFailureKind.OperationFault);
        }

        if (durableOutstanding.HasPublication && durableOutstanding.Publication is ProviderPublicationData outstandingPublication)
        {
            return ToReadPublicationResult(session, outstandingPublication);
        }

        try
        {
            for (int skippedCount = 0; skippedCount < 100; skippedCount++)
            {
                TransportPublication? publication = await _subscriptionTransport.ReadPublicationAsync(
                    session.SessionId,
                    session.AzureTopicName,
                    session.AzureSubscriptionName,
                    cancellationToken);

                if (publication is null)
                {
                    return ReadPublicationResult.NoPublication();
                }

                ReadProviderPublicationResult claimResult =
                    await _publicationRepository.TryMarkPublicationOutstandingAsync(
                        publication.PublicationId,
                        session.SessionId,
                        cancellationToken);
                if (!claimResult.Succeeded)
                {
                    return ReadPublicationResult.Failure(ReadPublicationFailureKind.OperationFault);
                }

                if (claimResult.HasPublication && claimResult.Publication is ProviderPublicationData claimedPublication)
                {
                    return ToReadPublicationResult(session, claimedPublication);
                }

                PublicationExpirationStateResult expirationState =
                    await _publicationRepository.IsPublicationExpiredAsync(
                        publication.PublicationId,
                        session.SessionId,
                        cancellationToken);
                if (!expirationState.Succeeded)
                {
                    return ReadPublicationResult.Failure(ReadPublicationFailureKind.OperationFault);
                }

                await _subscriptionTransport.RemovePublicationAsync(
                    session.SessionId,
                    session.AzureTopicName,
                    session.AzureSubscriptionName,
                    cancellationToken);

                if (expirationState.IsExpired)
                {
                    ProviderPublicationRepositoryResult skippedResult =
                        await _publicationRepository.MarkRecipientSkippedExpiredAsync(
                            publication.PublicationId,
                            session.SessionId,
                            cancellationToken);
                    if (!skippedResult.Succeeded)
                    {
                        return ReadPublicationResult.Failure(ReadPublicationFailureKind.OperationFault);
                    }
                }
            }

            _logger.LogError(
                "Too many expired publications skipped during one read operation. SessionId: {SessionId}. ChannelId: {ChannelId}. AzureTopicName: {AzureTopicName}. AzureSubscriptionName: {AzureSubscriptionName}.",
                session.SessionId,
                session.ChannelId,
                session.AzureTopicName,
                session.AzureSubscriptionName);
            return ReadPublicationResult.Failure(ReadPublicationFailureKind.OperationFault);
        }
        catch (RequestFailedException exception)
        {
            _logger.LogError(
                exception,
                "Azure Service Bus receive failed while reading publication. SessionId: {SessionId}. ChannelId: {ChannelId}. AzureTopicName: {AzureTopicName}. AzureSubscriptionName: {AzureSubscriptionName}. Status: {AzureStatus}. ErrorCode: {AzureErrorCode}.",
                session.SessionId,
                session.ChannelId,
                session.AzureTopicName,
                session.AzureSubscriptionName,
                exception.Status,
                exception.ErrorCode);
            return ReadPublicationResult.Failure(ReadPublicationFailureKind.OperationFault);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Unexpected transport failure while reading publication. SessionId: {SessionId}. ChannelId: {ChannelId}. AzureTopicName: {AzureTopicName}. AzureSubscriptionName: {AzureSubscriptionName}.",
                session.SessionId,
                session.ChannelId,
                session.AzureTopicName,
                session.AzureSubscriptionName);
            return ReadPublicationResult.Failure(ReadPublicationFailureKind.OperationFault);
        }
    }

    public async Task<RemovePublicationResult> RemovePublicationAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        GetOpenConsumerSessionResult sessionResult = await GetOpenConsumerSubscriptionSessionAsync(sessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Session is null)
        {
            return sessionResult.FailureKind switch
            {
                OpenConsumerSessionFailureKind.ParameterFault => RemovePublicationResult.Failure(RemovePublicationFailureKind.ParameterFault),
                OpenConsumerSessionFailureKind.SessionFault => RemovePublicationResult.Failure(RemovePublicationFailureKind.SessionFault),
                _ => RemovePublicationResult.Failure(RemovePublicationFailureKind.OperationFault)
            };
        }

        ConsumerSubscriptionSessionData session = sessionResult.Session;

        try
        {
            RemoveProviderPublicationRecipientResult removeResult =
                await _publicationRepository.RemoveOutstandingPublicationAsync(
                    session.SessionId,
                    cancellationToken);
            if (!removeResult.Succeeded)
            {
                return RemovePublicationResult.Failure(RemovePublicationFailureKind.OperationFault);
            }

            TransportPublication? outstandingPublication = await _subscriptionTransport.GetOutstandingPublicationAsync(
                session.SessionId,
                cancellationToken);

            if (removeResult.MessageId is Guid removedMessageId &&
                outstandingPublication is not null &&
                outstandingPublication.PublicationId == removedMessageId)
            {
                await _subscriptionTransport.RemovePublicationAsync(
                    session.SessionId,
                    session.AzureTopicName,
                    session.AzureSubscriptionName,
                    cancellationToken);
            }

            return RemovePublicationResult.Success();
        }
        catch (RequestFailedException exception)
        {
            _logger.LogError(
                exception,
                "Azure Service Bus settlement failed while removing publication. SessionId: {SessionId}. ChannelId: {ChannelId}. AzureTopicName: {AzureTopicName}. AzureSubscriptionName: {AzureSubscriptionName}. Status: {AzureStatus}. ErrorCode: {AzureErrorCode}.",
                session.SessionId,
                session.ChannelId,
                session.AzureTopicName,
                session.AzureSubscriptionName,
                exception.Status,
                exception.ErrorCode);
            return RemovePublicationResult.Failure(RemovePublicationFailureKind.OperationFault);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Unexpected transport failure while removing publication. SessionId: {SessionId}. ChannelId: {ChannelId}. AzureTopicName: {AzureTopicName}. AzureSubscriptionName: {AzureSubscriptionName}.",
                session.SessionId,
                session.ChannelId,
                session.AzureTopicName,
                session.AzureSubscriptionName);
            return RemovePublicationResult.Failure(RemovePublicationFailureKind.OperationFault);
        }
    }

    private ReadPublicationResult ToReadPublicationResult(
        ConsumerSubscriptionSessionData session,
        TransportPublication publication)
    {
        if (!BrokerMessageContentAdapter.TryReadMessageContent(publication, out InternalMessageContent? messageContent))
        {
            _logger.LogError(
                "Publication broker message body did not contain a valid messageContent envelope. SessionId: {SessionId}. PublicationId: {PublicationId}.",
                session.SessionId,
                publication.PublicationId);
            return ReadPublicationResult.Failure(ReadPublicationFailureKind.OperationFault);
        }

        return ReadPublicationResult.Success(new ReadPublicationData(
            publication.PublicationId,
            publication.Topics,
            messageContent!));
    }

    private ReadPublicationResult ToReadPublicationResult(
        ConsumerSubscriptionSessionData session,
        ProviderPublicationData publication)
    {
        if (!SqlMessageContentAdapter.TryFromSql(publication.MessageContent, out InternalMessageContent? messageContent))
        {
            _logger.LogError(
                "Stored publication message content was not a valid MessageContent value. SessionId: {SessionId}. PublicationId: {PublicationId}.",
                session.SessionId,
                publication.MessageId);
            return ReadPublicationResult.Failure(ReadPublicationFailureKind.OperationFault);
        }

        return ReadPublicationResult.Success(new ReadPublicationData(
            publication.MessageId,
            publication.Topics,
            messageContent!));
    }

    private async Task<GetOpenConsumerSessionResult> GetOpenConsumerSubscriptionSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sessionId, out Guid parsedSessionId))
        {
            return GetOpenConsumerSessionResult.Failure(OpenConsumerSessionFailureKind.ParameterFault);
        }

        GetSubscriptionSessionResult sessionResult = await _sessionRepository.GetSubscriptionSessionAsync(parsedSessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Session is null)
        {
            return sessionResult.FailureKind switch
            {
                RapidRedPanda.ISBM.Host.Data.GetSubscriptionSessionFailureKind.NotFound => GetOpenConsumerSessionResult.Failure(OpenConsumerSessionFailureKind.SessionFault),
                _ => GetOpenConsumerSessionResult.Failure(OpenConsumerSessionFailureKind.OperationFault)
            };
        }

        ConsumerSubscriptionSessionData session = sessionResult.Session;
        if (!string.Equals(session.SessionType, SubscriptionSessionTypes.ConsumerPublicationSubscription, StringComparison.Ordinal) ||
            !string.Equals(session.SessionState, SubscriptionSessionStates.Open, StringComparison.Ordinal))
        {
            return GetOpenConsumerSessionResult.Failure(OpenConsumerSessionFailureKind.SessionFault);
        }

        return GetOpenConsumerSessionResult.Success(session);
    }

    private enum OpenConsumerSessionFailureKind
    {
        ParameterFault,
        SessionFault,
        OperationFault
    }

    private sealed record GetOpenConsumerSessionResult(
        bool Succeeded,
        ConsumerSubscriptionSessionData? Session,
        OpenConsumerSessionFailureKind? FailureKind)
    {
        public static GetOpenConsumerSessionResult Success(ConsumerSubscriptionSessionData session)
        {
            return new GetOpenConsumerSessionResult(true, session, null);
        }

        public static GetOpenConsumerSessionResult Failure(OpenConsumerSessionFailureKind failureKind)
        {
            return new GetOpenConsumerSessionResult(false, null, failureKind);
        }
    }

    private static bool TryDecodeAndValidateChannelUri(string channelId, out string decodedChannelUri)
    {
        try
        {
            decodedChannelUri = Uri.UnescapeDataString(channelId);
        }
        catch (UriFormatException)
        {
            decodedChannelUri = string.Empty;
            return false;
        }

        return ChannelValidationRules.IsValidChannelUri(decodedChannelUri);
    }

    private sealed class NullProviderPublicationRepository : IProviderPublicationRepository
    {
        public Task<ProviderPublicationRepositoryResult> InsertPublishedPublicationAsync(
            ProviderPublicationCreateData publication,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ProviderPublicationRepositoryResult.Success());
        }

        public Task<ProviderPublicationRepositoryResult> ExpirePublicationAsync(
            Guid providerSessionId,
            Guid channelId,
            Guid messageId,
            DateTime expiredUtc,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ProviderPublicationRepositoryResult.Success());
        }

        public Task<PublicationExpirationStateResult> IsPublicationExpiredAsync(
            Guid messageId,
            Guid consumerSessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(PublicationExpirationStateResult.Success(false));
        }

        public Task<ProviderPublicationRepositoryResult> MarkRecipientOutstandingAsync(
            Guid messageId,
            Guid consumerSessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ProviderPublicationRepositoryResult.Success());
        }

        public Task<ProviderPublicationRepositoryResult> MarkRecipientSkippedExpiredAsync(
            Guid messageId,
            Guid consumerSessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ProviderPublicationRepositoryResult.Success());
        }

        public Task<ProviderPublicationRepositoryResult> MarkRecipientRemovedAsync(
            Guid messageId,
            Guid consumerSessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ProviderPublicationRepositoryResult.Success());
        }

        public Task<ProviderPublicationRepositoryResult> MarkRecipientsSubscriptionDeletedAsync(
            Guid consumerSessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ProviderPublicationRepositoryResult.Success());
        }
    }
}
