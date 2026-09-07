using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Validation;
using System.Xml;

namespace RapidRedPanda.ISBM.Host.Services;

public interface IProviderPublicationSessionService
{
    Task<OpenPublicationSessionResult> OpenPublicationSessionAsync(
        string channelId,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken);

    Task<ClosePublicationSessionResult> ClosePublicationSessionAsync(
        string sessionId,
        CancellationToken cancellationToken);

    Task<PostPublicationResult> PostPublicationAsync(
        string sessionId,
        IReadOnlyList<string?>? topics,
        InternalMessageContent messageContent,
        string? expiry,
        CancellationToken cancellationToken);

    Task<ExpirePublicationResult> ExpirePublicationAsync(
        string sessionId,
        string messageId,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}

public enum OpenPublicationSessionFailureKind
{
    ParameterFault,
    ChannelFault,
    SecurityTokenFault,
    OperationFault
}

public sealed record OpenPublicationSessionResult(
    bool Succeeded,
    Guid? SessionId,
    OpenPublicationSessionFailureKind? FailureKind)
{
    public static OpenPublicationSessionResult Success(Guid sessionId)
    {
        return new OpenPublicationSessionResult(true, sessionId, null);
    }

    public static OpenPublicationSessionResult Failure(OpenPublicationSessionFailureKind failureKind)
    {
        return new OpenPublicationSessionResult(false, null, failureKind);
    }
}

public enum ClosePublicationSessionFailureKind
{
    ParameterFault,
    SessionFault,
    OperationFault
}

public sealed record ClosePublicationSessionResult(
    bool Succeeded,
    ClosePublicationSessionFailureKind? FailureKind)
{
    public static ClosePublicationSessionResult Success()
    {
        return new ClosePublicationSessionResult(true, null);
    }

    public static ClosePublicationSessionResult Failure(ClosePublicationSessionFailureKind failureKind)
    {
        return new ClosePublicationSessionResult(false, failureKind);
    }
}

public enum PostPublicationFailureKind
{
    ParameterFault,
    SessionFault,
    ChannelFault,
    OperationFault
}

public sealed record PostPublicationResult(
    bool Succeeded,
    Guid? PublicationId,
    PostPublicationFailureKind? FailureKind)
{
    public static PostPublicationResult Success(Guid publicationId)
    {
        return new PostPublicationResult(true, publicationId, null);
    }

    public static PostPublicationResult Failure(PostPublicationFailureKind failureKind)
    {
        return new PostPublicationResult(false, null, failureKind);
    }
}

public enum ExpirePublicationFailureKind
{
    ParameterFault,
    SessionFault,
    MessageFault,
    ChannelFault,
    OperationFault
}

public sealed record ExpirePublicationResult(
    bool Succeeded,
    ExpirePublicationFailureKind? FailureKind)
{
    public static ExpirePublicationResult Success()
    {
        return new ExpirePublicationResult(true, null);
    }

    public static ExpirePublicationResult Failure(ExpirePublicationFailureKind failureKind)
    {
        return new ExpirePublicationResult(false, failureKind);
    }
}

public sealed class ProviderPublicationSessionService : IProviderPublicationSessionService
{
    private readonly IChannelRepository _channelRepository;
    private readonly IProviderPublicationSessionRepository _sessionRepository;
    private readonly IProviderPublicationRepository _publicationRepository;
    private readonly IPublicationTransport _publicationTransport;
    private readonly ILogger<ProviderPublicationSessionService> _logger;

    public ProviderPublicationSessionService(
        IChannelRepository channelRepository,
        IProviderPublicationSessionRepository sessionRepository,
        IProviderPublicationRepository publicationRepository,
        IPublicationTransport publicationTransport,
        ILogger<ProviderPublicationSessionService> logger)
    {
        _channelRepository = channelRepository;
        _sessionRepository = sessionRepository;
        _publicationRepository = publicationRepository;
        _publicationTransport = publicationTransport;
        _logger = logger;
    }

    public ProviderPublicationSessionService(
        IChannelRepository channelRepository,
        IProviderPublicationSessionRepository sessionRepository,
        IPublicationTransport publicationTransport,
        ILogger<ProviderPublicationSessionService> logger)
        : this(
            channelRepository,
            sessionRepository,
            new NullProviderPublicationRepository(),
            publicationTransport,
            logger)
    {
    }

