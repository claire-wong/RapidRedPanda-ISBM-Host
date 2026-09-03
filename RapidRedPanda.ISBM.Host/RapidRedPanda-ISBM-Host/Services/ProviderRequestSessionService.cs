using Azure;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Validation;

namespace RapidRedPanda.ISBM.Host.Services;

public interface IProviderRequestSessionService
{
    Task<OpenProviderRequestSessionResult> OpenProviderRequestSessionAsync(
        string channelId,
        IReadOnlyList<string> topics,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken);

    Task<CloseProviderRequestSessionResult> CloseProviderRequestSessionAsync(
        string sessionId,
        CancellationToken cancellationToken);

    Task<ReadRequestResult> ReadRequestAsync(
        string sessionId,
        CancellationToken cancellationToken);

    Task<RemoveRequestResult> RemoveRequestAsync(
        string sessionId,
        CancellationToken cancellationToken);

    Task<PostResponseResult> PostResponseAsync(
        string sessionId,
        string requestMessageId,
        InternalMessageContent messageContent,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken);
}

public enum OpenProviderRequestSessionFailureKind
{
    ParameterFault,
    ChannelFault,
    SecurityTokenFault,
    OperationFault
}

public sealed record OpenProviderRequestSessionResult(
    bool Succeeded,
    Guid? SessionId,
    OpenProviderRequestSessionFailureKind? FailureKind)
{
    public static OpenProviderRequestSessionResult Success(Guid sessionId)
    {
        return new OpenProviderRequestSessionResult(true, sessionId, null);
    }

    public static OpenProviderRequestSessionResult Failure(OpenProviderRequestSessionFailureKind failureKind)
    {
        return new OpenProviderRequestSessionResult(false, null, failureKind);
    }
}

public enum CloseProviderRequestSessionFailureKind
{
    ParameterFault,
    SessionFault,
    OperationFault
}

public sealed record CloseProviderRequestSessionResult(
    bool Succeeded,
    CloseProviderRequestSessionFailureKind? FailureKind)
{
    public static CloseProviderRequestSessionResult Success()
    {
        return new CloseProviderRequestSessionResult(true, null);
    }

    public static CloseProviderRequestSessionResult Failure(CloseProviderRequestSessionFailureKind failureKind)
    {
        return new CloseProviderRequestSessionResult(false, failureKind);
    }
}

public enum ReadRequestFailureKind
{
    ParameterFault,
    SessionFault,
    OperationFault
}

public sealed record ReadRequestData(
    Guid MessageId,
    IReadOnlyList<string> Topics,
    InternalMessageContent MessageContent);

public sealed record ReadRequestResult(
    bool Succeeded,
    bool HasRequest,
    ReadRequestData? Request,
    ReadRequestFailureKind? FailureKind)
{
    public static ReadRequestResult Success(ReadRequestData request)
    {
        return new ReadRequestResult(true, true, request, null);
    }

    public static ReadRequestResult NoRequest()
    {
        return new ReadRequestResult(true, false, null, null);
    }

    public static ReadRequestResult Failure(ReadRequestFailureKind failureKind)
    {
        return new ReadRequestResult(false, false, null, failureKind);
    }
}

public enum RemoveRequestFailureKind
{
    ParameterFault,
    SessionFault,
    OperationFault
}

public sealed record RemoveRequestResult(
    bool Succeeded,
    RemoveRequestFailureKind? FailureKind)
{
    public static RemoveRequestResult Success()
    {
        return new RemoveRequestResult(true, null);
    }

    public static RemoveRequestResult Failure(RemoveRequestFailureKind failureKind)
    {
        return new RemoveRequestResult(false, failureKind);
    }
}

public enum PostResponseFailureKind
{
    ParameterFault,
    SessionFault,
    OperationFault
}

public sealed record PostResponseResult(
    bool Succeeded,
    bool ResponseCreated,
    Guid? MessageId,
    PostResponseFailureKind? FailureKind)
{
    public static PostResponseResult Success(Guid messageId)
    {
        return new PostResponseResult(true, true, messageId, null);
    }

    public static PostResponseResult NoAction()
    {
        return new PostResponseResult(true, false, null, null);
    }

    public static PostResponseResult Failure(PostResponseFailureKind failureKind)
    {
        return new PostResponseResult(false, false, null, failureKind);
    }
}

