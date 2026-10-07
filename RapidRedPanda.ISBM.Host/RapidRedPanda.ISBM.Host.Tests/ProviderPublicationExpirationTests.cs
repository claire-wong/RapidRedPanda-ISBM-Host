using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Controllers;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Services;
using System.Reflection;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class ProviderPublicationExpirationTests
{
    [Fact]
    public void ExpirePublication_RouteMetadata_ExposesScopedProviderPublicationItemDeleteRoute()
    {
        Assert.Equal(["isbm/2.0/sessions/{sessionId}/publications/{messageId}"], GetProviderRoutes<HttpDeleteAttribute>(
            nameof(ProviderPublicationServiceController.ExpirePublication)));
    }

    [Fact]
    public async Task ExpirePublication_ControllerSuccess_ReturnsNoContentAndDelegatesIds()
    {
        Guid sessionId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();
        CapturingProviderPublicationSessionService service = new()
        {
            ExpireResult = ExpirePublicationResult.Success()
        };
        ProviderPublicationServiceController controller = CreateProviderController(service);

        IActionResult result = await controller.ExpirePublication(
            sessionId.ToString("D"),
            messageId.ToString("D"),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(sessionId.ToString("D"), service.ExpiredSessionId);
        Assert.Equal(messageId.ToString("D"), service.ExpiredMessageId);
    }

    [Fact]
    public async Task ExpirePublication_ControllerFaults_MapToEstablishedFaultShape()
    {
        ProviderPublicationServiceController controller = CreateProviderController(new CapturingProviderPublicationSessionService
        {
            ExpireResult = ExpirePublicationResult.Failure(ExpirePublicationFailureKind.MessageFault)
        });

        IActionResult result = await controller.ExpirePublication(
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            CancellationToken.None);

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(notFound.Value);
        Assert.Equal("MessageFault", fault.Fault);
    }

    [Fact]
    public async Task ExpirePublication_OpenProviderSession_ExpiresPublicationForSameChannel()
    {
        Guid sessionId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();
        FakeProviderPublicationRepository publicationRepository = new();

        ExpirePublicationResult result = await CreateProviderService(
                sessionRepository: new FakeProviderPublicationSessionRepository
                {
                    SessionResult = GetProviderPublicationSessionResult.Success(OpenProviderSession(sessionId, channelId))
                },
                publicationRepository: publicationRepository)
            .ExpirePublicationAsync(sessionId.ToString("D"), messageId.ToString("D"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal((sessionId, channelId, messageId), Assert.Single(publicationRepository.ExpireRequests));
    }

    [Fact]
    public async Task ExpirePublication_InvalidMessageId_ReturnsParameterFaultWithoutRepositoryCall()
    {
        FakeProviderPublicationRepository publicationRepository = new();

        ExpirePublicationResult result = await CreateProviderService(publicationRepository: publicationRepository)
            .ExpirePublicationAsync(Guid.NewGuid().ToString("D"), "not-a-guid", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ExpirePublicationFailureKind.ParameterFault, result.FailureKind);
        Assert.Empty(publicationRepository.ExpireRequests);
    }

    [Fact]
    public async Task ExpirePublication_UnknownMessage_ReturnsSuccessNoOp()
    {
        Guid sessionId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        FakeProviderPublicationRepository publicationRepository = new()
        {
            ExpireResult = ProviderPublicationRepositoryResult.Failure(
                ProviderPublicationRepositoryFailureKind.NotFound,
                "missing")
        };

        ExpirePublicationResult result = await CreateProviderService(
                sessionRepository: new FakeProviderPublicationSessionRepository
                {
                    SessionResult = GetProviderPublicationSessionResult.Success(OpenProviderSession(sessionId, channelId))
                },
                publicationRepository: publicationRepository)
            .ExpirePublicationAsync(sessionId.ToString("D"), Guid.NewGuid().ToString("D"), CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task PostPublication_SuccessfulAzureSend_PersistsMetadataWithMessageContent()
    {
        Guid sessionId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        FakeProviderPublicationRepository publicationRepository = new();
        FakePublicationTransport transport = new();

        PostPublicationResult result = await CreateProviderService(
                channelRepository: new FakeChannelRepository
                {
                    TopicBindingByIdResult = GetChannelTopicBindingResult.Success(new ChannelTopicBindingData(
                        channelId,
                        "/publication/channel",
                        "Publication",
                        "topic"))
                },
                sessionRepository: new FakeProviderPublicationSessionRepository
                {
                    SessionResult = GetProviderPublicationSessionResult.Success(OpenProviderSession(sessionId, channelId))
                },
                publicationRepository: publicationRepository,
                publicationTransport: transport)
            .PostPublicationAsync(
                sessionId.ToString("D"),
                ["Topic-A", "Topic-A"],
                InternalMessageContent.FromJson(BinaryData.FromString("""{"reading":42}""")),
                "PT1H",
                CancellationToken.None);

        Assert.True(result.Succeeded);
        ProviderPublicationCreateData metadata = Assert.Single(publicationRepository.InsertedPublications);
        Assert.Equal(result.PublicationId, metadata.MessageId);
        Assert.Equal(sessionId, metadata.ProviderSessionId);
        Assert.Equal(channelId, metadata.ChannelId);
        Assert.Equal(["Topic-A"], metadata.Topics);
        Assert.NotNull(metadata.EffectiveExpiryUtc);
        Assert.Equal("""{"content":{"reading":42}}""", metadata.MessageContent.LegacyMessageContent);
        Assert.Equal("Json", metadata.MessageContent.PayloadKind);
        Assert.NotNull(metadata.MessageContent.Payload);
        Assert.Single(transport.SentMessages);
    }

    [Fact]
    public async Task SqlProviderPublicationRepositorySource_CreatesPendingNotificationsFromRecipientMetadataInsideInsertTransaction()
    {
        string source = await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "RapidRedPanda-ISBM-Host",
            "Data",
            "SqlProviderPublicationRepository.cs")));

        Assert.Contains("await InsertRecipientSnapshotAsync(", source, StringComparison.Ordinal);
        Assert.Contains("_notificationOptions.MaximumDeliveryAgeMinutes", source, StringComparison.Ordinal);
        Assert.Contains("await InsertPublicationNotificationsAsync(", source, StringComparison.Ordinal);
        Assert.Contains("insert dbo.Notifications", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DeliveryDeadlineUtc", source, StringComparison.Ordinal);
        Assert.Contains("from dbo.ProviderPublicationRecipients recipient", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inner join dbo.ConsumerSubscriptionSessions session", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("session.ListenerUrl is not null", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NotificationTopics", source, StringComparison.Ordinal);
        Assert.Contains("UQ_Notifications_Message_ConsumerSubscriptionSession_Publication", await ReadInstallScriptAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("ReceiveMessageAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CompleteMessageAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SqlProviderPublicationRepositorySource_UsesDurableContentAndRecipientStateForReadAndRemove()
    {
        string source = await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "RapidRedPanda-ISBM-Host",
            "Data",
            "SqlProviderPublicationRepository.cs")));

        Assert.Contains("MessageContent,", source, StringComparison.Ordinal);
        Assert.Contains("AddMessageContentParameters(command, publication.MessageContent)", source, StringComparison.Ordinal);
        Assert.Contains("GetOutstandingPublicationAsync", source, StringComparison.Ordinal);
        Assert.Contains("TryMarkPublicationOutstandingAsync", source, StringComparison.Ordinal);
        Assert.Contains("RemoveOutstandingPublicationAsync", source, StringComparison.Ordinal);
        Assert.Contains("publication.MessageContent is not null", source, StringComparison.Ordinal);
        Assert.Contains("with (updlock, holdlock)", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("State = @OutstandingState", source, StringComparison.Ordinal);
        Assert.Contains("State = @RemovedState", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadPublication_OutstandingPublication_ReturnsWithoutCheckingExpiration()
    {
        Guid sessionId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();
        FakeProviderPublicationRepository publicationRepository = new();
        publicationRepository.SeedOutstanding(sessionId, PublicationData(messageId));
        FakeSubscriptionTransport transport = new();

        ReadPublicationResult result = await CreateConsumerService(
                sessionRepository: OpenConsumerSessionRepository(sessionId),
                publicationRepository: publicationRepository,
                subscriptionTransport: transport)
            .ReadPublicationAsync(sessionId.ToString("D"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(messageId, result.Publication?.MessageId);
        Assert.Equal(["Topic-A"], result.Publication?.Topics);
        Assert.Equal(InternalMessageContentKind.Json, result.Publication?.MessageContent.Kind);
        Assert.Equal(0, publicationRepository.ExpirationChecks);
        Assert.Empty(transport.CompletedSessions);
    }

    [Fact]
    public async Task ReadPublication_RecreatedServiceReturnsDurableOutstandingWithoutBrokerLock()
    {
        Guid sessionId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();
        FakeProviderPublicationRepository publicationRepository = new();
        FakeSubscriptionTransport firstTransport = new();
        firstTransport.Publications.Enqueue(Publication(messageId));

        ReadPublicationResult firstRead = await CreateConsumerService(
                sessionRepository: OpenConsumerSessionRepository(sessionId),
                publicationRepository: publicationRepository,
                subscriptionTransport: firstTransport)
            .ReadPublicationAsync(sessionId.ToString("D"), CancellationToken.None);

        Assert.True(firstRead.Succeeded);
        Assert.Equal(messageId, firstRead.Publication?.MessageId);
        Assert.True(publicationRepository.HasOutstanding(sessionId));

        ReadPublicationResult secondRead = await CreateConsumerService(
                sessionRepository: OpenConsumerSessionRepository(sessionId),
                publicationRepository: publicationRepository,
                subscriptionTransport: new FakeSubscriptionTransport())
            .ReadPublicationAsync(sessionId.ToString("D"), CancellationToken.None);

        Assert.True(secondRead.Succeeded);
        Assert.Equal(messageId, secondRead.Publication?.MessageId);
        Assert.Equal(["Topic-A"], secondRead.Publication?.Topics);
        Assert.Equal(InternalMessageContentKind.Json, secondRead.Publication?.MessageContent.Kind);
    }

    [Fact]
    public async Task ReadPublication_ExpiredHead_CompletesItAndContinuesSequentially()
    {
        Guid sessionId = Guid.NewGuid();
        Guid expiredMessageId = Guid.NewGuid();
        Guid liveMessageId = Guid.NewGuid();
        FakeProviderPublicationRepository publicationRepository = new()
        {
            ExpiredMessageIds = { expiredMessageId }
        };
        FakeSubscriptionTransport transport = new();
        transport.Publications.Enqueue(Publication(expiredMessageId));
        transport.Publications.Enqueue(Publication(liveMessageId));

        ReadPublicationResult result = await CreateConsumerService(
                sessionRepository: OpenConsumerSessionRepository(sessionId),
                publicationRepository: publicationRepository,
                subscriptionTransport: transport)
            .ReadPublicationAsync(sessionId.ToString("D"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(liveMessageId, result.Publication?.MessageId);
        Assert.Equal([expiredMessageId], transport.CompletedMessages);
        Assert.Equal([expiredMessageId], publicationRepository.SkippedRecipients.Select(item => item.MessageId).ToArray());
        Assert.Equal([liveMessageId], publicationRepository.OutstandingRecipients.Select(item => item.MessageId).ToArray());
    }

    [Fact]
    public async Task RemovePublication_OutstandingExpiredPublication_CompletesThenMarksRemoved()
    {
        Guid sessionId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();
        FakeProviderPublicationRepository publicationRepository = new();
        publicationRepository.SeedOutstanding(sessionId, PublicationData(messageId));
        FakeSubscriptionTransport transport = new()
        {
            OutstandingPublication = Publication(messageId)
        };

        RemovePublicationResult result = await CreateConsumerService(
                sessionRepository: OpenConsumerSessionRepository(sessionId),
                publicationRepository: publicationRepository,
                subscriptionTransport: transport)
            .RemovePublicationAsync(sessionId.ToString("D"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal([messageId], transport.CompletedMessages);
        Assert.Equal([(messageId, sessionId)], publicationRepository.RemovedRecipients);
    }

    [Fact]
    public async Task RemovePublication_DurableOutstandingWithoutBrokerLock_MarksRemovedOnly()
    {
        Guid sessionId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();
        FakeProviderPublicationRepository publicationRepository = new();
        publicationRepository.SeedOutstanding(sessionId, PublicationData(messageId));
        FakeSubscriptionTransport transport = new();

        RemovePublicationResult result = await CreateConsumerService(
                sessionRepository: OpenConsumerSessionRepository(sessionId),
                publicationRepository: publicationRepository,
                subscriptionTransport: transport)
            .RemovePublicationAsync(sessionId.ToString("D"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(publicationRepository.HasOutstanding(sessionId));
        Assert.Equal([(messageId, sessionId)], publicationRepository.RemovedRecipients);
        Assert.Empty(transport.CompletedMessages);
    }

    [Fact]
    public async Task RemovePublication_NoDurableOutstanding_DoesNotCompleteNextBrokerMessage()
    {
        Guid sessionId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();
        FakeProviderPublicationRepository publicationRepository = new();
        FakeSubscriptionTransport transport = new()
        {
            OutstandingPublication = Publication(messageId)
        };

        RemovePublicationResult result = await CreateConsumerService(
                sessionRepository: OpenConsumerSessionRepository(sessionId),
                publicationRepository: publicationRepository,
                subscriptionTransport: transport)
            .RemovePublicationAsync(sessionId.ToString("D"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(publicationRepository.RemovedRecipients);
        Assert.Empty(transport.CompletedMessages);
    }

    private static ProviderPublicationServiceController CreateProviderController(
        CapturingProviderPublicationSessionService service)
    {
        ProviderPublicationServiceController controller = new(
            service,
            new NoOpUsernameTokenParser(),
            NullLogger<ProviderPublicationServiceController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static async Task<string> ReadInstallScriptAsync()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Scripts", "Install", "001_CreateDatabase.sql");
        return await File.ReadAllTextAsync(path);
    }

    private static ProviderPublicationSessionService CreateProviderService(
        FakeChannelRepository? channelRepository = null,
        FakeProviderPublicationSessionRepository? sessionRepository = null,
        FakeProviderPublicationRepository? publicationRepository = null,
        FakePublicationTransport? publicationTransport = null)
    {
        return new ProviderPublicationSessionService(
            channelRepository ?? new FakeChannelRepository(),
            sessionRepository ?? new FakeProviderPublicationSessionRepository(),
            publicationRepository ?? new FakeProviderPublicationRepository(),
            publicationTransport ?? new FakePublicationTransport(),
            NullLogger<ProviderPublicationSessionService>.Instance);
    }

    private static ConsumerSubscriptionSessionService CreateConsumerService(
        FakeSessionRepository sessionRepository,
        FakeProviderPublicationRepository publicationRepository,
        FakeSubscriptionTransport subscriptionTransport)
    {
        return new ConsumerSubscriptionSessionService(
            new FakeChannelRepository(),
            sessionRepository,
            publicationRepository,
            subscriptionTransport,
            new FakeChannelTopicTransport(),
            NullLogger<ConsumerSubscriptionSessionService>.Instance);
    }

    private static FakeSessionRepository OpenConsumerSessionRepository(Guid sessionId)
    {
        return new FakeSessionRepository
        {
            SessionResult = GetSubscriptionSessionResult.Success(new ConsumerSubscriptionSessionData(
                sessionId,
                Guid.NewGuid(),
                SubscriptionSessionTypes.ConsumerPublicationSubscription,
                SubscriptionSessionStates.Open,
                "topic",
                $"isbm-css-{sessionId:N}",
                ["Topic-A"],
                null,
                DateTime.UtcNow,
                null))
        };
    }

    private static TransportPublication Publication(Guid messageId)
    {
        return new TransportPublication(
            messageId,
            ["Topic-A"],
            "application/json",
            BinaryData.FromString($$"""
                {
                  "messageId": "{{messageId:D}}",
                  "messageContent": {
                    "content": {
                      "reading": 42
                    }
                  },
                  "topics": ["Topic-A"]
                }
                """));
    }

    private static ProviderPublicationData PublicationData(Guid messageId)
    {
        return new ProviderPublicationData(
            messageId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            ["Topic-A"],
            SqlMessageContentAdapter.ToSql(InternalMessageContent.FromJson(BinaryData.FromString("""{"reading":42}"""))));
    }

    private static ProviderPublicationSessionData OpenProviderSession(Guid sessionId, Guid channelId)
    {
        return new ProviderPublicationSessionData(
            sessionId,
            channelId,
            ProviderPublicationSessionStates.Open,
            null,
            DateTime.UtcNow,
            null);
    }

    private static string[] GetProviderRoutes<TAttribute>(string actionName)
        where TAttribute : HttpMethodAttribute
    {
        RouteAttribute controllerRoute = Assert.IsType<RouteAttribute>(
            typeof(ProviderPublicationServiceController).GetCustomAttribute<RouteAttribute>());
        MethodInfo action = Assert.Single(
            typeof(ProviderPublicationServiceController).GetMethods(),
            method => method.Name == actionName);

        return action
            .GetCustomAttributes<TAttribute>()
            .Select(attribute => string.Join('/', controllerRoute.Template, attribute.Template))
            .ToArray();
    }

    private sealed class CapturingProviderPublicationSessionService : IProviderPublicationSessionService
    {
        public ExpirePublicationResult ExpireResult { get; init; } = ExpirePublicationResult.Success();
        public string? ExpiredSessionId { get; private set; }
        public string? ExpiredMessageId { get; private set; }

        public Task<OpenPublicationSessionResult> OpenPublicationSessionAsync(string channelId, ChannelCredentials? credentials, CancellationToken cancellationToken)
        {
            return Task.FromResult(OpenPublicationSessionResult.Success(Guid.NewGuid()));
        }

        public Task<ClosePublicationSessionResult> ClosePublicationSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(ClosePublicationSessionResult.Success());
        }

        public Task<PostPublicationResult> PostPublicationAsync(string sessionId, IReadOnlyList<string?>? topics, InternalMessageContent messageContent, string? expiry, CancellationToken cancellationToken)
        {
            return Task.FromResult(PostPublicationResult.Success(Guid.NewGuid()));
        }

        public Task<ExpirePublicationResult> ExpirePublicationAsync(string sessionId, string messageId, CancellationToken cancellationToken)
        {
            ExpiredSessionId = sessionId;
            ExpiredMessageId = messageId;
            return Task.FromResult(ExpireResult);
        }
    }

    private sealed class FakeProviderPublicationRepository : IProviderPublicationRepository
    {
        public ProviderPublicationRepositoryResult ExpireResult { get; init; } = ProviderPublicationRepositoryResult.Success();
        public HashSet<Guid> ExpiredMessageIds { get; } = [];
        public int ExpirationChecks { get; private set; }
        public List<ProviderPublicationCreateData> InsertedPublications { get; } = [];
        public List<(Guid SessionId, Guid ChannelId, Guid MessageId)> ExpireRequests { get; } = [];
        public List<(Guid MessageId, Guid ConsumerSessionId)> OutstandingRecipients { get; } = [];
        public List<(Guid MessageId, Guid ConsumerSessionId)> SkippedRecipients { get; } = [];
        public List<(Guid MessageId, Guid ConsumerSessionId)> RemovedRecipients { get; } = [];
        public List<Guid> SubscriptionDeletedRecipients { get; } = [];
        private readonly Dictionary<Guid, ProviderPublicationData> _outstandingPublications = [];

        public bool HasOutstanding(Guid consumerSessionId)
        {
            return _outstandingPublications.ContainsKey(consumerSessionId);
        }

        public void SeedOutstanding(Guid consumerSessionId, ProviderPublicationData publication)
        {
            _outstandingPublications[consumerSessionId] = publication;
        }

        public Task<ProviderPublicationRepositoryResult> InsertPublishedPublicationAsync(ProviderPublicationCreateData publication, CancellationToken cancellationToken)
        {
            InsertedPublications.Add(publication);
            return Task.FromResult(ProviderPublicationRepositoryResult.Success());
        }

        public Task<ProviderPublicationRepositoryResult> ExpirePublicationAsync(Guid providerSessionId, Guid channelId, Guid messageId, DateTime expiredUtc, CancellationToken cancellationToken)
        {
            ExpireRequests.Add((providerSessionId, channelId, messageId));
            return Task.FromResult(ExpireResult);
        }

        public Task<PublicationExpirationStateResult> IsPublicationExpiredAsync(Guid messageId, Guid consumerSessionId, CancellationToken cancellationToken)
        {
            ExpirationChecks++;
            return Task.FromResult(PublicationExpirationStateResult.Success(ExpiredMessageIds.Contains(messageId)));
        }

        public Task<ProviderPublicationRepositoryResult> MarkRecipientOutstandingAsync(Guid messageId, Guid consumerSessionId, CancellationToken cancellationToken)
        {
            OutstandingRecipients.Add((messageId, consumerSessionId));
            return Task.FromResult(ProviderPublicationRepositoryResult.Success());
        }

        public Task<ReadProviderPublicationResult> GetOutstandingPublicationAsync(Guid consumerSessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _outstandingPublications.TryGetValue(consumerSessionId, out ProviderPublicationData? publication)
                    ? ReadProviderPublicationResult.Success(publication)
                    : ReadProviderPublicationResult.NoPublication());
        }

        public Task<ReadProviderPublicationResult> TryMarkPublicationOutstandingAsync(Guid messageId, Guid consumerSessionId, CancellationToken cancellationToken)
        {
            if (_outstandingPublications.TryGetValue(consumerSessionId, out ProviderPublicationData? existing))
            {
                return Task.FromResult(ReadProviderPublicationResult.Success(existing));
            }

            if (ExpiredMessageIds.Contains(messageId))
            {
                return Task.FromResult(ReadProviderPublicationResult.NoPublication());
            }

            ProviderPublicationData publication = PublicationData(messageId);
            _outstandingPublications[consumerSessionId] = publication;
            OutstandingRecipients.Add((messageId, consumerSessionId));
            return Task.FromResult(ReadProviderPublicationResult.Success(publication));
        }

        public Task<ProviderPublicationRepositoryResult> MarkRecipientSkippedExpiredAsync(Guid messageId, Guid consumerSessionId, CancellationToken cancellationToken)
        {
            SkippedRecipients.Add((messageId, consumerSessionId));
            return Task.FromResult(ProviderPublicationRepositoryResult.Success());
        }

        public Task<ProviderPublicationRepositoryResult> MarkRecipientRemovedAsync(Guid messageId, Guid consumerSessionId, CancellationToken cancellationToken)
        {
            RemovedRecipients.Add((messageId, consumerSessionId));
            return Task.FromResult(ProviderPublicationRepositoryResult.Success());
        }

        public Task<RemoveProviderPublicationRecipientResult> RemoveOutstandingPublicationAsync(Guid consumerSessionId, CancellationToken cancellationToken)
        {
            if (!_outstandingPublications.Remove(consumerSessionId, out ProviderPublicationData? publication))
            {
                return Task.FromResult(RemoveProviderPublicationRecipientResult.Success(null));
            }

            RemovedRecipients.Add((publication.MessageId, consumerSessionId));
            return Task.FromResult(RemoveProviderPublicationRecipientResult.Success(publication.MessageId));
        }

        public Task<ProviderPublicationRepositoryResult> MarkRecipientsSubscriptionDeletedAsync(Guid consumerSessionId, CancellationToken cancellationToken)
        {
            SubscriptionDeletedRecipients.Add(consumerSessionId);
            return Task.FromResult(ProviderPublicationRepositoryResult.Success());
        }
    }

    private sealed class FakeProviderPublicationSessionRepository : IProviderPublicationSessionRepository
    {
        public GetProviderPublicationSessionResult SessionResult { get; init; } =
            GetProviderPublicationSessionResult.Failure(GetProviderPublicationSessionFailureKind.NotFound, "missing");

        public Task<InsertProviderPublicationSessionResult> InsertProviderPublicationSessionAsync(ProviderPublicationSessionCreateData session, CancellationToken cancellationToken)
        {
            return Task.FromResult(InsertProviderPublicationSessionResult.Success());
        }

        public Task<GetProviderPublicationSessionResult> GetProviderPublicationSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(SessionResult);
        }

        public Task<CloseProviderPublicationSessionResult> CloseProviderPublicationSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(CloseProviderPublicationSessionResult.Success());
        }
    }

    private sealed class FakeSessionRepository : ISubscriptionSessionRepository
    {
        public GetSubscriptionSessionResult SessionResult { get; init; } =
            GetSubscriptionSessionResult.Failure(GetSubscriptionSessionFailureKind.NotFound, "missing");

        public Task<InsertSubscriptionSessionResult> InsertConsumerSubscriptionSessionAsync(ConsumerSubscriptionSessionCreateData session, CancellationToken cancellationToken)
        {
            return Task.FromResult(InsertSubscriptionSessionResult.Success());
        }

        public Task<GetSubscriptionSessionResult> GetSubscriptionSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(SessionResult);
        }

        public Task<CloseSubscriptionSessionResult> CloseSubscriptionSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(CloseSubscriptionSessionResult.Success());
        }
    }

    private sealed class FakeSubscriptionTransport : ISubscriptionTransport
    {
        public Queue<TransportPublication> Publications { get; } = [];
        public TransportPublication? OutstandingPublication { get; set; }
        public List<Guid> CompletedSessions { get; } = [];
        public List<Guid> CompletedMessages { get; } = [];

        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task CreateSubscriptionAsync(string topicName, string subscriptionName, IReadOnlyList<string> consumerTopics, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task DeleteSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<TransportPublication?> GetOutstandingPublicationAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(OutstandingPublication);
        }

        public Task<TransportPublication?> ReadPublicationAsync(Guid sessionId, string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            OutstandingPublication = Publications.Count == 0 ? null : Publications.Dequeue();
            return Task.FromResult(OutstandingPublication);
        }

        public Task RemovePublicationAsync(Guid sessionId, string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            CompletedSessions.Add(sessionId);
            if (OutstandingPublication is not null)
            {
                CompletedMessages.Add(OutstandingPublication.PublicationId);
            }

            OutstandingPublication = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePublicationTransport : IPublicationTransport
    {
        public List<ServiceBusMessage> SentMessages { get; } = [];

        public Task SendPublicationAsync(string topicName, ServiceBusMessage message, CancellationToken cancellationToken)
        {
            SentMessages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeChannelTopicTransport : IChannelTopicTransport
    {
        public Task CreateTopicAsync(string topicName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task DeleteTopicAsync(string topicName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeChannelRepository : IChannelRepository
    {
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
            return Task.FromResult(TopicBindingByIdResult);
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForConsumerPublicationSubscriptionAsync(string channelUri, ChannelCredentials? credentials, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.NotFound, "missing"));
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForProviderPublicationSessionAsync(string channelUri, ChannelCredentials? credentials, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.NotFound, "missing"));
        }

        public Task<CreateChannelResult> CreateChannelAsync(ChannelCreateRequestData request, CancellationToken cancellationToken)
        {
            return Task.FromResult(CreateChannelResult.Success(new ChannelData(
                request.ChannelId,
                request.ChannelUri,
                request.ChannelType,
                request.Description,
                request.AzureTopicName)));
        }

        public Task<DeleteChannelResult> DeleteChannelAsync(string channelUri, CancellationToken cancellationToken)
        {
            return Task.FromResult(DeleteChannelResult.Success());
        }

        public Task<SecurityTokenMutationResult> AddSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken)
        {
            return Task.FromResult(SecurityTokenMutationResult.Success());
        }

        public Task<SecurityTokenMutationResult> RemoveSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken)
        {
            return Task.FromResult(SecurityTokenMutationResult.Success());
        }
    }

    private sealed class NoOpUsernameTokenParser : IUsernameTokenParser
    {
        public IReadOnlyList<UsernameTokenInput> Parse(IReadOnlyList<SecurityTokenRequest>? securityTokens)
        {
            return [];
        }

        public string NormalizeUsername(string username)
        {
            return username;
        }
    }
}
