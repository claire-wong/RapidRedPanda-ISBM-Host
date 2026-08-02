using RapidRedPanda.ISBM.Host.Data;

namespace RapidRedPanda.ISBM.Host.Services;

public interface ISessionCloseService
{
    Task<CloseSessionResult> CloseSessionAsync(
        string sessionId,
        CancellationToken cancellationToken);
}

public enum CloseSessionFailureKind
{
    ParameterFault,
    SessionFault,
    OperationFault
}

public sealed record CloseSessionResult(
    bool Succeeded,
    CloseSessionFailureKind? FailureKind)
{
    public static CloseSessionResult Success()
    {
        return new CloseSessionResult(true, null);
    }

    public static CloseSessionResult Failure(CloseSessionFailureKind failureKind)
    {
        return new CloseSessionResult(false, failureKind);
    }
}

public sealed class SessionCloseService : ISessionCloseService
{
    private readonly ISubscriptionSessionRepository _consumerSessionRepository;
    private readonly IProviderPublicationSessionRepository _providerSessionRepository;
    private readonly IProviderRequestSessionRepository _providerRequestSessionRepository;
    private readonly IConsumerRequestSessionRepository _consumerRequestSessionRepository;
    private readonly IConsumerSubscriptionSessionService _consumerSessionService;
    private readonly IProviderPublicationSessionService _providerSessionService;
    private readonly IProviderRequestSessionService _providerRequestSessionService;
    private readonly IConsumerRequestSessionService _consumerRequestSessionService;
    private readonly ILogger<SessionCloseService> _logger;

    public SessionCloseService(
        ISubscriptionSessionRepository consumerSessionRepository,
        IProviderPublicationSessionRepository providerSessionRepository,
        IProviderRequestSessionRepository providerRequestSessionRepository,
        IConsumerRequestSessionRepository consumerRequestSessionRepository,
        IConsumerSubscriptionSessionService consumerSessionService,
        IProviderPublicationSessionService providerSessionService,
        IProviderRequestSessionService providerRequestSessionService,
        IConsumerRequestSessionService consumerRequestSessionService,
        ILogger<SessionCloseService> logger)
    {
        _consumerSessionRepository = consumerSessionRepository;
        _providerSessionRepository = providerSessionRepository;
        _providerRequestSessionRepository = providerRequestSessionRepository;
        _consumerRequestSessionRepository = consumerRequestSessionRepository;
        _consumerSessionService = consumerSessionService;
        _providerSessionService = providerSessionService;
        _providerRequestSessionService = providerRequestSessionService;
        _consumerRequestSessionService = consumerRequestSessionService;
        _logger = logger;
    }

    public SessionCloseService(
        ISubscriptionSessionRepository consumerSessionRepository,
        IProviderPublicationSessionRepository providerSessionRepository,
        IConsumerSubscriptionSessionService consumerSessionService,
        IProviderPublicationSessionService providerSessionService,
        ILogger<SessionCloseService> logger)
        : this(
            consumerSessionRepository,
            providerSessionRepository,
            new NullProviderRequestSessionRepository(),
            new NullConsumerRequestSessionRepository(),
            consumerSessionService,
            providerSessionService,
            new NullProviderRequestSessionService(),
            new NullConsumerRequestSessionService(),
            logger)
    {
    }

