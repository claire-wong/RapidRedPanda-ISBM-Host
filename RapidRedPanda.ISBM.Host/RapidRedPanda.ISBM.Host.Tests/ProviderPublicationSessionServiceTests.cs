using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Services;
using System.Text.Json;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class ProviderPublicationSessionServiceTests
{
    private const string ChannelUri = "/publication/channel";
    private const string EncodedChannelUri = "%2Fpublication%2Fchannel";

    [Fact]
    public async Task OpenPublicationSession_ValidPublicationChannel_PersistsSessionForResolvedChannel()
    {
        Guid channelId = Guid.NewGuid();
        FakeChannelRepository channelRepository = ValidChannelRepository(channelId);
        FakeProviderPublicationSessionRepository sessionRepository = new();

        OpenPublicationSessionResult result = await CreateService(channelRepository, sessionRepository)
            .OpenPublicationSessionAsync(EncodedChannelUri, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Guid sessionId = Assert.IsType<Guid>(result.SessionId);
        Assert.NotEqual(Guid.Empty, sessionId);
        Assert.Equal(ChannelUri, channelRepository.ProviderChannelUri);
        ProviderPublicationSessionCreateData inserted = Assert.Single(sessionRepository.InsertedSessions);
        Assert.Equal(sessionId, inserted.SessionId);
        Assert.Equal(channelId, inserted.ChannelId);
    }

    [Fact]
    public async Task OpenPublicationSession_ChannelIdWithEncodedSpaces_DecodesOnceAndPersists()
    {
        const string channelUri = "/Plant A/publication channel";
        FakeChannelRepository channelRepository = ValidChannelRepository(Guid.NewGuid());

        OpenPublicationSessionResult result = await CreateService(channelRepository)
            .OpenPublicationSessionAsync("%2FPlant%20A%2Fpublication%20channel", null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(channelUri, channelRepository.ProviderChannelUri);
    }

    [Fact]
    public async Task OpenPublicationSession_InvalidChannelId_ReturnsParameterFaultWithoutRepositoryCalls()
    {
        FakeChannelRepository channelRepository = new();
        FakeProviderPublicationSessionRepository sessionRepository = new();

        OpenPublicationSessionResult result = await CreateService(channelRepository, sessionRepository)
            .OpenPublicationSessionAsync("%20%2Fbad", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OpenPublicationSessionFailureKind.ParameterFault, result.FailureKind);
        Assert.Null(channelRepository.ProviderChannelUri);
        Assert.Empty(sessionRepository.InsertedSessions);
    }

    [Fact]
    public async Task OpenPublicationSession_MissingChannel_ReturnsChannelFault()
    {
        FakeChannelRepository channelRepository = new()
        {
            ProviderChannelResult = GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.NotFound, "missing")
        };

        OpenPublicationSessionResult result = await CreateService(channelRepository)
            .OpenPublicationSessionAsync(EncodedChannelUri, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OpenPublicationSessionFailureKind.ChannelFault, result.FailureKind);
    }

    [Fact]
    public async Task OpenPublicationSession_InvalidSecurityCredentials_ReturnsSecurityTokenFault()
    {
        FakeChannelRepository channelRepository = new()
        {
            ProviderChannelResult = GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.SecurityTokenInvalid, "invalid")
        };

        OpenPublicationSessionResult result = await CreateService(channelRepository)
            .OpenPublicationSessionAsync(EncodedChannelUri, new ChannelCredentials("USER", "secret"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OpenPublicationSessionFailureKind.SecurityTokenFault, result.FailureKind);
    }

    [Fact]
    public async Task OpenPublicationSession_RepositoryFailure_ReturnsOperationFault()
    {
        FakeProviderPublicationSessionRepository sessionRepository = new()
        {
            InsertResult = InsertProviderPublicationSessionResult.Failure(InsertProviderPublicationSessionFailureKind.DatabaseFailure, "failed")
        };

        OpenPublicationSessionResult result = await CreateService(ValidChannelRepository(Guid.NewGuid()), sessionRepository)
            .OpenPublicationSessionAsync(EncodedChannelUri, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OpenPublicationSessionFailureKind.OperationFault, result.FailureKind);
    }

    [Fact]
    public async Task ClosePublicationSession_OpenSession_ClosesOnlyRequestedProviderSession()
    {
        Guid sessionId = Guid.NewGuid();
        FakeProviderPublicationSessionRepository sessionRepository = new()
        {
            SessionResult = GetProviderPublicationSessionResult.Success(OpenSession(sessionId))
        };

        ClosePublicationSessionResult result = await CreateService(sessionRepository: sessionRepository)
            .ClosePublicationSessionAsync(sessionId.ToString(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(sessionId, Assert.Single(sessionRepository.ClosedSessionIds));
    }

    [Fact]
    public async Task ClosePublicationSession_AlreadyClosedSession_ReturnsSessionFault()
    {
        Guid sessionId = Guid.NewGuid();
        FakeProviderPublicationSessionRepository sessionRepository = new()
        {
            SessionResult = GetProviderPublicationSessionResult.Success(OpenSession(
                sessionId,
                ProviderPublicationSessionStates.Closed,
                DateTime.UtcNow))
        };

        ClosePublicationSessionResult result = await CreateService(sessionRepository: sessionRepository)
            .ClosePublicationSessionAsync(sessionId.ToString(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ClosePublicationSessionFailureKind.SessionFault, result.FailureKind);
        Assert.Empty(sessionRepository.ClosedSessionIds);
    }

    [Fact]
    public async Task ClosePublicationSession_InvalidSessionId_ReturnsParameterFault()
    {
        FakeProviderPublicationSessionRepository sessionRepository = new();

        ClosePublicationSessionResult result = await CreateService(sessionRepository: sessionRepository)
            .ClosePublicationSessionAsync("not-a-guid", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ClosePublicationSessionFailureKind.ParameterFault, result.FailureKind);
        Assert.Empty(sessionRepository.ClosedSessionIds);
    }

    [Fact]
    public async Task ClosePublicationSession_MissingSession_ReturnsSessionFault()
    {
        FakeProviderPublicationSessionRepository sessionRepository = new()
        {
            SessionResult = GetProviderPublicationSessionResult.Failure(GetProviderPublicationSessionFailureKind.NotFound, "missing")
        };

        ClosePublicationSessionResult result = await CreateService(sessionRepository: sessionRepository)
            .ClosePublicationSessionAsync(Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ClosePublicationSessionFailureKind.SessionFault, result.FailureKind);
    }

    [Fact]
    public async Task PostPublication_OpenProviderSession_SendsOneAzureMessageWithTopicsPayloadAndExpiry()
    {
        Guid sessionId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        FakeProviderPublicationSessionRepository sessionRepository = new()
        {
            SessionResult = GetProviderPublicationSessionResult.Success(OpenSession(sessionId, channelId: channelId))
        };
        FakeChannelRepository channelRepository = new()
        {
            TopicBindingByIdResult = GetChannelTopicBindingResult.Success(PublicationBinding(channelId, "isbm-topic"))
        };
        FakePublicationTransport transport = new();

        PostPublicationResult result = await CreateService(channelRepository, sessionRepository, transport)
            .PostPublicationAsync(
                sessionId.ToString(),
                ["Topic-A", "Topic-B", "Topic-A"],
                BinaryData.FromString("""{"content":{"reading":42}}"""),
                null,
                "P2D",
                CancellationToken.None);

        Assert.True(result.Succeeded);
        Guid publicationId = Assert.IsType<Guid>(result.PublicationId);
        SentPublication sent = Assert.Single(transport.SentPublications);
        Assert.Equal("isbm-topic", sent.TopicName);
        Assert.Equal(publicationId.ToString("D"), sent.Message.MessageId);
        Assert.Null(sent.Message.ContentType);
        Assert.Equal(TimeSpan.FromDays(2), sent.Message.TimeToLive);
        using (JsonDocument body = JsonDocument.Parse(sent.Message.Body))
        {
            Assert.Equal(publicationId.ToString("D"), body.RootElement.GetProperty("messageId").GetString());
            Assert.False(body.RootElement.GetProperty("messageContent").TryGetProperty("mediaType", out _));
            Assert.Equal(42, body.RootElement.GetProperty("messageContent").GetProperty("content").GetProperty("reading").GetInt32());
            Assert.Equal("Topic-A", body.RootElement.GetProperty("topics")[0].GetString());
            Assert.Equal("Topic-B", body.RootElement.GetProperty("topics")[1].GetString());
            Assert.False(body.RootElement.TryGetProperty("expiry", out _));
        }

        Assert.Equal(2, sent.Message.ApplicationProperties[PublicationRoutingProperties.TopicCount]);
        Assert.Equal("Topic-A", sent.Message.ApplicationProperties[PublicationRoutingProperties.TopicPropertyName(0)]);
        Assert.Equal("Topic-B", sent.Message.ApplicationProperties[PublicationRoutingProperties.TopicPropertyName(1)]);
        Assert.False(sent.Message.ApplicationProperties.ContainsKey(PublicationRoutingProperties.TopicPropertyName(2)));
        Assert.Empty(sessionRepository.InsertedSessions);
        Assert.Empty(sessionRepository.ClosedSessionIds);
    }

    [Theory]
    [InlineData("PT30M", 0, 30)]
    [InlineData("P1DT12H", 36, 0)]
    public async Task PostPublication_ValidIso8601ExpiryValues_SetMessageTimeToLive(
        string expiry,
        int expectedHours,
        int expectedMinutes)
    {
        Guid sessionId = Guid.NewGuid();
        FakePublicationTransport transport = new();

        PostPublicationResult result = await CreateServiceForPost(sessionId, transport: transport)
            .PostPublicationAsync(
                sessionId.ToString(),
                ["Topic-A"],
                BinaryData.FromString("""{"content":{"reading":42}}"""),
                null,
                expiry,
                CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new TimeSpan(expectedHours, expectedMinutes, 0), Assert.Single(transport.SentPublications).Message.TimeToLive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-duration")]
    [InlineData("PT0S")]
    [InlineData("-PT1S")]
    public async Task PostPublication_InvalidExpiry_ReturnsParameterFaultWithoutSending(string? expiry)
    {
        Guid sessionId = Guid.NewGuid();
        FakePublicationTransport transport = new();

        PostPublicationResult result = await CreateServiceForPost(sessionId, transport: transport)
            .PostPublicationAsync(
                sessionId.ToString(),
                ["Topic-A"],
                BinaryData.FromString("""{"content":{"reading":42}}"""),
                null,
                expiry,
                CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(PostPublicationFailureKind.ParameterFault, result.FailureKind);
        Assert.Empty(transport.SentPublications);
    }

    [Fact]
    public async Task PostPublication_OmittedExpiry_SucceedsWithoutAssigningHostTimeToLive()
    {
        Guid sessionId = Guid.NewGuid();
        FakePublicationTransport transport = new();

        PostPublicationResult result = await CreateServiceForPost(sessionId, transport: transport)
            .PostPublicationAsync(
                sessionId.ToString(),
                ["Topic-A"],
                BinaryData.FromString("""{"content":{"reading":42}}"""),
                null,
                null,
                CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new ServiceBusMessage().TimeToLive, Assert.Single(transport.SentPublications).Message.TimeToLive);
    }

    [Fact]
    public async Task PostPublication_ServiceBusBody_IsCompletePublicationEnvelopeWithoutExpiry()
    {
        Guid sessionId = Guid.NewGuid();
        FakePublicationTransport transport = new();
        const string messageContent = """
            {
              "content": {
                "$schema": "http://www.mimosa.org/ccom4/BOD/SyncMeasurements.json",
                "syncMeasurements": {
                  "releaseID": "9.0",
                  "versionID": "1.0"
                }
              }
            }
            """;

        PostPublicationResult result = await CreateServiceForPost(sessionId, transport: transport)
            .PostPublicationAsync(
                sessionId.ToString(),
                ["Topic-A"],
                BinaryData.FromString(messageContent),
                null,
                null,
                CancellationToken.None);

        Assert.True(result.Succeeded);
        ServiceBusMessage message = Assert.Single(transport.SentPublications).Message;
        using JsonDocument body = JsonDocument.Parse(message.Body);
        Assert.Equal(result.PublicationId!.Value.ToString("D"), body.RootElement.GetProperty("messageId").GetString());
        Assert.True(body.RootElement.TryGetProperty("messageContent", out JsonElement messageContentElement));
        Assert.False(messageContentElement.TryGetProperty("mediaType", out _));
        Assert.True(messageContentElement.TryGetProperty("content", out JsonElement content));
        Assert.True(content.TryGetProperty("$schema", out _));
        Assert.True(content.TryGetProperty("syncMeasurements", out _));
        Assert.True(body.RootElement.TryGetProperty("topics", out JsonElement bodyTopics));
        Assert.Equal("Topic-A", bodyTopics[0].GetString());
        Assert.False(body.RootElement.TryGetProperty("expiry", out _));
        Assert.False(messageContentElement.TryGetProperty("messageContent", out _));
        Assert.Null(message.ContentType);
        Assert.Equal(result.PublicationId!.Value.ToString("D"), message.MessageId);
        Assert.Equal(result.PublicationId!.Value.ToString("D"), message.ApplicationProperties[PublicationRoutingProperties.PublicationId]);
        Assert.Equal(1, message.ApplicationProperties[PublicationRoutingProperties.TopicCount]);
        Assert.Equal("Topic-A", message.ApplicationProperties[PublicationRoutingProperties.TopicPropertyName(0)]);
    }

    [Fact]
    public async Task PostPublication_InvalidSessionId_ReturnsParameterFaultWithoutRepositoryCalls()
    {
        FakeProviderPublicationSessionRepository sessionRepository = new();

        PostPublicationResult result = await CreateService(sessionRepository: sessionRepository)
            .PostPublicationAsync("not-a-guid", ["Topic-A"], BinaryData.FromString("{}"), "application/json", "PT1H", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(PostPublicationFailureKind.ParameterFault, result.FailureKind);
        Assert.Equal(0, sessionRepository.GetCalls);
    }

    [Fact]
    public async Task PostPublication_MissingOrConsumerSession_ReturnsSessionFault()
    {
        FakeProviderPublicationSessionRepository sessionRepository = new()
        {
            SessionResult = GetProviderPublicationSessionResult.Failure(GetProviderPublicationSessionFailureKind.NotFound, "missing")
        };

        PostPublicationResult result = await CreateService(sessionRepository: sessionRepository)
            .PostPublicationAsync(Guid.NewGuid().ToString(), ["Topic-A"], BinaryData.FromString("{}"), "application/json", "PT1H", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(PostPublicationFailureKind.SessionFault, result.FailureKind);
    }

    [Fact]
    public async Task PostPublication_ClosedProviderSession_ReturnsSessionFaultWithoutSending()
    {
        Guid sessionId = Guid.NewGuid();
        FakePublicationTransport transport = new();
        FakeProviderPublicationSessionRepository sessionRepository = new()
        {
            SessionResult = GetProviderPublicationSessionResult.Success(OpenSession(
                sessionId,
                ProviderPublicationSessionStates.Closed,
                DateTime.UtcNow))
        };

        PostPublicationResult result = await CreateService(sessionRepository: sessionRepository, publicationTransport: transport)
            .PostPublicationAsync(sessionId.ToString(), ["Topic-A"], BinaryData.FromString("{}"), "application/json", "PT1H", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(PostPublicationFailureKind.SessionFault, result.FailureKind);
        Assert.Empty(transport.SentPublications);
    }

    [Theory]
    [MemberData(nameof(InvalidTopicSets))]
    public async Task PostPublication_InvalidTopics_ReturnsParameterFaultWithoutSending(IReadOnlyList<string?>? topics)
    {
        Guid sessionId = Guid.NewGuid();
        FakePublicationTransport transport = new();

        PostPublicationResult result = await CreateServiceForPost(sessionId, transport: transport)
            .PostPublicationAsync(sessionId.ToString(), topics, BinaryData.FromString("{}"), "application/json", "PT1H", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(PostPublicationFailureKind.ParameterFault, result.FailureKind);
        Assert.Empty(transport.SentPublications);
    }

    [Fact]
    public async Task PostPublication_ChannelMissingOrNotPublication_ReturnsChannelFaultWithoutSending()
    {
        Guid sessionId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        FakePublicationTransport transport = new();
        FakeChannelRepository channelRepository = new()
        {
            TopicBindingByIdResult = GetChannelTopicBindingResult.Success(PublicationBinding(channelId, "isbm-topic", channelType: "Request"))
        };
        FakeProviderPublicationSessionRepository sessionRepository = new()
        {
            SessionResult = GetProviderPublicationSessionResult.Success(OpenSession(sessionId, channelId: channelId))
        };

        PostPublicationResult result = await CreateService(channelRepository, sessionRepository, transport)
            .PostPublicationAsync(sessionId.ToString(), ["Topic-A"], BinaryData.FromString("{}"), "application/json", "PT1H", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(PostPublicationFailureKind.ChannelFault, result.FailureKind);
        Assert.Empty(transport.SentPublications);
    }

    [Fact]
    public async Task PostPublication_TransportFailure_ReturnsOperationFault()
    {
        Guid sessionId = Guid.NewGuid();
        FakePublicationTransport transport = new()
        {
            ExceptionToThrow = new InvalidOperationException("send failed")
        };

        PostPublicationResult result = await CreateServiceForPost(sessionId, transport: transport)
            .PostPublicationAsync(sessionId.ToString(), ["Topic-A"], BinaryData.FromString("{}"), "application/json", "PT1H", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(PostPublicationFailureKind.OperationFault, result.FailureKind);
    }

    public static TheoryData<IReadOnlyList<string?>?> InvalidTopicSets()
    {
        TheoryData<IReadOnlyList<string?>?> data = new()
        {
            null,
            Array.Empty<string?>(),
            new string?[] { null },
            new[] { "" },
            new[] { "   " },
            Enumerable.Range(0, PublicationRoutingProperties.MaxPublicationTopics + 1)
                .Select(index => $"Topic-{index}")
                .Cast<string?>()
                .ToArray()
        };

        return data;
    }

    private static ProviderPublicationSessionService CreateService(
        FakeChannelRepository? channelRepository = null,
        FakeProviderPublicationSessionRepository? sessionRepository = null,
        FakePublicationTransport? publicationTransport = null)
    {
        return new ProviderPublicationSessionService(
            channelRepository ?? new FakeChannelRepository(),
            sessionRepository ?? new FakeProviderPublicationSessionRepository(),
            publicationTransport ?? new FakePublicationTransport(),
            NullLogger<ProviderPublicationSessionService>.Instance);
    }

    private static ProviderPublicationSessionService CreateServiceForPost(
        Guid sessionId,
        Guid? channelId = null,
        FakePublicationTransport? transport = null)
    {
        Guid resolvedChannelId = channelId ?? Guid.NewGuid();
        return CreateService(
            new FakeChannelRepository
            {
                TopicBindingByIdResult = GetChannelTopicBindingResult.Success(PublicationBinding(resolvedChannelId, "isbm-topic"))
            },
            new FakeProviderPublicationSessionRepository
            {
                SessionResult = GetProviderPublicationSessionResult.Success(OpenSession(sessionId, channelId: resolvedChannelId))
            },
            transport);
    }

    private static FakeChannelRepository ValidChannelRepository(Guid channelId)
    {
        return new FakeChannelRepository
        {
            ProviderChannelResult = GetChannelForSubscriptionResult.Success(new ChannelSubscriptionData(
                channelId,
                ChannelUri,
                "Publication",
                "isbm-ch-test",
                null))
        };
    }

    private static ChannelTopicBindingData PublicationBinding(
        Guid channelId,
        string azureTopicName,
        string channelType = "Publication")
    {
        return new ChannelTopicBindingData(channelId, ChannelUri, channelType, azureTopicName);
    }

    private static ProviderPublicationSessionData OpenSession(
        Guid sessionId,
        string sessionState = ProviderPublicationSessionStates.Open,
        DateTime? closedUtc = null,
        Guid? channelId = null)
    {
        return new ProviderPublicationSessionData(
            sessionId,
            channelId ?? Guid.NewGuid(),
            sessionState,
            null,
            DateTime.UtcNow,
            closedUtc);
    }

    private sealed class FakeChannelRepository : IChannelRepository
    {
        public string? ProviderChannelUri { get; private set; }
        public Guid? TopicBindingChannelId { get; private set; }
        public GetChannelForSubscriptionResult ProviderChannelResult { get; init; } =
            GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.NotFound, "missing");
        public GetChannelTopicBindingResult TopicBindingByIdResult { get; init; } =
            GetChannelTopicBindingResult.Failure(GetChannelTopicBindingFailureKind.NotFound, "missing");

        public Task<IReadOnlyList<ChannelData>> GetChannelsAsync(ChannelCredentials? credentials, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ChannelData>>([]);
        }

        public Task<GetChannelResult> GetChannelAsync(string channelUri, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelResult.Failure(GetChannelFailureKind.NotFound, "missing"));
        }

        public Task<GetChannelTopicBindingResult> GetChannelTopicBindingAsync(string channelUri, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelTopicBindingResult.Failure(GetChannelTopicBindingFailureKind.NotFound, "missing"));
        }

        public Task<GetChannelTopicBindingResult> GetChannelTopicBindingByIdAsync(Guid channelId, CancellationToken cancellationToken)
        {
            TopicBindingChannelId = channelId;
            return Task.FromResult(TopicBindingByIdResult);
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForConsumerPublicationSubscriptionAsync(
            string channelUri,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForProviderPublicationSessionAsync(
            string channelUri,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            ProviderChannelUri = channelUri;
            return Task.FromResult(ProviderChannelResult);
        }

        public Task<CreateChannelResult> CreateChannelAsync(ChannelCreateRequestData request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<DeleteChannelResult> DeleteChannelAsync(string channelUri, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<SecurityTokenMutationResult> AddSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<SecurityTokenMutationResult> RemoveSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeProviderPublicationSessionRepository : IProviderPublicationSessionRepository
    {
        public List<ProviderPublicationSessionCreateData> InsertedSessions { get; } = [];
        public List<Guid> ClosedSessionIds { get; } = [];
        public int GetCalls { get; private set; }
        public InsertProviderPublicationSessionResult InsertResult { get; init; } = InsertProviderPublicationSessionResult.Success();
        public GetProviderPublicationSessionResult SessionResult { get; init; } =
            GetProviderPublicationSessionResult.Failure(GetProviderPublicationSessionFailureKind.NotFound, "missing");
        public CloseProviderPublicationSessionResult CloseResult { get; init; } = CloseProviderPublicationSessionResult.Success();

        public Task<InsertProviderPublicationSessionResult> InsertProviderPublicationSessionAsync(
            ProviderPublicationSessionCreateData session,
            CancellationToken cancellationToken)
        {
            InsertedSessions.Add(session);
            return Task.FromResult(InsertResult);
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
            ClosedSessionIds.Add(sessionId);
            return Task.FromResult(CloseResult);
        }
    }

    private sealed class FakePublicationTransport : IPublicationTransport
    {
        public List<SentPublication> SentPublications { get; } = [];
        public Exception? ExceptionToThrow { get; init; }

        public Task SendPublicationAsync(string topicName, ServiceBusMessage message, CancellationToken cancellationToken)
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            SentPublications.Add(new SentPublication(topicName, message));
            return Task.CompletedTask;
        }
    }

    private sealed record SentPublication(string TopicName, ServiceBusMessage Message);
}
