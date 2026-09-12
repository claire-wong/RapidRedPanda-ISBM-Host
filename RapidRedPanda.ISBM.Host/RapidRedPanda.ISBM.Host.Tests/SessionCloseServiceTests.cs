using Microsoft.Extensions.Logging.Abstractions;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Services;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class SessionCloseServiceTests
{
    [Fact]
    public async Task CloseSession_OpenProviderSession_ClosesProviderOnly()
    {
        Guid sessionId = Guid.NewGuid();
        FakeProviderPublicationSessionRepository providerRepository = new()
        {
            SessionResult = GetProviderPublicationSessionResult.Success(OpenProviderSession(sessionId))
        };
        FakeProviderPublicationSessionService providerService = new();
        FakeConsumerSubscriptionSessionService consumerService = new();

        CloseSessionResult result = await CreateService(
                providerRepository: providerRepository,
                providerService: providerService,
                consumerService: consumerService)
            .CloseSessionAsync(sessionId.ToString(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(sessionId.ToString(), providerService.ClosedSessionId);
        Assert.Null(consumerService.ClosedSessionId);
    }

    [Fact]
    public async Task CloseSession_OpenConsumerSession_DelegatesToConsumerClosePreservingCleanupPath()
    {
        Guid sessionId = Guid.NewGuid();
        FakeSubscriptionSessionRepository consumerRepository = new()
        {
            SessionResult = GetSubscriptionSessionResult.Success(OpenConsumerSession(sessionId))
        };
        FakeConsumerSubscriptionSessionService consumerService = new();
        FakeProviderPublicationSessionService providerService = new();

        CloseSessionResult result = await CreateService(
                consumerRepository: consumerRepository,
                consumerService: consumerService,
                providerService: providerService)
            .CloseSessionAsync(sessionId.ToString(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(sessionId.ToString(), consumerService.ClosedSessionId);
        Assert.Null(providerService.ClosedSessionId);
    }

    [Fact]
    public async Task CloseSession_InvalidSessionId_ReturnsParameterFaultWithoutRepositoryCalls()
    {
        FakeSubscriptionSessionRepository consumerRepository = new();
        FakeProviderPublicationSessionRepository providerRepository = new();

        CloseSessionResult result = await CreateService(
                consumerRepository: consumerRepository,
                providerRepository: providerRepository)
            .CloseSessionAsync("not-a-guid", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(CloseSessionFailureKind.ParameterFault, result.FailureKind);
        Assert.Equal(0, consumerRepository.GetCalls);
        Assert.Equal(0, providerRepository.GetCalls);
    }

    [Fact]
    public async Task CloseSession_UnknownSession_ReturnsSessionFault()
    {
        CloseSessionResult result = await CreateService()
            .CloseSessionAsync(Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(CloseSessionFailureKind.SessionFault, result.FailureKind);
    }

    [Fact]
    public async Task CloseSession_ClosedProviderSession_ReturnsSessionFault()
    {
        Guid sessionId = Guid.NewGuid();
        FakeProviderPublicationSessionRepository providerRepository = new()
        {
            SessionResult = GetProviderPublicationSessionResult.Success(OpenProviderSession(
                sessionId,
                ProviderPublicationSessionStates.Closed,
                DateTime.UtcNow))
        };
        FakeProviderPublicationSessionService providerService = new();

        CloseSessionResult result = await CreateService(
                providerRepository: providerRepository,
                providerService: providerService)
            .CloseSessionAsync(sessionId.ToString(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(CloseSessionFailureKind.SessionFault, result.FailureKind);
        Assert.Null(providerService.ClosedSessionId);
    }

    [Fact]
    public async Task CloseSession_ProviderRepositoryFailure_ReturnsOperationFault()
    {
        FakeProviderPublicationSessionRepository providerRepository = new()
        {
            SessionResult = GetProviderPublicationSessionResult.Failure(
                GetProviderPublicationSessionFailureKind.DatabaseFailure,
                "failed")
        };

        CloseSessionResult result = await CreateService(providerRepository: providerRepository)
            .CloseSessionAsync(Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(CloseSessionFailureKind.OperationFault, result.FailureKind);
    }

    [Fact]
    public async Task CloseSession_SameActiveIdInBothStores_ReturnsOperationFaultAndClosesNeither()
    {
        Guid sessionId = Guid.NewGuid();
        FakeSubscriptionSessionRepository consumerRepository = new()
        {
            SessionResult = GetSubscriptionSessionResult.Success(OpenConsumerSession(sessionId))
        };
        FakeProviderPublicationSessionRepository providerRepository = new()
        {
            SessionResult = GetProviderPublicationSessionResult.Success(OpenProviderSession(sessionId))
        };
        FakeConsumerSubscriptionSessionService consumerService = new();
        FakeProviderPublicationSessionService providerService = new();

        CloseSessionResult result = await CreateService(
                consumerRepository,
                providerRepository,
                consumerService,
                providerService)
            .CloseSessionAsync(sessionId.ToString(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(CloseSessionFailureKind.OperationFault, result.FailureKind);
        Assert.Null(consumerService.ClosedSessionId);
        Assert.Null(providerService.ClosedSessionId);
    }

    [Fact]
    public async Task SessionCloseServiceSource_DoesNotReferenceAzureTransportsDirectly()
    {
        string source = await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "RapidRedPanda-ISBM-Host",
            "Services",
            "SessionCloseService.cs")));

        Assert.DoesNotContain("ISubscriptionTransport", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IChannelTopicTransport", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ServiceBus", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PublicationMessageFactory", source, StringComparison.Ordinal);
    }

    private static SessionCloseService CreateService(
        FakeSubscriptionSessionRepository? consumerRepository = null,
        FakeProviderPublicationSessionRepository? providerRepository = null,
        FakeConsumerSubscriptionSessionService? consumerService = null,
        FakeProviderPublicationSessionService? providerService = null)
    {
        return new SessionCloseService(
            consumerRepository ?? new FakeSubscriptionSessionRepository(),
            providerRepository ?? new FakeProviderPublicationSessionRepository(),
            consumerService ?? new FakeConsumerSubscriptionSessionService(),
            providerService ?? new FakeProviderPublicationSessionService(),
            NullLogger<SessionCloseService>.Instance);
    }

    private static ConsumerSubscriptionSessionData OpenConsumerSession(Guid sessionId)
    {
        return new ConsumerSubscriptionSessionData(
            sessionId,
            Guid.NewGuid(),
            SubscriptionSessionTypes.ConsumerPublicationSubscription,
            SubscriptionSessionStates.Open,
            "topic",
            $"isbm-css-{sessionId:N}",
            ["Topic-A"],
            null,
            DateTime.UtcNow,
            null);
    }

    private static ProviderPublicationSessionData OpenProviderSession(
        Guid sessionId,
        string sessionState = ProviderPublicationSessionStates.Open,
        DateTime? closedUtc = null)
    {
        return new ProviderPublicationSessionData(
            sessionId,
            Guid.NewGuid(),
            sessionState,
            null,
            DateTime.UtcNow,
            closedUtc);
    }

    private sealed class FakeSubscriptionSessionRepository : ISubscriptionSessionRepository
    {
        public int GetCalls { get; private set; }
        public GetSubscriptionSessionResult SessionResult { get; init; } =
            GetSubscriptionSessionResult.Failure(GetSubscriptionSessionFailureKind.NotFound, "missing");

        public Task<InsertSubscriptionSessionResult> InsertConsumerSubscriptionSessionAsync(
            ConsumerSubscriptionSessionCreateData session,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<GetSubscriptionSessionResult> GetSubscriptionSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            GetCalls++;
            return Task.FromResult(SessionResult);
        }

        public Task<CloseSubscriptionSessionResult> CloseSubscriptionSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeProviderPublicationSessionRepository : IProviderPublicationSessionRepository
    {
        public int GetCalls { get; private set; }
        public GetProviderPublicationSessionResult SessionResult { get; init; } =
            GetProviderPublicationSessionResult.Failure(GetProviderPublicationSessionFailureKind.NotFound, "missing");

        public Task<InsertProviderPublicationSessionResult> InsertProviderPublicationSessionAsync(
            ProviderPublicationSessionCreateData session,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<GetProviderPublicationSessionResult> GetProviderPublicationSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            GetCalls++;
            return Task.FromResult(SessionResult);
        }

        public Task<CloseProviderPublicationSessionResult> CloseProviderPublicationSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeConsumerSubscriptionSessionService : IConsumerSubscriptionSessionService
    {
        public string? ClosedSessionId { get; private set; }
        public CloseConsumerSubscriptionSessionResult CloseResult { get; init; } = CloseConsumerSubscriptionSessionResult.Success();

        public Task<OpenSubscriptionSessionResult> OpenSubscriptionSessionAsync(
            string channelId,
            IReadOnlyList<string> topics,
            string? listenerUrl,
            string? filterExpressionsJson,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CloseConsumerSubscriptionSessionResult> CloseSubscriptionSessionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            ClosedSessionId = sessionId;
            return Task.FromResult(CloseResult);
        }

        public Task<ReadPublicationResult> ReadPublicationAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<RemovePublicationResult> RemovePublicationAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeProviderPublicationSessionService : IProviderPublicationSessionService
    {
        public string? ClosedSessionId { get; private set; }
        public ClosePublicationSessionResult CloseResult { get; init; } = ClosePublicationSessionResult.Success();

        public Task<OpenPublicationSessionResult> OpenPublicationSessionAsync(
            string channelId,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ClosePublicationSessionResult> ClosePublicationSessionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            ClosedSessionId = sessionId;
            return Task.FromResult(CloseResult);
        }

        public Task<PostPublicationResult> PostPublicationAsync(
            string sessionId,
            IReadOnlyList<string?>? topics,
            InternalMessageContent messageContent,
            string? expiry,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