    public async Task<CloseSessionResult> CloseSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sessionId, out Guid parsedSessionId))
        {
            return CloseSessionResult.Failure(CloseSessionFailureKind.ParameterFault);
        }

        GetSubscriptionSessionResult consumerResult = await _consumerSessionRepository.GetSubscriptionSessionAsync(
            parsedSessionId,
            cancellationToken);
        if (!consumerResult.Succeeded &&
            consumerResult.FailureKind != GetSubscriptionSessionFailureKind.NotFound)
        {
            return CloseSessionResult.Failure(CloseSessionFailureKind.OperationFault);
        }

        GetProviderPublicationSessionResult providerResult = await _providerSessionRepository.GetProviderPublicationSessionAsync(
            parsedSessionId,
            cancellationToken);
        if (!providerResult.Succeeded &&
            providerResult.FailureKind != GetProviderPublicationSessionFailureKind.NotFound)
        {
            return CloseSessionResult.Failure(CloseSessionFailureKind.OperationFault);
        }

        GetProviderRequestSessionResult providerRequestResult = await _providerRequestSessionRepository.GetProviderRequestSessionAsync(
            parsedSessionId,
            cancellationToken);
        if (!providerRequestResult.Succeeded &&
            providerRequestResult.FailureKind != GetProviderRequestSessionFailureKind.NotFound)
        {
            return CloseSessionResult.Failure(CloseSessionFailureKind.OperationFault);
        }

        GetConsumerRequestSessionResult consumerRequestResult = await _consumerRequestSessionRepository.GetConsumerRequestSessionAsync(
            parsedSessionId,
            cancellationToken);
        if (!consumerRequestResult.Succeeded &&
            consumerRequestResult.FailureKind != GetConsumerRequestSessionFailureKind.NotFound)
        {
            return CloseSessionResult.Failure(CloseSessionFailureKind.OperationFault);
        }

        bool hasActiveConsumerSession = consumerResult.Session is ConsumerSubscriptionSessionData consumerSession &&
            string.Equals(consumerSession.SessionType, SubscriptionSessionTypes.ConsumerPublicationSubscription, StringComparison.Ordinal) &&
            string.Equals(consumerSession.SessionState, SubscriptionSessionStates.Open, StringComparison.Ordinal);

        bool hasActiveProviderRequestSession = providerRequestResult.Session is ProviderRequestSessionData providerRequestSession &&
            string.Equals(providerRequestSession.SessionState, SubscriptionSessionStates.Open, StringComparison.Ordinal);

        bool hasActiveProviderSession = providerResult.Session is ProviderPublicationSessionData providerSession &&
            string.Equals(providerSession.SessionState, ProviderPublicationSessionStates.Open, StringComparison.Ordinal) &&
            providerSession.ClosedUtc is null;

        bool hasActiveConsumerRequestSession = consumerRequestResult.Session is ConsumerRequestSessionData requestSession &&
            string.Equals(requestSession.SessionState, ConsumerRequestSessionStates.Open, StringComparison.Ordinal) &&
            requestSession.ClosedUtc is null;

        int activeSessionStoreCount = new[]
        {
            hasActiveConsumerSession,
            hasActiveProviderRequestSession,
            hasActiveProviderSession,
            hasActiveConsumerRequestSession
        }.Count(value => value);

        if (activeSessionStoreCount > 1)
        {
            _logger.LogError(
                "Session ID {SessionId} is active in more than one session store.",
                parsedSessionId);
            return CloseSessionResult.Failure(CloseSessionFailureKind.OperationFault);
        }

        if (hasActiveConsumerSession)
        {
            CloseConsumerSubscriptionSessionResult closeResult = await _consumerSessionService.CloseSubscriptionSessionAsync(
                sessionId,
                cancellationToken);
            return ToSharedResult(closeResult);
        }

        if (hasActiveProviderSession)
        {
            ClosePublicationSessionResult closeResult = await _providerSessionService.ClosePublicationSessionAsync(
                sessionId,
                cancellationToken);
            return ToSharedResult(closeResult);
        }

        if (hasActiveProviderRequestSession)
        {
            CloseProviderRequestSessionResult closeResult = await _providerRequestSessionService.CloseProviderRequestSessionAsync(
                sessionId,
                cancellationToken);
            return ToSharedResult(closeResult);
        }

        if (hasActiveConsumerRequestSession)
        {
            CloseConsumerRequestSessionServiceResult closeResult = await _consumerRequestSessionService.CloseConsumerRequestSessionAsync(
                sessionId,
                cancellationToken);
            return ToSharedResult(closeResult);
        }

        return CloseSessionResult.Failure(CloseSessionFailureKind.SessionFault);
    }

    private static CloseSessionResult ToSharedResult(CloseConsumerSubscriptionSessionResult closeResult)
    {
        if (closeResult.Succeeded)
        {
            return CloseSessionResult.Success();
        }

        return closeResult.FailureKind switch
        {
            CloseSubscriptionSessionFailureKind.ParameterFault => CloseSessionResult.Failure(CloseSessionFailureKind.ParameterFault),
            CloseSubscriptionSessionFailureKind.SessionFault => CloseSessionResult.Failure(CloseSessionFailureKind.SessionFault),
            _ => CloseSessionResult.Failure(CloseSessionFailureKind.OperationFault)
        };
    }

    private static CloseSessionResult ToSharedResult(ClosePublicationSessionResult closeResult)
    {
        if (closeResult.Succeeded)
        {
            return CloseSessionResult.Success();
        }

        return closeResult.FailureKind switch
        {
            ClosePublicationSessionFailureKind.ParameterFault => CloseSessionResult.Failure(CloseSessionFailureKind.ParameterFault),
            ClosePublicationSessionFailureKind.SessionFault => CloseSessionResult.Failure(CloseSessionFailureKind.SessionFault),
            _ => CloseSessionResult.Failure(CloseSessionFailureKind.OperationFault)
        };
    }

    private static CloseSessionResult ToSharedResult(CloseProviderRequestSessionResult closeResult)
    {
        if (closeResult.Succeeded)
        {
            return CloseSessionResult.Success();
        }

        return closeResult.FailureKind switch
        {
            CloseProviderRequestSessionFailureKind.ParameterFault => CloseSessionResult.Failure(CloseSessionFailureKind.ParameterFault),
            CloseProviderRequestSessionFailureKind.SessionFault => CloseSessionResult.Failure(CloseSessionFailureKind.SessionFault),
            _ => CloseSessionResult.Failure(CloseSessionFailureKind.OperationFault)
        };
    }

    private static CloseSessionResult ToSharedResult(CloseConsumerRequestSessionServiceResult closeResult)
    {
        if (closeResult.Succeeded)
        {
            return CloseSessionResult.Success();
        }

        return closeResult.FailureKind switch
        {
            CloseConsumerRequestSessionServiceFailureKind.ParameterFault => CloseSessionResult.Failure(CloseSessionFailureKind.ParameterFault),
            CloseConsumerRequestSessionServiceFailureKind.SessionFault => CloseSessionResult.Failure(CloseSessionFailureKind.SessionFault),
            _ => CloseSessionResult.Failure(CloseSessionFailureKind.OperationFault)
        };
    }

    private sealed class NullConsumerRequestSessionRepository : IConsumerRequestSessionRepository
    {
        public Task<InsertConsumerRequestSessionResult> InsertConsumerRequestSessionAsync(
            ConsumerRequestSessionCreateData session,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(InsertConsumerRequestSessionResult.Success());
        }

        public Task<GetConsumerRequestSessionResult> GetConsumerRequestSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(GetConsumerRequestSessionResult.Failure(
                GetConsumerRequestSessionFailureKind.NotFound,
                "missing"));
        }

        public Task<Data.CloseConsumerRequestSessionResult> CloseConsumerRequestSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Data.CloseConsumerRequestSessionResult.Success());
        }
    }

    private sealed class NullProviderRequestSessionRepository : IProviderRequestSessionRepository
    {
        public Task<InsertProviderRequestSessionResult> InsertProviderRequestSessionAsync(
            ProviderRequestSessionCreateData session,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(InsertProviderRequestSessionResult.Success());
        }

        public Task<GetProviderRequestSessionResult> GetProviderRequestSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(GetProviderRequestSessionResult.Failure(
                GetProviderRequestSessionFailureKind.NotFound,
                "missing"));
        }

        public Task<CloseProviderRequestSessionRepositoryResult> CloseProviderRequestSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(CloseProviderRequestSessionRepositoryResult.Success());
        }
    }

    private sealed class NullProviderRequestSessionService : IProviderRequestSessionService
    {
        public Task<OpenProviderRequestSessionResult> OpenProviderRequestSessionAsync(
            string channelId,
            IReadOnlyList<string> topics,
            string? listenerUrl,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(OpenProviderRequestSessionResult.Failure(OpenProviderRequestSessionFailureKind.ChannelFault));
        }

        public Task<CloseProviderRequestSessionResult> CloseProviderRequestSessionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(CloseProviderRequestSessionResult.Failure(CloseProviderRequestSessionFailureKind.SessionFault));
        }

        public Task<ReadRequestResult> ReadRequestAsync(string sessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(ReadRequestResult.NoRequest());
        }

        public Task<RemoveRequestResult> RemoveRequestAsync(string sessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(RemoveRequestResult.Success());
        }

        public Task<PostResponseResult> PostResponseAsync(
            string sessionId,
            string requestMessageId,
            BinaryData messageContent,
            string? mediaType,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(PostResponseResult.Failure(PostResponseFailureKind.SessionFault));
        }
    }

    private sealed class NullConsumerRequestSessionService : IConsumerRequestSessionService
    {
        public Task<OpenConsumerRequestSessionResult> OpenConsumerRequestSessionAsync(
            string channelId,
            string? listenerUrl,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(OpenConsumerRequestSessionResult.Failure(OpenConsumerRequestSessionFailureKind.ChannelFault));
        }

        public Task<CloseConsumerRequestSessionServiceResult> CloseConsumerRequestSessionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(CloseConsumerRequestSessionServiceResult.Failure(CloseConsumerRequestSessionServiceFailureKind.SessionFault));
        }

        public Task<PostRequestResult> PostRequestAsync(
            string sessionId,
            IReadOnlyList<string?>? topics,
            BinaryData messageContent,
            string? mediaType,
            string? expiry,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(PostRequestResult.Failure(PostRequestFailureKind.SessionFault));
        }

        public Task<ExpireRequestResult> ExpireRequestAsync(
            string sessionId,
            string messageId,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ExpireRequestResult.Failure(ExpireRequestFailureKind.SessionFault));
        }

        public Task<ReadResponseResult> ReadResponseAsync(
            string sessionId,
            string requestMessageId,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ReadResponseResult.Failure(ReadResponseFailureKind.SessionFault));
        }

        public Task<RemoveResponseResult> RemoveResponseAsync(
            string sessionId,
            string requestMessageId,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(RemoveResponseResult.Failure(RemoveResponseFailureKind.SessionFault));
        }
    }
}
