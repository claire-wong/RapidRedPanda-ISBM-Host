using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Validation;
using System.Xml;

namespace RapidRedPanda.ISBM.Host.Services;

public interface IConsumerRequestSessionService
{
    Task<OpenConsumerRequestSessionResult> OpenConsumerRequestSessionAsync(
        string channelId,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken);

    Task<CloseConsumerRequestSessionServiceResult> CloseConsumerRequestSessionAsync(
        string sessionId,
        CancellationToken cancellationToken);

    Task<PostRequestResult> PostRequestAsync(
        string sessionId,
        IReadOnlyList<string?>? topics,
        InternalMessageContent messageContent,
        string? expiry,
        CancellationToken cancellationToken);

    Task<ExpireRequestResult> ExpireRequestAsync(
        string sessionId,
        string messageId,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken);

    Task<ReadResponseResult> ReadResponseAsync(
        string sessionId,
        string requestMessageId,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken);

    Task<RemoveResponseResult> RemoveResponseAsync(
        string sessionId,
        string requestMessageId,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken);
}

public enum OpenConsumerRequestSessionFailureKind
{
    ParameterFault,
    ChannelFault,
    SecurityTokenFault,
    OperationFault
}

public sealed record OpenConsumerRequestSessionResult(
    bool Succeeded,
    Guid? SessionId,
    OpenConsumerRequestSessionFailureKind? FailureKind)
{
    public static OpenConsumerRequestSessionResult Success(Guid sessionId)
    {
        return new OpenConsumerRequestSessionResult(true, sessionId, null);
    }

    public static OpenConsumerRequestSessionResult Failure(OpenConsumerRequestSessionFailureKind failureKind)
    {
        return new OpenConsumerRequestSessionResult(false, null, failureKind);
    }
}

public enum CloseConsumerRequestSessionServiceFailureKind
{
    ParameterFault,
    SessionFault,
    OperationFault
}

public sealed record CloseConsumerRequestSessionServiceResult(
    bool Succeeded,
    CloseConsumerRequestSessionServiceFailureKind? FailureKind)
{
    public static CloseConsumerRequestSessionServiceResult Success()
    {
        return new CloseConsumerRequestSessionServiceResult(true, null);
    }

    public static CloseConsumerRequestSessionServiceResult Failure(CloseConsumerRequestSessionServiceFailureKind failureKind)
    {
        return new CloseConsumerRequestSessionServiceResult(false, failureKind);
    }
}

public enum PostRequestFailureKind
{
    ParameterFault,
    SessionFault,
    ChannelFault,
    OperationFault
}

public sealed record PostRequestResult(
    bool Succeeded,
    Guid? MessageId,
    PostRequestFailureKind? FailureKind)
{
    public static PostRequestResult Success(Guid messageId)
    {
        return new PostRequestResult(true, messageId, null);
    }

    public static PostRequestResult Failure(PostRequestFailureKind failureKind)
    {
        return new PostRequestResult(false, null, failureKind);
    }
}

public enum ExpireRequestFailureKind
{
    ParameterFault,
    SessionFault,
    OperationFault
}

public sealed record ExpireRequestResult(
    bool Succeeded,
    ExpireRequestFailureKind? FailureKind)
{
    public static ExpireRequestResult Success()
    {
        return new ExpireRequestResult(true, null);
    }

    public static ExpireRequestResult Failure(ExpireRequestFailureKind failureKind)
    {
        return new ExpireRequestResult(false, failureKind);
    }
}

public enum ReadResponseFailureKind
{
    ParameterFault,
    SessionFault,
    OperationFault
}

public sealed record ReadResponseData(
    Guid RequestMessageId,
    Guid MessageId,
    InternalMessageContent MessageContent);

public sealed record ReadResponseResult(
    bool Succeeded,
    bool HasResponse,
    ReadResponseData? Response,
    ReadResponseFailureKind? FailureKind)
{
    public static ReadResponseResult Success(ReadResponseData response)
    {
        return new ReadResponseResult(true, true, response, null);
    }

    public static ReadResponseResult NoResponse()
    {
        return new ReadResponseResult(true, false, null, null);
    }

    public static ReadResponseResult Failure(ReadResponseFailureKind failureKind)
    {
        return new ReadResponseResult(false, false, null, failureKind);
    }
}

public enum RemoveResponseFailureKind
{
    ParameterFault,
    SessionFault,
    OperationFault
}