    public async Task<OpenPublicationSessionResult> OpenPublicationSessionAsync(
        string channelId,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        if (!TryDecodeAndValidateChannelUri(channelId, out string decodedChannelUri))
        {
            return OpenPublicationSessionResult.Failure(OpenPublicationSessionFailureKind.ParameterFault);
        }

        GetChannelForSubscriptionResult channelResult = await _channelRepository.GetChannelForProviderPublicationSessionAsync(
            decodedChannelUri,
            credentials,
            cancellationToken);

        if (!channelResult.Succeeded || channelResult.Channel is null)
        {
            return channelResult.FailureKind switch
            {
                GetChannelForSubscriptionFailureKind.NotFound => OpenPublicationSessionResult.Failure(OpenPublicationSessionFailureKind.ChannelFault),
                GetChannelForSubscriptionFailureKind.SecurityTokenInvalid => OpenPublicationSessionResult.Failure(OpenPublicationSessionFailureKind.SecurityTokenFault),
                GetChannelForSubscriptionFailureKind.InvalidChannelType => OpenPublicationSessionResult.Failure(OpenPublicationSessionFailureKind.OperationFault),
                _ => OpenPublicationSessionResult.Failure(OpenPublicationSessionFailureKind.OperationFault)
            };
        }

        Guid sessionId = Guid.NewGuid();
        InsertProviderPublicationSessionResult insertResult = await _sessionRepository.InsertProviderPublicationSessionAsync(
            new ProviderPublicationSessionCreateData(
                sessionId,
                channelResult.Channel.ChannelId,
                channelResult.Channel.AuthenticatedTokenUsername),
            cancellationToken);

        if (insertResult.Succeeded)
        {
            return OpenPublicationSessionResult.Success(sessionId);
        }

        return OpenPublicationSessionResult.Failure(OpenPublicationSessionFailureKind.OperationFault);
    }