public sealed class ProviderRequestSessionService : IProviderRequestSessionService
{
    private readonly IChannelRepository _channelRepository;
    private readonly ISubscriptionSessionRepository _sessionRepository;
    private readonly IConsumerRequestRepository _requestRepository;
    private readonly IRequestResponseRepository _responseRepository;
    private readonly ISubscriptionTransport _subscriptionTransport;
    private readonly IChannelTopicTransport _topicTransport;
    private readonly ILogger<ProviderRequestSessionService> _logger;

    public ProviderRequestSessionService(
        IChannelRepository channelRepository,
        ISubscriptionSessionRepository sessionRepository,
        IConsumerRequestRepository requestRepository,
        IRequestResponseRepository responseRepository,
        ISubscriptionTransport subscriptionTransport,
        IChannelTopicTransport topicTransport,
        ILogger<ProviderRequestSessionService> logger)
    {
        _channelRepository = channelRepository;
        _sessionRepository = sessionRepository;
        _requestRepository = requestRepository;
        _responseRepository = responseRepository;
        _subscriptionTransport = subscriptionTransport;
        _topicTransport = topicTransport;
        _logger = logger;
    }

    public async Task<OpenProviderRequestSessionResult> OpenProviderRequestSessionAsync(
        string channelId,
        IReadOnlyList<string> topics,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        if (!TryDecodeAndValidateChannelUri(channelId, out string decodedChannelUri) ||
            topics.Count == 0 ||
            topics.Any(string.IsNullOrEmpty))
        {
            return OpenProviderRequestSessionResult.Failure(OpenProviderRequestSessionFailureKind.ParameterFault);
        }

        IReadOnlyList<string> requestedTopics = TopicSelectionRules.DeduplicateTopics(topics);
        try
        {
            _ = TopicSelectionRules.BuildAzureSqlFilter(requestedTopics);
        }
        catch (ArgumentException)
        {
            return OpenProviderRequestSessionResult.Failure(OpenProviderRequestSessionFailureKind.ParameterFault);
        }

        GetChannelForSubscriptionResult channelResult = await _channelRepository.GetChannelForProviderRequestSessionAsync(
            decodedChannelUri,
            credentials,
            cancellationToken);

        if (!channelResult.Succeeded || channelResult.Channel is null)
        {
            return channelResult.FailureKind switch
            {
                GetChannelForSubscriptionFailureKind.NotFound => OpenProviderRequestSessionResult.Failure(OpenProviderRequestSessionFailureKind.ChannelFault),
                GetChannelForSubscriptionFailureKind.SecurityTokenInvalid => OpenProviderRequestSessionResult.Failure(OpenProviderRequestSessionFailureKind.SecurityTokenFault),
                _ => OpenProviderRequestSessionResult.Failure(OpenProviderRequestSessionFailureKind.OperationFault)
            };
        }

        ChannelSubscriptionData channel = channelResult.Channel;
        if (!await EnsureTopicExistsAsync(channel, cancellationToken))
        {
            return OpenProviderRequestSessionResult.Failure(OpenProviderRequestSessionFailureKind.OperationFault);
        }

        Guid sessionId = Guid.NewGuid();
        string azureSubscriptionName = ChannelTransportNames.SubscriptionNameForSession(sessionId);
        bool azureSubscriptionCreated = false;

        try
        {
            await _subscriptionTransport.CreateSubscriptionAsync(
                channel.AzureTopicName,
                azureSubscriptionName,
                requestedTopics,
                cancellationToken);
            azureSubscriptionCreated = true;
        }
        catch (RequestFailedException exception)
        {
            _logger.LogError(exception, "Azure Service Bus subscription creation failed while opening provider request session. SessionId: {SessionId}. ChannelId: {ChannelId}.", sessionId, channel.ChannelId);
            return OpenProviderRequestSessionResult.Failure(OpenProviderRequestSessionFailureKind.OperationFault);
        }

        InsertSubscriptionSessionResult insertResult = await _sessionRepository.InsertProviderRequestSessionAsync(
            new ConsumerSubscriptionSessionCreateData(
                sessionId,
                channel.ChannelId,
                channel.AzureTopicName,
                azureSubscriptionName,
                requestedTopics,
                channel.AuthenticatedTokenUsername),
            cancellationToken);

        if (insertResult.Succeeded)
        {
            return OpenProviderRequestSessionResult.Success(sessionId);
        }

        if (azureSubscriptionCreated)
        {
            try
            {
                await _subscriptionTransport.DeleteSubscriptionAsync(channel.AzureTopicName, azureSubscriptionName, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Compensating cleanup failed after SQL insert failure while opening provider request session. SessionId: {SessionId}.", sessionId);
            }
        }

        return OpenProviderRequestSessionResult.Failure(OpenProviderRequestSessionFailureKind.OperationFault);
    }

    public async Task<CloseProviderRequestSessionResult> CloseProviderRequestSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sessionId, out Guid parsedSessionId))
        {
            return CloseProviderRequestSessionResult.Failure(CloseProviderRequestSessionFailureKind.ParameterFault);
        }

        GetSubscriptionSessionResult sessionResult = await _sessionRepository.GetSubscriptionSessionAsync(parsedSessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Session is null)
        {
            return sessionResult.FailureKind == GetSubscriptionSessionFailureKind.NotFound
                ? CloseProviderRequestSessionResult.Failure(CloseProviderRequestSessionFailureKind.SessionFault)
                : CloseProviderRequestSessionResult.Failure(CloseProviderRequestSessionFailureKind.OperationFault);
        }

        ConsumerSubscriptionSessionData session = sessionResult.Session;
        if (!string.Equals(session.SessionType, SubscriptionSessionTypes.ProviderRequestSubscription, StringComparison.Ordinal))
        {
            return CloseProviderRequestSessionResult.Failure(CloseProviderRequestSessionFailureKind.SessionFault);
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
                _logger.LogError(exception, "Azure Service Bus subscription deletion failed while closing provider request session. SessionId: {SessionId}.", parsedSessionId);
                return CloseProviderRequestSessionResult.Failure(CloseProviderRequestSessionFailureKind.OperationFault);
            }
        }

        CloseSubscriptionSessionResult closeResult = await _sessionRepository.CloseSubscriptionSessionAsync(parsedSessionId, cancellationToken);
        return closeResult.Succeeded
            ? CloseProviderRequestSessionResult.Success()
            : closeResult.FailureKind == Data.CloseSubscriptionSessionFailureKind.NotFound
                ? CloseProviderRequestSessionResult.Failure(CloseProviderRequestSessionFailureKind.SessionFault)
                : CloseProviderRequestSessionResult.Failure(CloseProviderRequestSessionFailureKind.OperationFault);
    }

    public async Task<ReadRequestResult> ReadRequestAsync(string sessionId, CancellationToken cancellationToken)
    {
        GetOpenProviderRequestSessionResult sessionResult = await GetOpenProviderRequestSessionAsync(sessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Session is null)
        {
            return sessionResult.FailureKind switch
            {
                OpenProviderRequestSessionLookupFailureKind.ParameterFault => ReadRequestResult.Failure(ReadRequestFailureKind.ParameterFault),
                OpenProviderRequestSessionLookupFailureKind.SessionFault => ReadRequestResult.Failure(ReadRequestFailureKind.SessionFault),
                _ => ReadRequestResult.Failure(ReadRequestFailureKind.OperationFault)
            };
        }

        ConsumerSubscriptionSessionData session = sessionResult.Session;
        try
        {
            TransportPublication? outstandingRequest = await _subscriptionTransport.GetOutstandingPublicationAsync(session.SessionId, cancellationToken);
            if (outstandingRequest is not null)
            {
                return ToReadRequestResult(session, outstandingRequest);
            }

            for (int skippedCount = 0; skippedCount < 100; skippedCount++)
            {
                TransportPublication? request = await _subscriptionTransport.ReadPublicationAsync(
                    session.SessionId,
                    session.AzureTopicName,
                    session.AzureSubscriptionName,
                    cancellationToken);

                if (request is null)
                {
                    return ReadRequestResult.NoRequest();
                }

                RequestExpirationStateResult expirationState = await _requestRepository.IsRequestExpiredAsync(
                    request.PublicationId,
                    session.SessionId,
                    cancellationToken);
                if (!expirationState.Succeeded)
                {
                    return ReadRequestResult.Failure(ReadRequestFailureKind.OperationFault);
                }

                if (!expirationState.IsExpired)
                {
                    return ToReadRequestResult(session, request);
                }

                await _subscriptionTransport.RemovePublicationAsync(
                    session.SessionId,
                    session.AzureTopicName,
                    session.AzureSubscriptionName,
                    cancellationToken);
            }

            _logger.LogError("Too many expired requests skipped during one read operation. SessionId: {SessionId}.", session.SessionId);
            return ReadRequestResult.Failure(ReadRequestFailureKind.OperationFault);
        }
        catch (RequestFailedException exception)
        {
            _logger.LogError(exception, "Azure Service Bus receive failed while reading request. SessionId: {SessionId}.", session.SessionId);
            return ReadRequestResult.Failure(ReadRequestFailureKind.OperationFault);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected transport failure while reading request. SessionId: {SessionId}.", session.SessionId);
            return ReadRequestResult.Failure(ReadRequestFailureKind.OperationFault);
        }
    }

    public async Task<RemoveRequestResult> RemoveRequestAsync(string sessionId, CancellationToken cancellationToken)
    {
        GetOpenProviderRequestSessionResult sessionResult = await GetOpenProviderRequestSessionAsync(sessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Session is null)
        {
            return sessionResult.FailureKind switch
            {
                OpenProviderRequestSessionLookupFailureKind.ParameterFault => RemoveRequestResult.Failure(RemoveRequestFailureKind.ParameterFault),
                OpenProviderRequestSessionLookupFailureKind.SessionFault => RemoveRequestResult.Failure(RemoveRequestFailureKind.SessionFault),
                _ => RemoveRequestResult.Failure(RemoveRequestFailureKind.OperationFault)
            };
        }

        ConsumerSubscriptionSessionData session = sessionResult.Session;
        try
        {
            await _subscriptionTransport.RemovePublicationAsync(
                session.SessionId,
                session.AzureTopicName,
                session.AzureSubscriptionName,
                cancellationToken);

            return RemoveRequestResult.Success();
        }
        catch (RequestFailedException exception)
        {
            _logger.LogError(exception, "Azure Service Bus settlement failed while removing request. SessionId: {SessionId}.", session.SessionId);
            return RemoveRequestResult.Failure(RemoveRequestFailureKind.OperationFault);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected transport failure while removing request. SessionId: {SessionId}.", session.SessionId);
            return RemoveRequestResult.Failure(RemoveRequestFailureKind.OperationFault);
        }
    }

    public async Task<PostResponseResult> PostResponseAsync(
        string sessionId,
        string requestMessageId,
        InternalMessageContent messageContent,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(requestMessageId, out Guid parsedRequestMessageId) ||
            messageContent is null)
        {
            return PostResponseResult.Failure(PostResponseFailureKind.ParameterFault);
        }

        GetOpenProviderRequestSessionResult sessionResult = await GetOpenProviderRequestSessionAsync(sessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Session is null)
        {
            return sessionResult.FailureKind switch
            {
                OpenProviderRequestSessionLookupFailureKind.ParameterFault => PostResponseResult.Failure(PostResponseFailureKind.ParameterFault),
                OpenProviderRequestSessionLookupFailureKind.SessionFault => PostResponseResult.Failure(PostResponseFailureKind.SessionFault),
                _ => PostResponseResult.Failure(PostResponseFailureKind.OperationFault)
            };
        }

        ConsumerSubscriptionSessionData session = sessionResult.Session;
        ValidateChannelCredentialsResult credentialsResult = await _channelRepository.ValidateChannelCredentialsAsync(
            session.ChannelId,
            session.AuthenticatedTokenUsername,
            credentials,
            cancellationToken);
        if (!credentialsResult.Succeeded)
        {
            return credentialsResult.FailureKind == ValidateChannelCredentialsFailureKind.DatabaseFailure
                ? PostResponseResult.Failure(PostResponseFailureKind.OperationFault)
                : PostResponseResult.Failure(PostResponseFailureKind.SessionFault);
        }

        ActiveRequestMatchResult activeRequest = await _responseRepository.FindActiveRequestAsync(
            parsedRequestMessageId,
            session.ChannelId,
            cancellationToken);
        if (!activeRequest.Succeeded)
        {
            return PostResponseResult.Failure(PostResponseFailureKind.OperationFault);
        }

        if (!activeRequest.HasActiveRequest || activeRequest.ConsumerRequestSessionId is not Guid consumerRequestSessionId)
        {
            return PostResponseResult.NoAction();
        }

        Guid responseMessageId = Guid.NewGuid();
        SqlMessageContent sqlMessageContent = SqlMessageContentAdapter.ToSql(messageContent);
        RequestResponseRepositoryResult insertResult = await _responseRepository.InsertResponseAsync(
            new RequestResponseCreateData(
                responseMessageId,
                parsedRequestMessageId,
                consumerRequestSessionId,
                session.SessionId,
                session.ChannelId,
                sqlMessageContent.MessageContent,
                sqlMessageContent.MediaType,
                DateTime.UtcNow),
            cancellationToken);

        return insertResult.Succeeded
            ? PostResponseResult.Success(responseMessageId)
            : PostResponseResult.Failure(PostResponseFailureKind.OperationFault);
    }

    private async Task<bool> EnsureTopicExistsAsync(ChannelSubscriptionData channel, CancellationToken cancellationToken)
    {
        try
        {
            if (await _topicTransport.TopicExistsAsync(channel.AzureTopicName, cancellationToken))
            {
                return true;
            }

            await _topicTransport.CreateTopicAsync(channel.AzureTopicName, cancellationToken);
            return true;
        }
        catch (RequestFailedException exception)
        {
            _logger.LogError(exception, "Azure Service Bus topic repair failed while opening provider request session. ChannelId: {ChannelId}.", channel.ChannelId);
            return false;
        }
    }

    private ReadRequestResult ToReadRequestResult(ConsumerSubscriptionSessionData session, TransportPublication request)
    {
        if (!BrokerMessageContentAdapter.TryReadMessageContent(request, out InternalMessageContent? messageContent))
        {
            _logger.LogError("Request broker message body did not contain a valid messageContent envelope. SessionId: {SessionId}. MessageId: {MessageId}.", session.SessionId, request.PublicationId);
            return ReadRequestResult.Failure(ReadRequestFailureKind.OperationFault);
        }

        return ReadRequestResult.Success(new ReadRequestData(request.PublicationId, request.Topics, messageContent!));
    }

    private async Task<GetOpenProviderRequestSessionResult> GetOpenProviderRequestSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sessionId, out Guid parsedSessionId))
        {
            return GetOpenProviderRequestSessionResult.Failure(OpenProviderRequestSessionLookupFailureKind.ParameterFault);
        }

        GetSubscriptionSessionResult sessionResult = await _sessionRepository.GetSubscriptionSessionAsync(parsedSessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Session is null)
        {
            return sessionResult.FailureKind == GetSubscriptionSessionFailureKind.NotFound
                ? GetOpenProviderRequestSessionResult.Failure(OpenProviderRequestSessionLookupFailureKind.SessionFault)
                : GetOpenProviderRequestSessionResult.Failure(OpenProviderRequestSessionLookupFailureKind.OperationFault);
        }

        ConsumerSubscriptionSessionData session = sessionResult.Session;
        if (!string.Equals(session.SessionType, SubscriptionSessionTypes.ProviderRequestSubscription, StringComparison.Ordinal) ||
            !string.Equals(session.SessionState, SubscriptionSessionStates.Open, StringComparison.Ordinal))
        {
            return GetOpenProviderRequestSessionResult.Failure(OpenProviderRequestSessionLookupFailureKind.SessionFault);
        }

        return GetOpenProviderRequestSessionResult.Success(session);
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

    private enum OpenProviderRequestSessionLookupFailureKind
    {
        ParameterFault,
        SessionFault,
        OperationFault
    }

    private sealed record GetOpenProviderRequestSessionResult(
        bool Succeeded,
        ConsumerSubscriptionSessionData? Session,
        OpenProviderRequestSessionLookupFailureKind? FailureKind)
    {
        public static GetOpenProviderRequestSessionResult Success(ConsumerSubscriptionSessionData session)
        {
            return new GetOpenProviderRequestSessionResult(true, session, null);
        }

        public static GetOpenProviderRequestSessionResult Failure(OpenProviderRequestSessionLookupFailureKind failureKind)
        {
            return new GetOpenProviderRequestSessionResult(false, null, failureKind);
        }
    }
}