public sealed record RemoveResponseResult(
    bool Succeeded,
    RemoveResponseFailureKind? FailureKind)
{
    public static RemoveResponseResult Success()
    {
        return new RemoveResponseResult(true, null);
    }

    public static RemoveResponseResult Failure(RemoveResponseFailureKind failureKind)
    {
        return new RemoveResponseResult(false, failureKind);
    }
}

public sealed class ConsumerRequestSessionService : IConsumerRequestSessionService
{
    private readonly IChannelRepository _channelRepository;
    private readonly IConsumerRequestSessionRepository _sessionRepository;
    private readonly IConsumerRequestRepository _requestRepository;
    private readonly IRequestResponseRepository _responseRepository;
    private readonly IPublicationTransport _publicationTransport;
    private readonly ILogger<ConsumerRequestSessionService> _logger;

    public ConsumerRequestSessionService(
        IChannelRepository channelRepository,
        IConsumerRequestSessionRepository sessionRepository,
        IConsumerRequestRepository requestRepository,
        IRequestResponseRepository responseRepository,
        IPublicationTransport publicationTransport,
        ILogger<ConsumerRequestSessionService> logger)
    {
        _channelRepository = channelRepository;
        _sessionRepository = sessionRepository;
        _requestRepository = requestRepository;
        _responseRepository = responseRepository;
        _publicationTransport = publicationTransport;
        _logger = logger;
    }

    public async Task<OpenConsumerRequestSessionResult> OpenConsumerRequestSessionAsync(
        string channelId,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        if (!TryDecodeAndValidateChannelUri(channelId, out string decodedChannelUri))
        {
            return OpenConsumerRequestSessionResult.Failure(OpenConsumerRequestSessionFailureKind.ParameterFault);
        }

        GetChannelForSubscriptionResult channelResult = await _channelRepository.GetChannelForConsumerRequestSessionAsync(
            decodedChannelUri,
            credentials,
            cancellationToken);

        if (!channelResult.Succeeded || channelResult.Channel is null)
        {
            return channelResult.FailureKind switch
            {
                GetChannelForSubscriptionFailureKind.NotFound => OpenConsumerRequestSessionResult.Failure(OpenConsumerRequestSessionFailureKind.ChannelFault),
                GetChannelForSubscriptionFailureKind.SecurityTokenInvalid => OpenConsumerRequestSessionResult.Failure(OpenConsumerRequestSessionFailureKind.SecurityTokenFault),
                _ => OpenConsumerRequestSessionResult.Failure(OpenConsumerRequestSessionFailureKind.OperationFault)
            };
        }

        Guid sessionId = Guid.NewGuid();
        InsertConsumerRequestSessionResult insertResult = await _sessionRepository.InsertConsumerRequestSessionAsync(
            new ConsumerRequestSessionCreateData(
                sessionId,
                channelResult.Channel.ChannelId,
                channelResult.Channel.AuthenticatedTokenUsername),
            cancellationToken);

        return insertResult.Succeeded
            ? OpenConsumerRequestSessionResult.Success(sessionId)
            : OpenConsumerRequestSessionResult.Failure(OpenConsumerRequestSessionFailureKind.OperationFault);
    }

    public async Task<CloseConsumerRequestSessionServiceResult> CloseConsumerRequestSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        GetOpenConsumerRequestSessionResult sessionResult = await GetOpenConsumerRequestSessionAsync(sessionId, cancellationToken);
        if (!sessionResult.Succeeded)
        {
            return sessionResult.FailureKind switch
            {
                ConsumerRequestSessionLookupFailureKind.ParameterFault => CloseConsumerRequestSessionServiceResult.Failure(CloseConsumerRequestSessionServiceFailureKind.ParameterFault),
                ConsumerRequestSessionLookupFailureKind.SessionFault => CloseConsumerRequestSessionServiceResult.Failure(CloseConsumerRequestSessionServiceFailureKind.SessionFault),
                _ => CloseConsumerRequestSessionServiceResult.Failure(CloseConsumerRequestSessionServiceFailureKind.OperationFault)
            };
        }

        CloseConsumerRequestSessionResult closeResult = await _sessionRepository.CloseConsumerRequestSessionAsync(
            sessionResult.Session!.SessionId,
            cancellationToken);