    public async Task<ClosePublicationSessionResult> ClosePublicationSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sessionId, out Guid parsedSessionId))
        {
            return ClosePublicationSessionResult.Failure(ClosePublicationSessionFailureKind.ParameterFault);
        }

        GetProviderPublicationSessionResult sessionResult = await _sessionRepository.GetProviderPublicationSessionAsync(
            parsedSessionId,
            cancellationToken);

        if (!sessionResult.Succeeded || sessionResult.Session is null)
        {
            return sessionResult.FailureKind switch
            {
                GetProviderPublicationSessionFailureKind.NotFound => ClosePublicationSessionResult.Failure(ClosePublicationSessionFailureKind.SessionFault),
                _ => ClosePublicationSessionResult.Failure(ClosePublicationSessionFailureKind.OperationFault)
            };
        }

        ProviderPublicationSessionData session = sessionResult.Session;
        if (!string.Equals(session.SessionState, ProviderPublicationSessionStates.Open, StringComparison.Ordinal) ||
            session.ClosedUtc is not null)
        {
            return ClosePublicationSessionResult.Failure(ClosePublicationSessionFailureKind.SessionFault);
        }

        CloseProviderPublicationSessionResult closeResult = await _sessionRepository.CloseProviderPublicationSessionAsync(
            parsedSessionId,
            cancellationToken);

        if (closeResult.Succeeded)
        {
            return ClosePublicationSessionResult.Success();
        }

        return closeResult.FailureKind switch
        {
            Data.CloseProviderPublicationSessionFailureKind.NotFound => ClosePublicationSessionResult.Failure(ClosePublicationSessionFailureKind.SessionFault),
            _ => ClosePublicationSessionResult.Failure(ClosePublicationSessionFailureKind.OperationFault)
        };
    }

    public async Task<PostPublicationResult> PostPublicationAsync(
        string sessionId,
        IReadOnlyList<string?>? topics,
        InternalMessageContent messageContent,
        string? expiry,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sessionId, out Guid parsedSessionId))
        {
            return PostPublicationResult.Failure(PostPublicationFailureKind.ParameterFault);
        }

        if (messageContent is null ||
            !TryValidateTopics(topics, out IReadOnlyList<string>? validatedTopics) ||
            !TryParseExpiry(expiry, out TimeSpan? timeToLive))
        {
            return PostPublicationResult.Failure(PostPublicationFailureKind.ParameterFault);
        }

        GetProviderPublicationSessionResult sessionResult = await _sessionRepository.GetProviderPublicationSessionAsync(
            parsedSessionId,
            cancellationToken);

        if (!sessionResult.Succeeded || sessionResult.Session is null)
        {
            return sessionResult.FailureKind switch
            {
                GetProviderPublicationSessionFailureKind.NotFound => PostPublicationResult.Failure(PostPublicationFailureKind.SessionFault),
                _ => PostPublicationResult.Failure(PostPublicationFailureKind.OperationFault)
            };
        }

        ProviderPublicationSessionData session = sessionResult.Session;
        if (!string.Equals(session.SessionState, ProviderPublicationSessionStates.Open, StringComparison.Ordinal) ||
            session.ClosedUtc is not null)
        {
            return PostPublicationResult.Failure(PostPublicationFailureKind.SessionFault);
        }

        GetChannelTopicBindingResult channelResult = await _channelRepository.GetChannelTopicBindingByIdAsync(
            session.ChannelId,
            cancellationToken);
        if (!channelResult.Succeeded || channelResult.Channel is null)
        {
            return channelResult.FailureKind switch
            {
                GetChannelTopicBindingFailureKind.NotFound => PostPublicationResult.Failure(PostPublicationFailureKind.ChannelFault),
                _ => PostPublicationResult.Failure(PostPublicationFailureKind.OperationFault)
            };
        }

        ChannelTopicBindingData channel = channelResult.Channel;
        if (!string.Equals(channel.ChannelType, "Publication", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(channel.AzureTopicName))
        {
            return PostPublicationResult.Failure(PostPublicationFailureKind.ChannelFault);
        }

        PublicationMessageEnvelope envelope;
        try
        {
            envelope = PublicationMessageFactory.CreatePublicationMessage(messageContent, validatedTopics!);
            envelope.Message.ContentType = messageContent.MediaType;
            if (timeToLive is TimeSpan parsedTimeToLive)
            {
                envelope.Message.TimeToLive = parsedTimeToLive;
            }
        }
        catch (ArgumentException)
        {
            return PostPublicationResult.Failure(PostPublicationFailureKind.ParameterFault);
        }

        try
        {
            await _publicationTransport.SendPublicationAsync(
                channel.AzureTopicName,
                envelope.Message,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Publication send failed. SessionId: {SessionId}. PublicationId: {PublicationId}. ChannelId: {ChannelId}. AzureTopicName: {AzureTopicName}. TopicCount: {TopicCount}.",
                session.SessionId,
                envelope.PublicationId,
                session.ChannelId,
                channel.AzureTopicName,
                envelope.Topics.Count);
            return PostPublicationResult.Failure(PostPublicationFailureKind.OperationFault);
        }

        ProviderPublicationRepositoryResult insertPublicationResult =
            await _publicationRepository.InsertPublishedPublicationAsync(
                new ProviderPublicationCreateData(
                    envelope.PublicationId,
                    session.SessionId,
                    session.ChannelId,
                    envelope.Topics,
                    DateTime.UtcNow,
                    timeToLive is TimeSpan ttl ? DateTime.UtcNow.Add(ttl) : null),
                cancellationToken);

        if (!insertPublicationResult.Succeeded)
        {
            _logger.LogError(
                "Publication metadata insert failed after Azure send. SessionId: {SessionId}. PublicationId: {PublicationId}. ChannelId: {ChannelId}.",
                session.SessionId,
                envelope.PublicationId,
                session.ChannelId);
            return PostPublicationResult.Failure(PostPublicationFailureKind.OperationFault);
        }

        return PostPublicationResult.Success(envelope.PublicationId);
    }

    public async Task<ExpirePublicationResult> ExpirePublicationAsync(
        string sessionId,
        string messageId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sessionId, out Guid parsedSessionId) ||
            !Guid.TryParse(messageId, out Guid parsedMessageId))
        {
            return ExpirePublicationResult.Failure(ExpirePublicationFailureKind.ParameterFault);
        }

        GetProviderPublicationSessionResult sessionResult = await _sessionRepository.GetProviderPublicationSessionAsync(
            parsedSessionId,
            cancellationToken);

        if (!sessionResult.Succeeded || sessionResult.Session is null)
        {
            return sessionResult.FailureKind switch
            {
                GetProviderPublicationSessionFailureKind.NotFound => ExpirePublicationResult.Failure(ExpirePublicationFailureKind.SessionFault),
                _ => ExpirePublicationResult.Failure(ExpirePublicationFailureKind.OperationFault)
            };
        }

        ProviderPublicationSessionData session = sessionResult.Session;
        if (!string.Equals(session.SessionState, ProviderPublicationSessionStates.Open, StringComparison.Ordinal) ||
            session.ClosedUtc is not null)
        {
            return ExpirePublicationResult.Failure(ExpirePublicationFailureKind.SessionFault);
        }

        ProviderPublicationRepositoryResult expireResult = await _publicationRepository.ExpirePublicationAsync(
            session.SessionId,
            session.ChannelId,
            parsedMessageId,
            DateTime.UtcNow,
            cancellationToken);

        if (expireResult.Succeeded)
        {
            return ExpirePublicationResult.Success();
        }

        return expireResult.FailureKind switch
        {
            ProviderPublicationRepositoryFailureKind.NotFound => ExpirePublicationResult.Failure(ExpirePublicationFailureKind.MessageFault),
            ProviderPublicationRepositoryFailureKind.ChannelMismatch => ExpirePublicationResult.Failure(ExpirePublicationFailureKind.MessageFault),
            _ => ExpirePublicationResult.Failure(ExpirePublicationFailureKind.OperationFault)
        };
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

    private static bool TryValidateTopics(
        IReadOnlyList<string?>? topics,
        out IReadOnlyList<string>? validatedTopics)
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
