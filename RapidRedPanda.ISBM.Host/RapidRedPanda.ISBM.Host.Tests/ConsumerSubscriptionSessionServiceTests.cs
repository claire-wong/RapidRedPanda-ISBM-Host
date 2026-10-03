using Azure;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Services;
using System.Text.Json;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class ConsumerSubscriptionSessionServiceTests
{
    private const string ChannelUri = "/publication/channel";
    private const string EncodedChannelUri = "%2Fpublication%2Fchannel";
    private static readonly IReadOnlyList<string> ConsumerTopics = ["Topic-A"];

    [Fact]
    public async Task OpenSubscriptionSession_ValidPublicationChannel_CreatesAzureSubscriptionAndSqlSession()
    {
        Guid channelId = Guid.NewGuid();
        FakeChannelRepository channelRepository = new()
        {
            ChannelResult = GetChannelForSubscriptionResult.Success(new ChannelSubscriptionData(
                channelId,
                ChannelUri,
                "Publication",
                ChannelTransportNames.TopicNameForChannel(channelId),
                null))
        };
        FakeSubscriptionTransport transport = new();
        FakeSessionRepository sessionRepository = new();
        ConsumerSubscriptionSessionService service = CreateService(channelRepository, sessionRepository, transport);

        OpenSubscriptionSessionResult result = await service.OpenSubscriptionSessionAsync(EncodedChannelUri, ConsumerTopics, null, null, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Guid sessionId = Assert.IsType<Guid>(result.SessionId);
        Assert.NotEqual(Guid.Empty, sessionId);
        Assert.Equal(ChannelUri, channelRepository.ChannelUri);
        Assert.Single(transport.CreatedSubscriptions);
        Assert.Equal(ChannelTransportNames.TopicNameForChannel(channelId), transport.CreatedSubscriptions[0].TopicName);
        Assert.Equal($"isbm-css-{sessionId:N}", transport.CreatedSubscriptions[0].SubscriptionName);
        Assert.Equal(ConsumerTopics, transport.CreatedSubscriptions[0].ConsumerTopics);
        ConsumerSubscriptionSessionCreateData inserted = Assert.Single(sessionRepository.InsertedSessions);
        Assert.Equal(sessionId, inserted.SessionId);
        Assert.Equal(channelId, inserted.ChannelId);
        Assert.Equal($"isbm-css-{sessionId:N}", inserted.AzureSubscriptionName);
        Assert.Equal(ConsumerTopics, inserted.RequestedTopics);
        Assert.Null(inserted.ListenerUrl);
    }

    [Fact]
    public async Task OpenSubscriptionSession_ThirtyThreeShortTopicsUnderFilterLimit_Succeeds()
    {
        Guid channelId = Guid.NewGuid();
        IReadOnlyList<string> topics = Enumerable.Range(33, 33)
            .Select(value => ((char)value).ToString())
            .ToArray();
        string filter = TopicSelectionRules.BuildAzureSqlFilter(topics);
        FakeSubscriptionTransport transport = new();
        FakeSessionRepository sessionRepository = new();

        OpenSubscriptionSessionResult result = await CreateService(
                ValidChannelRepository(channelId),
                sessionRepository,
                transport)
            .OpenSubscriptionSessionAsync(EncodedChannelUri, topics, null, null, null, CancellationToken.None);

        Assert.Equal(1019, filter.Length);
        Assert.True(filter.Length < PublicationRoutingProperties.MaxSqlFilterLength);
        Assert.True(result.Succeeded);
        Assert.Equal(topics, Assert.Single(transport.CreatedSubscriptions).ConsumerTopics);
        Assert.Equal(topics, Assert.Single(sessionRepository.InsertedSessions).RequestedTopics);
    }

    [Fact]
    public async Task OpenSubscriptionSession_FilterOverLimit_ReturnsParameterFaultBeforeSideEffects()
    {
        IReadOnlyList<string> topics = Enumerable.Range(33, 34)
            .Select(value => ((char)value).ToString())
            .ToArray();
        FakeChannelRepository channelRepository = ValidChannelRepository(Guid.NewGuid());
        FakeSubscriptionTransport transport = new();
        FakeSessionRepository sessionRepository = new();

        OpenSubscriptionSessionResult result = await CreateService(
                channelRepository,
                sessionRepository,
                transport)
            .OpenSubscriptionSessionAsync(EncodedChannelUri, topics, null, null, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OpenSubscriptionSessionFailureKind.ParameterFault, result.FailureKind);
        Assert.Null(channelRepository.ChannelUri);
        Assert.Empty(transport.CreatedSubscriptions);
        Assert.Empty(sessionRepository.InsertedSessions);
    }

    [Theory]
    [InlineData("http://localhost:5080/isbm")]
    [InlineData("https://listener.example.com/isbm?tenant=a")]
    public async Task OpenSubscriptionSession_ValidListenerUrl_PersistsNormalizedUrl(string listenerUrl)
    {
        Guid channelId = Guid.NewGuid();
        FakeSessionRepository sessionRepository = new();

        OpenSubscriptionSessionResult result = await CreateService(
                ValidChannelRepository(channelId),
                sessionRepository)
            .OpenSubscriptionSessionAsync(EncodedChannelUri, ConsumerTopics, listenerUrl, null, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        ConsumerSubscriptionSessionCreateData inserted = Assert.Single(sessionRepository.InsertedSessions);
        Assert.Equal(new Uri(listenerUrl).AbsoluteUri, inserted.ListenerUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/relative/listener")]
    [InlineData("ftp://listener.example.com/isbm")]
    [InlineData("https://user:pass@listener.example.com/isbm")]
    [InlineData("https://listener.example.com/isbm#fragment")]
    public async Task OpenSubscriptionSession_InvalidListenerUrl_ReturnsParameterFaultBeforeSideEffects(string listenerUrl)
    {
        FakeChannelRepository channelRepository = ValidChannelRepository(Guid.NewGuid());
        FakeSessionRepository sessionRepository = new();
        FakeSubscriptionTransport transport = new();
        FakeChannelTopicTransport topicTransport = new();

        OpenSubscriptionSessionResult result = await CreateService(
                channelRepository,
                sessionRepository,
                transport,
                topicTransport)
            .OpenSubscriptionSessionAsync(EncodedChannelUri, ConsumerTopics, listenerUrl, null, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OpenSubscriptionSessionFailureKind.ParameterFault, result.FailureKind);
        Assert.Null(channelRepository.ChannelUri);
        Assert.Empty(topicTransport.CreatedTopics);
        Assert.Empty(transport.CreatedSubscriptions);
        Assert.Empty(sessionRepository.InsertedSessions);
    }

    [Fact]
    public async Task OpenSubscriptionSession_TooLongListenerUrl_ReturnsParameterFaultBeforeSideEffects()
    {
        FakeChannelRepository channelRepository = ValidChannelRepository(Guid.NewGuid());
        FakeSessionRepository sessionRepository = new();
        FakeSubscriptionTransport transport = new();
        string listenerUrl = $"https://listener.example.com/{new string('a', 2048)}";

        OpenSubscriptionSessionResult result = await CreateService(
                channelRepository,
                sessionRepository,
                transport)
            .OpenSubscriptionSessionAsync(EncodedChannelUri, ConsumerTopics, listenerUrl, null, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OpenSubscriptionSessionFailureKind.ParameterFault, result.FailureKind);
        Assert.Null(channelRepository.ChannelUri);
        Assert.Empty(transport.CreatedSubscriptions);
        Assert.Empty(sessionRepository.InsertedSessions);
    }

    [Fact]
    public async Task OpenSubscriptionSession_MultipleCalls_CreateDistinctAzureSubscriptions()
    {
        Guid channelId = Guid.NewGuid();
        FakeChannelRepository channelRepository = new()
        {
            ChannelResult = GetChannelForSubscriptionResult.Success(new ChannelSubscriptionData(
                channelId,
                ChannelUri,
                "Publication",
                ChannelTransportNames.TopicNameForChannel(channelId),
                null))
        };
        FakeSubscriptionTransport transport = new();
        FakeSessionRepository sessionRepository = new();
        ConsumerSubscriptionSessionService service = CreateService(channelRepository, sessionRepository, transport);

        OpenSubscriptionSessionResult first = await service.OpenSubscriptionSessionAsync(EncodedChannelUri, ConsumerTopics, null, null, null, CancellationToken.None);
        OpenSubscriptionSessionResult second = await service.OpenSubscriptionSessionAsync(EncodedChannelUri, ConsumerTopics, null, null, null, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.Equal(2, transport.CreatedSubscriptions.Select(item => item.SubscriptionName).Distinct().Count());
    }

    [Fact]
    public async Task OpenSubscriptionSession_MissingChannel_ReturnsChannelFault()
    {
        FakeChannelRepository channelRepository = new()
        {
            ChannelResult = GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.NotFound, "missing")
        };

        OpenSubscriptionSessionResult result = await CreateService(channelRepository).OpenSubscriptionSessionAsync(EncodedChannelUri, ConsumerTopics, null, null, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OpenSubscriptionSessionFailureKind.ChannelFault, result.FailureKind);
    }

    [Fact]
    public async Task OpenSubscriptionSession_InvalidChannelId_ReturnsParameterFault()
    {
        FakeChannelRepository channelRepository = new();

        OpenSubscriptionSessionResult result = await CreateService(channelRepository).OpenSubscriptionSessionAsync("%20%2Fbad", ConsumerTopics, null, null, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OpenSubscriptionSessionFailureKind.ParameterFault, result.FailureKind);
        Assert.Null(channelRepository.ChannelUri);
    }

    [Fact]
    public async Task OpenSubscriptionSession_InvalidSecurityToken_ReturnsSecurityTokenFault()
    {
        FakeChannelRepository channelRepository = new()
        {
            ChannelResult = GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.SecurityTokenInvalid, "invalid")
        };

        OpenSubscriptionSessionResult result = await CreateService(channelRepository).OpenSubscriptionSessionAsync(
            EncodedChannelUri,
            ConsumerTopics,
            null,
            null,
            new ChannelCredentials("TESTER", "secret"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OpenSubscriptionSessionFailureKind.SecurityTokenFault, result.FailureKind);
    }

    [Fact]
    public async Task OpenSubscriptionSession_MissingAzureTopic_ReturnsOperationFaultAndDoesNotInsertSqlSession()
    {
        Guid channelId = Guid.NewGuid();
        FakeChannelTopicTransport topicTransport = new() { TopicExists = false, CreateException = new RequestFailedException(503, "unavailable") };
        FakeSubscriptionTransport transport = new();
        FakeSessionRepository sessionRepository = new();

        OpenSubscriptionSessionResult result = await CreateService(
                ValidChannelRepository(channelId),
                sessionRepository,
                transport,
                topicTransport)
            .OpenSubscriptionSessionAsync(EncodedChannelUri, ConsumerTopics, null, null, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OpenSubscriptionSessionFailureKind.OperationFault, result.FailureKind);
        Assert.Empty(transport.CreatedSubscriptions);
        Assert.Empty(sessionRepository.InsertedSessions);
    }

    [Fact]
    public async Task OpenSubscriptionSession_MissingStoredTopic_IsRecreatedBeforeSubscriptionCreation()
    {
        Guid channelId = Guid.NewGuid();
        FakeChannelTopicTransport topicTransport = new() { TopicExists = false };
        FakeSubscriptionTransport subscriptionTransport = new();
        FakeSessionRepository sessionRepository = new();

        OpenSubscriptionSessionResult result = await CreateService(
                ValidChannelRepository(channelId),
                sessionRepository,
                subscriptionTransport,
                topicTransport)
            .OpenSubscriptionSessionAsync(EncodedChannelUri, ConsumerTopics, null, null, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ChannelTransportNames.TopicNameForChannel(channelId), Assert.Single(topicTransport.CreatedTopics));
        Assert.Single(subscriptionTransport.CreatedSubscriptions);
        Assert.Single(sessionRepository.InsertedSessions);
    }

    [Fact]
    public async Task OpenSubscriptionSession_AzureCreationFailure_DoesNotInsertSqlSession()
    {
        Guid channelId = Guid.NewGuid();
        FakeSubscriptionTransport transport = new() { CreateException = new RequestFailedException(503, "unavailable") };
        FakeSessionRepository sessionRepository = new();

        OpenSubscriptionSessionResult result = await CreateService(
                ValidChannelRepository(channelId),
                sessionRepository,
                transport)
            .OpenSubscriptionSessionAsync(EncodedChannelUri, ConsumerTopics, null, null, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OpenSubscriptionSessionFailureKind.OperationFault, result.FailureKind);
        Assert.Empty(sessionRepository.InsertedSessions);
    }

    [Fact]
    public async Task OpenSubscriptionSession_SqlInsertFailure_DeletesCreatedAzureSubscription()
    {
        Guid channelId = Guid.NewGuid();
        FakeSubscriptionTransport transport = new();
        FakeSessionRepository sessionRepository = new()
        {
            InsertResult = InsertSubscriptionSessionResult.Failure(InsertSubscriptionSessionFailureKind.DatabaseFailure, "failed")
        };

        OpenSubscriptionSessionResult result = await CreateService(
                ValidChannelRepository(channelId),
                sessionRepository,
                transport)
            .OpenSubscriptionSessionAsync(EncodedChannelUri, ConsumerTopics, null, null, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OpenSubscriptionSessionFailureKind.OperationFault, result.FailureKind);
        Assert.Single(transport.CreatedSubscriptions);
        Assert.Single(transport.DeletedSubscriptions);
        Assert.Equal(transport.CreatedSubscriptions[0].TopicName, transport.DeletedSubscriptions[0].TopicName);
        Assert.Equal(transport.CreatedSubscriptions[0].SubscriptionName, transport.DeletedSubscriptions[0].SubscriptionName);
    }

    [Fact]
    public async Task CloseSubscriptionSession_OpenConsumerSession_DeletesAzureSubscriptionAndClosesSqlSession()
    {
        Guid sessionId = Guid.NewGuid();
        FakeSubscriptionTransport transport = new();
        FakeSessionRepository sessionRepository = new()
        {
            SessionResult = GetSubscriptionSessionResult.Success(OpenSession(sessionId))
        };

        CloseConsumerSubscriptionSessionResult result = await CreateService(sessionRepository: sessionRepository, transport: transport)
            .CloseSubscriptionSessionAsync(sessionId.ToString(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(transport.DeletedSubscriptions);
        Assert.Equal(sessionId, Assert.Single(sessionRepository.ClosedSessionIds));
    }

    [Fact]
    public async Task CloseSubscriptionSession_AlreadyClosedSession_SucceedsWithoutDeletingAzureSubscriptionAgain()
    {
        Guid sessionId = Guid.NewGuid();
        FakeSubscriptionTransport transport = new();
        FakeSessionRepository sessionRepository = new()
        {
            SessionResult = GetSubscriptionSessionResult.Success(OpenSession(
                sessionId,
                sessionState: SubscriptionSessionStates.Closed,
                closedUtc: DateTime.UtcNow))
        };

        CloseConsumerSubscriptionSessionResult result = await CreateService(sessionRepository: sessionRepository, transport: transport)
            .CloseSubscriptionSessionAsync(sessionId.ToString(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(transport.DeletedSubscriptions);
        Assert.Equal(sessionId, Assert.Single(sessionRepository.ClosedSessionIds));
    }

    [Fact]
    public async Task CloseSubscriptionSession_InvalidSessionId_ReturnsParameterFault()
    {
        CloseConsumerSubscriptionSessionResult result = await CreateService()
            .CloseSubscriptionSessionAsync("not-a-guid", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(Services.CloseSubscriptionSessionFailureKind.ParameterFault, result.FailureKind);
    }

    [Fact]
    public async Task CloseSubscriptionSession_MissingSession_ReturnsSessionFault()
    {
        FakeSessionRepository sessionRepository = new()
        {
            SessionResult = GetSubscriptionSessionResult.Failure(GetSubscriptionSessionFailureKind.NotFound, "missing")
        };

        CloseConsumerSubscriptionSessionResult result = await CreateService(sessionRepository: sessionRepository)
            .CloseSubscriptionSessionAsync(Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(Services.CloseSubscriptionSessionFailureKind.SessionFault, result.FailureKind);
    }

    [Fact]
    public async Task CloseSubscriptionSession_WrongSessionType_ReturnsSessionFault()
    {
        Guid sessionId = Guid.NewGuid();
        FakeSessionRepository sessionRepository = new()
        {
            SessionResult = GetSubscriptionSessionResult.Success(OpenSession(sessionId, sessionType: "ProviderPublication"))
        };

        CloseConsumerSubscriptionSessionResult result = await CreateService(sessionRepository: sessionRepository)
            .CloseSubscriptionSessionAsync(sessionId.ToString(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(Services.CloseSubscriptionSessionFailureKind.SessionFault, result.FailureKind);
    }

    [Fact]
    public async Task CloseSubscriptionSession_AzureDeletionFailure_DoesNotReportSuccess()
    {
        Guid sessionId = Guid.NewGuid();
        FakeSubscriptionTransport transport = new() { DeleteException = new RequestFailedException(503, "unavailable") };
        FakeSessionRepository sessionRepository = new()
        {
            SessionResult = GetSubscriptionSessionResult.Success(OpenSession(sessionId))
        };

        CloseConsumerSubscriptionSessionResult result = await CreateService(sessionRepository: sessionRepository, transport: transport)
            .CloseSubscriptionSessionAsync(sessionId.ToString(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(Services.CloseSubscriptionSessionFailureKind.OperationFault, result.FailureKind);
        Assert.Empty(sessionRepository.ClosedSessionIds);
    }

    [Fact]
    public async Task ReadPublication_OpenConsumerSession_ReadsFromStoredAzureSubscription()
    {
        Guid sessionId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();
        TransportPublication publication = new(
            messageId,
            ["Topic-A", "Topic-B"],
            "application/json",
            BinaryData.FromString($$"""
                {
                  "messageId": "{{messageId:D}}",
                  "messageContent": {
                    "content": {
                      "reading": 42
                    }
                  },
                  "topics": ["Topic-A", "Topic-B"]
                }
                """));
        FakeSessionRepository sessionRepository = new()
        {
            SessionResult = GetSubscriptionSessionResult.Success(OpenSession(sessionId))
        };
        FakeSubscriptionTransport transport = new()
        {
            Publication = publication
        };
        FakeProviderPublicationRepository publicationRepository = new();
        publicationRepository.SeedPublication(new ProviderPublicationData(
            messageId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            ["Topic-A", "Topic-B"],
            SqlMessageContentAdapter.ToSql(InternalMessageContent.FromJson(BinaryData.FromString("""{"reading":42}""")))));

        ReadPublicationResult result = await CreateService(
                sessionRepository: sessionRepository,
                publicationRepository: publicationRepository,
                transport: transport)
            .ReadPublicationAsync(sessionId.ToString(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.HasPublication);
        Assert.NotNull(result.Publication);
        Assert.Equal(publication.PublicationId, result.Publication.MessageId);
        Assert.Equal(publication.Topics, result.Publication.Topics);
        Assert.Equal(InternalMessageContentKind.Json, result.Publication.MessageContent.Kind);
        Assert.Null(result.Publication.MessageContent.MediaType);
        using JsonDocument messageContent = JsonDocument.Parse(result.Publication.MessageContent.Payload);
        Assert.Equal(42, messageContent.RootElement.GetProperty("reading").GetInt32());
        (Guid ReadSessionId, string TopicName, string SubscriptionName) request = Assert.Single(transport.ReadRequests);
        Assert.Equal(sessionId, request.ReadSessionId);
        Assert.Equal("topic", request.TopicName);
        Assert.Equal($"isbm-css-{sessionId:N}", request.SubscriptionName);
    }

    [Fact]
    public async Task ReadPublication_NoPendingMessage_ReturnsSuccessfulNoPublication()
    {
        Guid sessionId = Guid.NewGuid();
        FakeSessionRepository sessionRepository = new()
        {
            SessionResult = GetSubscriptionSessionResult.Success(OpenSession(sessionId))
        };

        ReadPublicationResult result = await CreateService(sessionRepository: sessionRepository)
            .ReadPublicationAsync(sessionId.ToString(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.HasPublication);
        Assert.Null(result.Publication);
    }

    [Fact]
    public async Task ReadPublication_InvalidSessionId_ReturnsParameterFault()
    {
        ReadPublicationResult result = await CreateService()
            .ReadPublicationAsync("not-a-guid", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ReadPublicationFailureKind.ParameterFault, result.FailureKind);
    }

    [Fact]
    public async Task ReadPublication_MissingSession_ReturnsSessionFault()
    {
        FakeSessionRepository sessionRepository = new()
        {
            SessionResult = GetSubscriptionSessionResult.Failure(GetSubscriptionSessionFailureKind.NotFound, "missing")
        };

        ReadPublicationResult result = await CreateService(sessionRepository: sessionRepository)
            .ReadPublicationAsync(Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ReadPublicationFailureKind.SessionFault, result.FailureKind);
    }

    [Fact]
    public async Task ReadPublication_ClosedSession_ReturnsSessionFault()
    {
        Guid sessionId = Guid.NewGuid();
        FakeSessionRepository sessionRepository = new()
        {
            SessionResult = GetSubscriptionSessionResult.Success(OpenSession(
                sessionId,
                sessionState: SubscriptionSessionStates.Closed,
                closedUtc: DateTime.UtcNow))
        };

        ReadPublicationResult result = await CreateService(sessionRepository: sessionRepository)
            .ReadPublicationAsync(sessionId.ToString(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ReadPublicationFailureKind.SessionFault, result.FailureKind);
    }

    [Fact]
    public async Task RemovePublication_OpenConsumerSession_RemovesPendingAzurePublicationForSession()
    {
        Guid sessionId = Guid.NewGuid();
        FakeSessionRepository sessionRepository = new()
        {
            SessionResult = GetSubscriptionSessionResult.Success(OpenSession(sessionId))
        };
        FakeSubscriptionTransport transport = new();

        Guid messageId = Guid.NewGuid();
        FakeProviderPublicationRepository publicationRepository = new();
        publicationRepository.SeedOutstanding(sessionId, new ProviderPublicationData(
            messageId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            ["Topic-A"],
            SqlMessageContentAdapter.ToSql(InternalMessageContent.FromJson(BinaryData.FromString("""{"reading":42}""")))));
        transport.OutstandingPublication = new TransportPublication(
            messageId,
            ["Topic-A"],
            "application/json",
            BinaryData.FromString("""{"messageContent":{"content":{"reading":42}}}"""));

        RemovePublicationResult result = await CreateService(
                sessionRepository: sessionRepository,
                publicationRepository: publicationRepository,
                transport: transport)
            .RemovePublicationAsync(sessionId.ToString(), CancellationToken.None);

        Assert.True(result.Succeeded);
        (Guid RemovedSessionId, string TopicName, string SubscriptionName) request = Assert.Single(transport.RemoveRequests);
        Assert.Equal(sessionId, request.RemovedSessionId);
        Assert.Equal("topic", request.TopicName);
        Assert.Equal($"isbm-css-{sessionId:N}", request.SubscriptionName);
    }

    [Fact]
    public async Task RemovePublication_EmptyOpenConsumerSession_RemainsSuccessful()
    {
        Guid sessionId = Guid.NewGuid();
        FakeSessionRepository sessionRepository = new()
        {
            SessionResult = GetSubscriptionSessionResult.Success(OpenSession(sessionId))
        };

        RemovePublicationResult first = await CreateService(sessionRepository: sessionRepository)
            .RemovePublicationAsync(sessionId.ToString(), CancellationToken.None);
        RemovePublicationResult second = await CreateService(sessionRepository: sessionRepository)
            .RemovePublicationAsync(sessionId.ToString(), CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
    }

    [Fact]
    public async Task RemovePublication_InvalidSessionId_ReturnsParameterFault()
    {
        RemovePublicationResult result = await CreateService()
            .RemovePublicationAsync("not-a-guid", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RemovePublicationFailureKind.ParameterFault, result.FailureKind);
    }

    [Fact]
    public async Task RemovePublication_MissingSession_ReturnsSessionFault()
    {
        FakeSessionRepository sessionRepository = new()
        {
            SessionResult = GetSubscriptionSessionResult.Failure(GetSubscriptionSessionFailureKind.NotFound, "missing")
        };

        RemovePublicationResult result = await CreateService(sessionRepository: sessionRepository)
            .RemovePublicationAsync(Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RemovePublicationFailureKind.SessionFault, result.FailureKind);
    }

    [Fact]
    public async Task SubscriptionSessionSource_DoesNotCreateProcessorOrLogSecrets()
    {
        string servicesRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "RapidRedPanda-ISBM-Host",
            "Services"));

        string source = string.Join(Environment.NewLine, await Task.WhenAll(
            Directory.GetFiles(servicesRoot, "*.cs").Select(path => File.ReadAllTextAsync(path))));

        Assert.DoesNotContain("ServiceBusProcessor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("credentials.Password", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SqlSubscriptionSessionRepositorySource_StoresListenerUrlWithNullableParameter()
    {
        string source = await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "RapidRedPanda-ISBM-Host",
            "Data",
            "SqlSubscriptionSessionRepository.cs")));

        Assert.Contains("ListenerUrl", source, StringComparison.Ordinal);
        Assert.Contains("@ListenerUrl", source, StringComparison.Ordinal);
        Assert.Contains("SqlDbType.NVarChar, 2048", source, StringComparison.Ordinal);
        Assert.Contains("(object?)session.ListenerUrl ?? DBNull.Value", source, StringComparison.Ordinal);
        Assert.DoesNotContain("'+ session.ListenerUrl", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SqlSubscriptionSessionRepositorySource_DoesNotSuppressRequestNotificationsOnConsumerPublicationClose()
    {
        string source = await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "RapidRedPanda-ISBM-Host",
            "Data",
            "SqlSubscriptionSessionRepository.cs")));

        Assert.Contains("await connection.BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("set SessionState = @ClosedState", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlRequestNotificationSuppression.SuppressForSessionAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PublicationNotificationSuppressionReasons.ProviderRequestSessionClosed", source, StringComparison.Ordinal);
        Assert.Contains("await transaction.CommitAsync", source, StringComparison.Ordinal);
        Assert.Contains("await RollbackQuietlyAsync(transaction", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AzureSubscriptionTransportRemovePublicationSource_ReceivesAndCompletesOneUnreadMessageWhenNoneIsOutstanding()
    {
        string source = await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "RapidRedPanda-ISBM-Host",
            "Services",
            "AzureServiceBusSubscriptionTransport.cs")));

        Assert.Contains("ReceiveMessageAsync(TimeSpan.FromSeconds(1), cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("CompleteMessageAsync(message, cancellationToken)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CompleteMessagesAsync", source, StringComparison.Ordinal);
    }

    private static ConsumerSubscriptionSessionService CreateService(
        FakeChannelRepository? channelRepository = null,
        FakeSessionRepository? sessionRepository = null,
        FakeSubscriptionTransport? transport = null,
        FakeChannelTopicTransport? topicTransport = null,
        FakeProviderPublicationRepository? publicationRepository = null)
    {
        return new ConsumerSubscriptionSessionService(
            channelRepository ?? new FakeChannelRepository(),
            sessionRepository ?? new FakeSessionRepository(),
            publicationRepository ?? new FakeProviderPublicationRepository(),
            transport ?? new FakeSubscriptionTransport(),
            topicTransport ?? new FakeChannelTopicTransport(),
            NullLogger<ConsumerSubscriptionSessionService>.Instance);
    }

    private static FakeChannelRepository ValidChannelRepository(Guid channelId)
    {
        return new FakeChannelRepository
        {
            ChannelResult = GetChannelForSubscriptionResult.Success(new ChannelSubscriptionData(
                channelId,
                ChannelUri,
                "Publication",
                ChannelTransportNames.TopicNameForChannel(channelId),
                null))
        };
    }

    private static ConsumerSubscriptionSessionData OpenSession(
        Guid sessionId,
        string sessionType = SubscriptionSessionTypes.ConsumerPublicationSubscription,
        string sessionState = SubscriptionSessionStates.Open,
        DateTime? closedUtc = null)
    {
        return new ConsumerSubscriptionSessionData(
            sessionId,
            Guid.NewGuid(),
            sessionType,
            sessionState,
            "topic",
            $"isbm-css-{sessionId:N}",
            ConsumerTopics,
            null,
            DateTime.UtcNow,
            closedUtc);
    }

    private sealed class FakeChannelRepository : IChannelRepository
    {
        public string? ChannelUri { get; private set; }
        public GetChannelForSubscriptionResult ChannelResult { get; init; } =
            GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.NotFound, "missing");

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
            return Task.FromResult(GetChannelTopicBindingResult.Failure(GetChannelTopicBindingFailureKind.NotFound, "missing"));
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForConsumerPublicationSubscriptionAsync(
            string channelUri,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            ChannelUri = channelUri;
            return Task.FromResult(ChannelResult);
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForProviderPublicationSessionAsync(
            string channelUri,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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

    private sealed class FakeSessionRepository : ISubscriptionSessionRepository
    {
        public List<ConsumerSubscriptionSessionCreateData> InsertedSessions { get; } = [];
        public List<Guid> ClosedSessionIds { get; } = [];
        public InsertSubscriptionSessionResult InsertResult { get; init; } = InsertSubscriptionSessionResult.Success();
        public GetSubscriptionSessionResult SessionResult { get; init; } =
            GetSubscriptionSessionResult.Failure(GetSubscriptionSessionFailureKind.NotFound, "missing");
        public CloseSubscriptionSessionResult CloseResult { get; init; } = CloseSubscriptionSessionResult.Success();

        public Task<InsertSubscriptionSessionResult> InsertConsumerSubscriptionSessionAsync(
            ConsumerSubscriptionSessionCreateData session,
            CancellationToken cancellationToken)
        {
            InsertedSessions.Add(session);
            return Task.FromResult(InsertResult);
        }

        public Task<GetSubscriptionSessionResult> GetSubscriptionSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(SessionResult);
        }

        public Task<CloseSubscriptionSessionResult> CloseSubscriptionSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            ClosedSessionIds.Add(sessionId);
            return Task.FromResult(CloseResult);
        }
    }

    private sealed class FakeSubscriptionTransport : ISubscriptionTransport
    {
        public bool TopicExists { get; init; } = true;
        public RequestFailedException? CreateException { get; init; }
        public RequestFailedException? DeleteException { get; init; }
        public TransportPublication? Publication { get; init; }
        public TransportPublication? OutstandingPublication { get; set; }
        public List<(string TopicName, string SubscriptionName, IReadOnlyList<string> ConsumerTopics)> CreatedSubscriptions { get; } = [];
        public List<(string TopicName, string SubscriptionName)> DeletedSubscriptions { get; } = [];
        public List<(Guid ReadSessionId, string TopicName, string SubscriptionName)> ReadRequests { get; } = [];
        public List<(Guid RemovedSessionId, string TopicName, string SubscriptionName)> RemoveRequests { get; } = [];

        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken)
        {
            return Task.FromResult(TopicExists);
        }

        public Task CreateSubscriptionAsync(
            string topicName,
            string subscriptionName,
            IReadOnlyList<string> consumerTopics,
            CancellationToken cancellationToken)
        {
            if (CreateException is not null)
            {
                throw CreateException;
            }

            CreatedSubscriptions.Add((topicName, subscriptionName, consumerTopics));
            return Task.CompletedTask;
        }

        public Task DeleteSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            if (DeleteException is not null)
            {
                throw DeleteException;
            }

            DeletedSubscriptions.Add((topicName, subscriptionName));
            return Task.CompletedTask;
        }

        public Task<TransportPublication?> ReadPublicationAsync(
            Guid sessionId,
            string topicName,
            string subscriptionName,
            CancellationToken cancellationToken)
        {
            ReadRequests.Add((sessionId, topicName, subscriptionName));
            OutstandingPublication = Publication;
            return Task.FromResult(Publication);
        }

        public Task<TransportPublication?> GetOutstandingPublicationAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(OutstandingPublication);
        }

        public Task RemovePublicationAsync(
            Guid sessionId,
            string topicName,
            string subscriptionName,
            CancellationToken cancellationToken)
        {
            RemoveRequests.Add((sessionId, topicName, subscriptionName));
            OutstandingPublication = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProviderPublicationRepository : IProviderPublicationRepository
    {
        private readonly Dictionary<Guid, ProviderPublicationData> _publications = [];
        private readonly Dictionary<Guid, ProviderPublicationData> _outstanding = [];

        public void SeedPublication(ProviderPublicationData publication)
        {
            _publications[publication.MessageId] = publication;
        }

        public void SeedOutstanding(Guid consumerSessionId, ProviderPublicationData publication)
        {
            _publications[publication.MessageId] = publication;
            _outstanding[consumerSessionId] = publication;
        }

        public Task<ProviderPublicationRepositoryResult> InsertPublishedPublicationAsync(ProviderPublicationCreateData publication, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ProviderPublicationRepositoryResult> ExpirePublicationAsync(Guid providerSessionId, Guid channelId, Guid messageId, DateTime expiredUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PublicationExpirationStateResult> IsPublicationExpiredAsync(Guid messageId, Guid consumerSessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(PublicationExpirationStateResult.Success(false));
        }

        public Task<ProviderPublicationRepositoryResult> MarkRecipientOutstandingAsync(Guid messageId, Guid consumerSessionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReadProviderPublicationResult> GetOutstandingPublicationAsync(Guid consumerSessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _outstanding.TryGetValue(consumerSessionId, out ProviderPublicationData? publication)
                    ? ReadProviderPublicationResult.Success(publication)
                    : ReadProviderPublicationResult.NoPublication());
        }

        public Task<ReadProviderPublicationResult> TryMarkPublicationOutstandingAsync(Guid messageId, Guid consumerSessionId, CancellationToken cancellationToken)
        {
            if (_outstanding.TryGetValue(consumerSessionId, out ProviderPublicationData? existing))
            {
                return Task.FromResult(ReadProviderPublicationResult.Success(existing));
            }

            if (!_publications.TryGetValue(messageId, out ProviderPublicationData? publication))
            {
                return Task.FromResult(ReadProviderPublicationResult.NoPublication());
            }

            _outstanding[consumerSessionId] = publication;
            return Task.FromResult(ReadProviderPublicationResult.Success(publication));
        }

        public Task<ProviderPublicationRepositoryResult> MarkRecipientSkippedExpiredAsync(Guid messageId, Guid consumerSessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(ProviderPublicationRepositoryResult.Success());
        }

        public Task<ProviderPublicationRepositoryResult> MarkRecipientRemovedAsync(Guid messageId, Guid consumerSessionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RemoveProviderPublicationRecipientResult> RemoveOutstandingPublicationAsync(Guid consumerSessionId, CancellationToken cancellationToken)
        {
            if (!_outstanding.Remove(consumerSessionId, out ProviderPublicationData? publication))
            {
                return Task.FromResult(RemoveProviderPublicationRecipientResult.Success(null));
            }

            return Task.FromResult(RemoveProviderPublicationRecipientResult.Success(publication.MessageId));
        }

        public Task<ProviderPublicationRepositoryResult> MarkRecipientsSubscriptionDeletedAsync(Guid consumerSessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(ProviderPublicationRepositoryResult.Success());
        }
    }

    private sealed class FakeChannelTopicTransport : IChannelTopicTransport
    {
        public bool TopicExists { get; init; } = true;
        public RequestFailedException? CreateException { get; init; }
        public List<string> CreatedTopics { get; } = [];

        public Task CreateTopicAsync(string topicName, CancellationToken cancellationToken)
        {
            if (CreateException is not null)
            {
                throw CreateException;
            }

            CreatedTopics.Add(topicName);
            return Task.CompletedTask;
        }

        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken)
        {
            return Task.FromResult(TopicExists);
        }

        public Task DeleteTopicAsync(string topicName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