        return closeResult.Succeeded
            ? CloseConsumerRequestSessionServiceResult.Success()
            : closeResult.FailureKind == Data.CloseConsumerRequestSessionFailureKind.NotFound
                ? CloseConsumerRequestSessionServiceResult.Failure(CloseConsumerRequestSessionServiceFailureKind.SessionFault)
                : CloseConsumerRequestSessionServiceResult.Failure(CloseConsumerRequestSessionServiceFailureKind.OperationFault);
    }

    public async Task<PostRequestResult> PostRequestAsync(
        string sessionId,
        IReadOnlyList<string?>? topics,
        InternalMessageContent messageContent,
        string? expiry,
        CancellationToken cancellationToken)
    {
        if (messageContent is null ||
            !TryValidateTopics(topics, out IReadOnlyList<string>? validatedTopics) ||
            !TryParseExpiry(expiry, out TimeSpan? timeToLive))
        {
            return PostRequestResult.Failure(PostRequestFailureKind.ParameterFault);
        }

        GetOpenConsumerRequestSessionResult sessionResult = await GetOpenConsumerRequestSessionAsync(sessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Session is null)
        {
            return sessionResult.FailureKind switch
            {
                ConsumerRequestSessionLookupFailureKind.ParameterFault => PostRequestResult.Failure(PostRequestFailureKind.ParameterFault),
                ConsumerRequestSessionLookupFailureKind.SessionFault => PostRequestResult.Failure(PostRequestFailureKind.SessionFault),
                _ => PostRequestResult.Failure(PostRequestFailureKind.OperationFault)
            };
        }

        ConsumerRequestSessionData session = sessionResult.Session;
        GetChannelTopicBindingResult channelResult = await _channelRepository.GetChannelTopicBindingByIdAsync(
            session.ChannelId,
            cancellationToken);
        if (!channelResult.Succeeded || channelResult.Channel is null)
        {
            return channelResult.FailureKind == GetChannelTopicBindingFailureKind.NotFound
                ? PostRequestResult.Failure(PostRequestFailureKind.ChannelFault)
                : PostRequestResult.Failure(PostRequestFailureKind.OperationFault);
        }

        ChannelTopicBindingData channel = channelResult.Channel;
        if (!string.Equals(channel.ChannelType, "Request", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(channel.AzureTopicName))
        {
            return PostRequestResult.Failure(PostRequestFailureKind.ChannelFault);
        }

        PublicationMessageEnvelope envelope;
        try
        {
            envelope = PublicationMessageFactory.CreatePublicationMessage(messageContent, validatedTopics!);
            envelope.Message.ContentType = messageContent.MediaType;
            envelope.Message.ApplicationProperties["isbmMessageKind"] = "Request";
            envelope.Message.ApplicationProperties["isbmConsumerRequestSessionId"] = session.SessionId.ToString("D");
            if (timeToLive is TimeSpan parsedTimeToLive)
            {
                envelope.Message.TimeToLive = parsedTimeToLive;
            }
        }
        catch (ArgumentException)
        {
            return PostRequestResult.Failure(PostRequestFailureKind.ParameterFault);
        }

        try
        {
            await _publicationTransport.SendPublicationAsync(channel.AzureTopicName, envelope.Message, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Request send failed. SessionId: {SessionId}. MessageId: {MessageId}. ChannelId: {ChannelId}.", session.SessionId, envelope.PublicationId, session.ChannelId);
            return PostRequestResult.Failure(PostRequestFailureKind.OperationFault);
        }

        DateTime createdUtc = DateTime.UtcNow;
        ConsumerRequestRepositoryResult insertRequestResult = await _requestRepository.InsertPostedRequestAsync(
            new ConsumerRequestCreateData(
                envelope.PublicationId,
                session.SessionId,
                session.ChannelId,
                envelope.Topics,
                createdUtc,
                timeToLive is TimeSpan ttl ? createdUtc.Add(ttl) : null),
            cancellationToken);

        if (!insertRequestResult.Succeeded)
        {
            _logger.LogError("Request metadata insert failed after Azure send. SessionId: {SessionId}. MessageId: {MessageId}. ChannelId: {ChannelId}.", session.SessionId, envelope.PublicationId, session.ChannelId);
            return PostRequestResult.Failure(PostRequestFailureKind.OperationFault);
        }

        return PostRequestResult.Success(envelope.PublicationId);
    }

    public async Task<ExpireRequestResult> ExpireRequestAsync(
        string sessionId,
        string messageId,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(messageId, out Guid parsedMessageId))
        {
            return ExpireRequestResult.Failure(ExpireRequestFailureKind.ParameterFault);
        }

        GetOpenConsumerRequestSessionResult sessionResult = await GetOpenConsumerRequestSessionAsync(sessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Session is null)
        {
            return sessionResult.FailureKind switch
            {
                ConsumerRequestSessionLookupFailureKind.ParameterFault => ExpireRequestResult.Failure(ExpireRequestFailureKind.ParameterFault),
                ConsumerRequestSessionLookupFailureKind.SessionFault => ExpireRequestResult.Failure(ExpireRequestFailureKind.SessionFault),
                _ => ExpireRequestResult.Failure(ExpireRequestFailureKind.OperationFault)
            };
        }

        ValidateChannelCredentialsResult credentialsResult = await _channelRepository.ValidateChannelCredentialsAsync(
            sessionResult.Session.ChannelId,
            sessionResult.Session.AuthenticatedTokenUsername,
            credentials,
            cancellationToken);
        if (!credentialsResult.Succeeded)
        {
            return credentialsResult.FailureKind == ValidateChannelCredentialsFailureKind.DatabaseFailure
                ? ExpireRequestResult.Failure(ExpireRequestFailureKind.OperationFault)
                : ExpireRequestResult.Failure(ExpireRequestFailureKind.SessionFault);
        }

        ConsumerRequestRepositoryResult expireResult = await _requestRepository.ExpireRequestAsync(
            sessionResult.Session.SessionId,
            sessionResult.Session.ChannelId,
            parsedMessageId,
            DateTime.UtcNow,
            cancellationToken);

        return expireResult.Succeeded
            ? ExpireRequestResult.Success()
            : ExpireRequestResult.Failure(ExpireRequestFailureKind.OperationFault);
    }

    public async Task<ReadResponseResult> ReadResponseAsync(
        string sessionId,
        string requestMessageId,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(requestMessageId, out Guid parsedRequestMessageId))
        {
            return ReadResponseResult.Failure(ReadResponseFailureKind.ParameterFault);
        }

        GetOpenConsumerRequestSessionResult sessionResult = await GetOpenConsumerRequestSessionAsync(sessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Session is null)
        {
            return sessionResult.FailureKind switch
            {
                ConsumerRequestSessionLookupFailureKind.ParameterFault => ReadResponseResult.Failure(ReadResponseFailureKind.ParameterFault),
                ConsumerRequestSessionLookupFailureKind.SessionFault => ReadResponseResult.Failure(ReadResponseFailureKind.SessionFault),
                _ => ReadResponseResult.Failure(ReadResponseFailureKind.OperationFault)
            };
        }

        ValidateChannelCredentialsResult credentialsResult = await _channelRepository.ValidateChannelCredentialsAsync(
            sessionResult.Session.ChannelId,
            sessionResult.Session.AuthenticatedTokenUsername,
            credentials,
            cancellationToken);
        if (!credentialsResult.Succeeded)
        {
            return credentialsResult.FailureKind == ValidateChannelCredentialsFailureKind.DatabaseFailure
                ? ReadResponseResult.Failure(ReadResponseFailureKind.OperationFault)
                : ReadResponseResult.Failure(ReadResponseFailureKind.SessionFault);
        }

        ReadRequestResponseResult readResult = await _responseRepository.ReadResponseAsync(
            sessionResult.Session.SessionId,
            parsedRequestMessageId,
            cancellationToken);

        if (readResult.Succeeded && readResult.HasResponse && readResult.Response is RequestResponseData response)
        {
            if (!SqlMessageContentAdapter.TryFromSql(response.MessageContent, response.MediaType, out InternalMessageContent? messageContent))
            {
                _logger.LogError("Stored response message content was not a valid MessageContent value. RequestMessageId: {RequestMessageId}. MessageId: {MessageId}.", response.RequestMessageId, response.MessageId);
                return ReadResponseResult.Failure(ReadResponseFailureKind.OperationFault);
            }

            return ReadResponseResult.Success(new ReadResponseData(response.RequestMessageId, response.MessageId, messageContent!));
        }

        if (readResult.Succeeded)
        {
            return ReadResponseResult.NoResponse();
        }

        return readResult.FailureKind == RequestResponseRepositoryFailureKind.RequestNotFound
            ? ReadResponseResult.Failure(ReadResponseFailureKind.SessionFault)
            : ReadResponseResult.Failure(ReadResponseFailureKind.OperationFault);
    }

    public async Task<RemoveResponseResult> RemoveResponseAsync(
        string sessionId,
        string requestMessageId,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(requestMessageId, out Guid parsedRequestMessageId))
        {
            return RemoveResponseResult.Failure(RemoveResponseFailureKind.ParameterFault);
        }

        GetOpenConsumerRequestSessionResult sessionResult = await GetOpenConsumerRequestSessionAsync(sessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Session is null)
        {
            return sessionResult.FailureKind switch
            {
                ConsumerRequestSessionLookupFailureKind.ParameterFault => RemoveResponseResult.Failure(RemoveResponseFailureKind.ParameterFault),
                ConsumerRequestSessionLookupFailureKind.SessionFault => RemoveResponseResult.Failure(RemoveResponseFailureKind.SessionFault),
                _ => RemoveResponseResult.Failure(RemoveResponseFailureKind.OperationFault)
            };
        }

        ValidateChannelCredentialsResult credentialsResult = await _channelRepository.ValidateChannelCredentialsAsync(
            sessionResult.Session.ChannelId,
            sessionResult.Session.AuthenticatedTokenUsername,
            credentials,
            cancellationToken);
        if (!credentialsResult.Succeeded)
        {
            return credentialsResult.FailureKind == ValidateChannelCredentialsFailureKind.DatabaseFailure
                ? RemoveResponseResult.Failure(RemoveResponseFailureKind.OperationFault)
                : RemoveResponseResult.Failure(RemoveResponseFailureKind.SessionFault);
        }

        RequestResponseRepositoryResult removeResult = await _responseRepository.RemoveOutstandingResponseAsync(
            sessionResult.Session.SessionId,
            parsedRequestMessageId,
            cancellationToken);

        if (removeResult.Succeeded)
        {
            return RemoveResponseResult.Success();
        }

        return removeResult.FailureKind == RequestResponseRepositoryFailureKind.RequestNotFound
            ? RemoveResponseResult.Failure(RemoveResponseFailureKind.SessionFault)
            : RemoveResponseResult.Failure(RemoveResponseFailureKind.OperationFault);
    }

    private async Task<GetOpenConsumerRequestSessionResult> GetOpenConsumerRequestSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sessionId, out Guid parsedSessionId))
        {
            return GetOpenConsumerRequestSessionResult.Failure(ConsumerRequestSessionLookupFailureKind.ParameterFault);
        }

        GetConsumerRequestSessionResult sessionResult = await _sessionRepository.GetConsumerRequestSessionAsync(parsedSessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Session is null)
        {
            return sessionResult.FailureKind == GetConsumerRequestSessionFailureKind.NotFound
                ? GetOpenConsumerRequestSessionResult.Failure(ConsumerRequestSessionLookupFailureKind.SessionFault)
                : GetOpenConsumerRequestSessionResult.Failure(ConsumerRequestSessionLookupFailureKind.OperationFault);
        }

        ConsumerRequestSessionData session = sessionResult.Session;
        if (!string.Equals(session.SessionState, ConsumerRequestSessionStates.Open, StringComparison.Ordinal) ||
            session.ClosedUtc is not null)
        {
            return GetOpenConsumerRequestSessionResult.Failure(ConsumerRequestSessionLookupFailureKind.SessionFault);
        }

        return GetOpenConsumerRequestSessionResult.Success(session);
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

    private static bool TryValidateTopics(IReadOnlyList<string?>? topics, out IReadOnlyList<string>? validatedTopics)
    {
        validatedTopics = null;
        if (topics is null || topics.Count == 0)
        {
            return false;
        }

        List<string> topicValues = [];
        foreach (string? topic in topics)
        {
            if (string.IsNullOrWhiteSpace(topic))
            {
                return false;
            }

            topicValues.Add(topic);
        }

        try
        {
            validatedTopics = TopicSelectionRules.DeduplicateTopics(topicValues);
            return validatedTopics.Count > 0 &&
                validatedTopics.Count <= PublicationRoutingProperties.MaxPublicationTopics;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryParseExpiry(string? expiry, out TimeSpan? timeToLive)
    {
        timeToLive = null;
        if (expiry is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(expiry))
        {
            return false;
        }

        try
        {
            TimeSpan parsedTimeToLive = XmlConvert.ToTimeSpan(expiry);
            if (parsedTimeToLive <= TimeSpan.Zero)
            {
                return false;
            }

            timeToLive = parsedTimeToLive;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private enum ConsumerRequestSessionLookupFailureKind
    {
        ParameterFault,
        SessionFault,
        OperationFault
    }

    private sealed record GetOpenConsumerRequestSessionResult(
        bool Succeeded,
        ConsumerRequestSessionData? Session,
        ConsumerRequestSessionLookupFailureKind? FailureKind)
    {
        public static GetOpenConsumerRequestSessionResult Success(ConsumerRequestSessionData session)
        {
            return new GetOpenConsumerRequestSessionResult(true, session, null);
        }

        public static GetOpenConsumerRequestSessionResult Failure(ConsumerRequestSessionLookupFailureKind failureKind)
        {
            return new GetOpenConsumerRequestSessionResult(false, null, failureKind);
        }
    }
}
