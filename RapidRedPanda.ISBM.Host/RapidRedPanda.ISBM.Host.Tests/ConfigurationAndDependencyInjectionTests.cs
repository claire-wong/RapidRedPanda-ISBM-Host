using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Azure.Messaging.ServiceBus;
using RapidRedPanda.ISBM.Host.Controllers;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Services;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class ConfigurationAndDependencyInjectionTests
{
    [Fact]
    public void SqlChannelRepository_Constructs_WhenConnectionStringExists()
    {
        IConfiguration configuration = BuildConfiguration("Server=(localdb)\\MSSQLLocalDB;Database=TestOnly;Integrated Security=True;");
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => { });

        SqlChannelRepository repository = new(
            configuration,
            loggerFactory.CreateLogger<SqlChannelRepository>());

        Assert.NotNull(repository);
    }

    [Fact]
    public void SqlChannelRepository_ConstructionFails_WhenConnectionStringMissing()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => { });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new SqlChannelRepository(configuration, loggerFactory.CreateLogger<SqlChannelRepository>()));

        Assert.Equal("Connection string 'ISBMHost' is not configured.", exception.Message);
    }

    [Fact]
    public void DependencyInjection_ResolvesRepositoryAndController()
    {
        ServiceCollection services = new();
        services.AddSingleton(BuildConfiguration("Server=(localdb)\\MSSQLLocalDB;Database=TestOnly;Integrated Security=True;"));
        services.AddLogging();
        services.AddSingleton<IUsernameTokenParser, UsernameTokenParser>();
        services.AddScoped<IChannelRepository, SqlChannelRepository>();
        services.AddScoped<ISubscriptionSessionRepository, FakeSubscriptionSessionRepository>();
        services.AddScoped<IProviderPublicationSessionRepository, SqlProviderPublicationSessionRepository>();
        services.AddScoped<IPublicationNotificationRepository, FakePublicationNotificationRepository>();
        services.AddScoped<IConsumerRequestSessionRepository, FakeConsumerRequestSessionRepository>();
        services.AddScoped<IConsumerRequestRepository, FakeConsumerRequestRepository>();
        services.AddScoped<IChannelLifecycleService, ChannelLifecycleService>();
        services.AddScoped<IConsumerSubscriptionSessionService, FakeConsumerSubscriptionSessionService>();
        services.AddScoped<IProviderPublicationSessionService, ProviderPublicationSessionService>();
        services.AddScoped<IProviderRequestSessionService, FakeProviderRequestSessionService>();
        services.AddScoped<IConsumerRequestSessionService, FakeConsumerRequestSessionService>();
        services.AddScoped<ISessionCloseService, SessionCloseService>();
        services.AddSingleton<IChannelTopicTransport, FakeChannelTopicTransport>();
        services.AddSingleton<ISubscriptionTransport, FakeSubscriptionTransport>();
        services.AddSingleton<IPublicationTransport, FakePublicationTransport>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPublicationNotificationUriBuilder, PublicationNotificationUriBuilder>();
        services.AddSingleton<IPublicationNotificationClient, FakePublicationNotificationClient>();
        services.AddOptions<PublicationNotificationOptions>();
        services.AddTransient<ChannelManagementServicelController>();
        services.AddTransient<ProviderPublicationServiceController>();
        services.AddTransient<ConsumerPublicationServiceController>();
        services.AddTransient<ProviderRequestServiceController>();
        services.AddTransient<ConsumerRequestServiceController>();
        services.AddTransient<SessionServiceController>();

        using ServiceProvider serviceProvider = services.BuildServiceProvider(validateScopes: true);
        using IServiceScope scope = serviceProvider.CreateScope();

        Assert.IsType<SqlChannelRepository>(scope.ServiceProvider.GetRequiredService<IChannelRepository>());
        Assert.IsType<SqlProviderPublicationSessionRepository>(scope.ServiceProvider.GetRequiredService<IProviderPublicationSessionRepository>());
        Assert.IsType<FakePublicationNotificationRepository>(scope.ServiceProvider.GetRequiredService<IPublicationNotificationRepository>());
        Assert.IsType<PublicationNotificationUriBuilder>(scope.ServiceProvider.GetRequiredService<IPublicationNotificationUriBuilder>());
        Assert.IsType<FakePublicationNotificationClient>(scope.ServiceProvider.GetRequiredService<IPublicationNotificationClient>());
        Assert.IsType<ProviderPublicationSessionService>(scope.ServiceProvider.GetRequiredService<IProviderPublicationSessionService>());
        Assert.IsType<SessionCloseService>(scope.ServiceProvider.GetRequiredService<ISessionCloseService>());
        Assert.IsAssignableFrom<Controller>(scope.ServiceProvider.GetRequiredService<ChannelManagementServicelController>());
        Assert.IsAssignableFrom<Controller>(scope.ServiceProvider.GetRequiredService<ProviderPublicationServiceController>());
        Assert.IsAssignableFrom<Controller>(scope.ServiceProvider.GetRequiredService<ConsumerPublicationServiceController>());
        Assert.IsAssignableFrom<Controller>(scope.ServiceProvider.GetRequiredService<ProviderRequestServiceController>());
        Assert.IsAssignableFrom<Controller>(scope.ServiceProvider.GetRequiredService<ConsumerRequestServiceController>());
        Assert.IsAssignableFrom<Controller>(scope.ServiceProvider.GetRequiredService<SessionServiceController>());
    }

    [Fact]
    public async Task ProgramDoesNotHardCodeConnectionString()
    {
        string program = await ReadHostFileAsync("Program.cs");

        Assert.DoesNotContain("AttachDbFilename", program, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MSSQLLocalDB", program, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionStrings:ISBMHost", program, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ControllerDoesNotConstructRepositoryOrSqlConnection()
    {
        string controller = await ReadHostFileAsync("Controllers", "ChannelManagementServicelController.cs");

        Assert.DoesNotContain("new SqlChannelRepository", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlConnection", controller, StringComparison.Ordinal);
        Assert.Contains("IChannelRepository channelRepository", controller, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepositoryUsesExpectedConnectionStringKey()
    {
        string repository = await ReadHostFileAsync("Data", "SqlChannelRepository.cs");

        Assert.Contains("ConnectionStringName = \"ISBMHost\"", repository, StringComparison.Ordinal);
        Assert.Contains("GetConnectionString(ConnectionStringName)", repository, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProgramRegistersPublicationNotificationDispatcherDependencies()
    {
        string program = await ReadHostFileAsync("Program.cs");

        Assert.Contains("IPublicationNotificationRepository", program, StringComparison.Ordinal);
        Assert.Contains("SqlPublicationNotificationRepository", program, StringComparison.Ordinal);
        Assert.Contains("IPublicationNotificationClient", program, StringComparison.Ordinal);
        Assert.Contains("PublicationNotificationClient", program, StringComparison.Ordinal);
        Assert.Contains("AddHttpClient(PublicationNotificationClient.HttpClientName", program, StringComparison.Ordinal);
        Assert.Contains("AllowAutoRedirect = false", program, StringComparison.Ordinal);
        Assert.Contains("UseCookies = false", program, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<PublicationNotificationDispatcher>", program, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(program, "IPublicationNotificationRepository"));
        Assert.Equal(1, CountOccurrences(program, "AddHostedService<PublicationNotificationDispatcher>"));
        Assert.DoesNotContain("ResponseNotificationDispatcher", program, StringComparison.Ordinal);
        Assert.DoesNotContain("ResponseNotificationClient", program, StringComparison.Ordinal);
        Assert.DoesNotContain("ResponseNotifications\"", program, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotificationConfiguration_IsSharedAcrossAllNotificationServices()
    {
        string options = await ReadHostFileAsync("Services", "PublicationNotificationOptions.cs");
        string settings = await ReadHostFileAsync("appsettings.json");

        Assert.Contains("PublicationNotifications", settings, StringComparison.Ordinal);
        Assert.Contains("Enabled", settings, StringComparison.Ordinal);
        Assert.Contains("BatchSize", settings, StringComparison.Ordinal);
        Assert.Contains("MaxConcurrency", settings, StringComparison.Ordinal);
        Assert.Contains("IdlePollIntervalSeconds", settings, StringComparison.Ordinal);
        Assert.Contains("ErrorPollIntervalSeconds", settings, StringComparison.Ordinal);
        Assert.Contains("LeaseDurationSeconds", settings, StringComparison.Ordinal);
        Assert.Contains("HttpTimeoutSeconds", settings, StringComparison.Ordinal);
        Assert.Contains("MaximumAttempts", settings, StringComparison.Ordinal);
        Assert.Contains("MaximumDeliveryAgeMinutes", settings, StringComparison.Ordinal);
        Assert.Contains("InitialRetryDelaySeconds", settings, StringComparison.Ordinal);
        Assert.Contains("MaximumRetryDelaySeconds", settings, StringComparison.Ordinal);
        Assert.Contains("BackoffMultiplier", settings, StringComparison.Ordinal);
        Assert.Contains("JitterPercent", settings, StringComparison.Ordinal);
        Assert.Contains("SectionName = \"PublicationNotifications\"", options, StringComparison.Ordinal);
        Assert.DoesNotContain("ResponseNotification", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("ResponseNotification", options, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationNotificationOptions_ValidateBounds()
    {
        Assert.True(PublicationNotificationOptions.IsValid(new PublicationNotificationOptions()));
        Assert.False(PublicationNotificationOptions.IsValid(new PublicationNotificationOptions { BatchSize = 0 }));
        Assert.False(PublicationNotificationOptions.IsValid(new PublicationNotificationOptions { MaxConcurrency = 0 }));
        Assert.False(PublicationNotificationOptions.IsValid(new PublicationNotificationOptions { LeaseDurationSeconds = 0 }));
        Assert.False(PublicationNotificationOptions.IsValid(new PublicationNotificationOptions { LeaseDurationSeconds = 10, HttpTimeoutSeconds = 10 }));
        Assert.False(PublicationNotificationOptions.IsValid(new PublicationNotificationOptions { LeaseDurationSeconds = 29, HttpTimeoutSeconds = 10 }));
        Assert.False(PublicationNotificationOptions.IsValid(new PublicationNotificationOptions { MaximumDeliveryAgeMinutes = 0 }));
        Assert.False(PublicationNotificationOptions.IsValid(new PublicationNotificationOptions { MaximumAttempts = 0 }));
        Assert.False(PublicationNotificationOptions.IsValid(new PublicationNotificationOptions { InitialRetryDelaySeconds = -1 }));
        Assert.False(PublicationNotificationOptions.IsValid(new PublicationNotificationOptions { MaximumRetryDelaySeconds = 9, InitialRetryDelaySeconds = 10 }));
        Assert.False(PublicationNotificationOptions.IsValid(new PublicationNotificationOptions { BackoffMultiplier = 0.99 }));
        Assert.False(PublicationNotificationOptions.IsValid(new PublicationNotificationOptions { JitterPercent = -1 }));
        Assert.False(PublicationNotificationOptions.IsValid(new PublicationNotificationOptions { JitterPercent = 101 }));
    }

    private static IConfiguration BuildConfiguration(string connectionString)
    {
        Dictionary<string, string?> configurationValues = new()
        {
            ["ConnectionStrings:ISBMHost"] = connectionString
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
    }

    private static async Task<string> ReadHostFileAsync(params string[] pathSegments)
    {
        string path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "RapidRedPanda-ISBM-Host",
            Path.Combine(pathSegments)));

        return await File.ReadAllTextAsync(path);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int startIndex = 0;

        while (true)
        {
            int index = text.IndexOf(value, startIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            startIndex = index + value.Length;
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

    private sealed class FakePublicationTransport : IPublicationTransport
    {
        public Task SendPublicationAsync(string topicName, ServiceBusMessage message, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakePublicationNotificationClient : IPublicationNotificationClient
    {
        public Task<PublicationNotificationDeliveryResult> SendAsync(
            ClaimedPublicationNotification notification,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakePublicationNotificationRepository : IPublicationNotificationRepository
    {
        public Task<PublicationNotificationClaimResult> ClaimDueAsync(string lockOwner, int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PublicationNotificationEligibilityResult> RecheckClaimedEligibilityAsync(Guid notificationId, string lockOwner, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PublicationNotificationCompletionResult> MarkDeliveredAsync(Guid notificationId, string lockOwner, int statusCode, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PublicationNotificationCompletionResult> ScheduleRetryAsync(Guid notificationId, string lockOwner, TimeSpan retryDelay, int? statusCode, string error, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PublicationNotificationCompletionResult> CompleteRetryableFailureAsync(Guid notificationId, string lockOwner, int maximumAttempts, TimeSpan retryDelay, int? statusCode, string error, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PublicationNotificationCompletionResult> MarkFailedAsync(Guid notificationId, string lockOwner, int? statusCode, string error, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PublicationNotificationCompletionResult> MarkSuppressedAsync(Guid notificationId, string lockOwner, string resolutionReason, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PublicationNotificationCompletionResult> MarkExpiredAsync(Guid notificationId, string lockOwner, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PublicationNotificationSuppressionResult> SuppressRequestNotificationsForSessionAsync(Guid providerRequestSessionId, DateTime suppressedUtc, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PublicationNotificationSuppressionResult> SuppressRequestNotificationsForMessageAsync(Guid requestMessageId, DateTime suppressedUtc, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PublicationNotificationSuppressionResult> SuppressResponseNotificationsForConsumerSessionAsync(Guid consumerRequestSessionId, DateTime suppressedUtc, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PublicationNotificationSuppressionResult> SuppressResponseNotificationsForRequestAsync(Guid requestMessageId, DateTime suppressedUtc, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PublicationNotificationSuppressionResult> SuppressResponseNotificationForResponseAsync(Guid responseMessageId, Guid consumerRequestSessionId, DateTime suppressedUtc, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PublicationNotificationQueryResult> GetPendingPublicationNotificationsAsync(int maxCount, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeSubscriptionTransport : ISubscriptionTransport
    {
        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task CreateSubscriptionAsync(
            string topicName,
            string subscriptionName,
            IReadOnlyList<string> consumerTopics,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task DeleteSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<TransportPublication?> ReadPublicationAsync(
            Guid sessionId,
            string topicName,
            string subscriptionName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<TransportPublication?>(null);
        }

        public Task RemovePublicationAsync(
            Guid sessionId,
            string topicName,
            string subscriptionName,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSubscriptionSessionRepository : ISubscriptionSessionRepository
    {
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
            return Task.FromResult(GetSubscriptionSessionResult.Failure(GetSubscriptionSessionFailureKind.NotFound, "missing"));
        }

        public Task<CloseSubscriptionSessionResult> CloseSubscriptionSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeConsumerSubscriptionSessionService : IConsumerSubscriptionSessionService
    {
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
            return Task.FromResult(CloseConsumerSubscriptionSessionResult.Success());
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

    private sealed class FakeConsumerRequestSessionRepository : IConsumerRequestSessionRepository
    {
        public Task<InsertConsumerRequestSessionResult> InsertConsumerRequestSessionAsync(
            ConsumerRequestSessionCreateData session,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<GetConsumerRequestSessionResult> GetConsumerRequestSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(GetConsumerRequestSessionResult.Failure(GetConsumerRequestSessionFailureKind.NotFound, "missing"));
        }

        public Task<CloseConsumerRequestSessionResult> CloseConsumerRequestSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeConsumerRequestRepository : IConsumerRequestRepository
    {
        public Task<ConsumerRequestRepositoryResult> InsertPostedRequestAsync(
            ConsumerRequestCreateData request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ConsumerRequestRepositoryResult> ExpireRequestAsync(
            Guid consumerSessionId,
            Guid channelId,
            Guid messageId,
            DateTime expiredUtc,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<RequestExpirationStateResult> IsRequestExpiredAsync(
            Guid messageId,
            Guid providerSessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(RequestExpirationStateResult.Success(false));
        }
    }

    private sealed class FakeProviderRequestSessionService : IProviderRequestSessionService
    {
        public Task<OpenProviderRequestSessionResult> OpenProviderRequestSessionAsync(
            string channelId,
            IReadOnlyList<string> topics,
            string? listenerUrl,
            string? filterExpressionsJson,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CloseProviderRequestSessionResult> CloseProviderRequestSessionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(CloseProviderRequestSessionResult.Success());
        }

        public Task<ReadRequestResult> ReadRequestAsync(string sessionId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<RemoveRequestResult> RemoveRequestAsync(string sessionId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PostResponseResult> PostResponseAsync(
            string sessionId,
            string requestMessageId,
            BinaryData messageContent,
            string? mediaType,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeConsumerRequestSessionService : IConsumerRequestSessionService
    {
        public Task<OpenConsumerRequestSessionResult> OpenConsumerRequestSessionAsync(
            string channelId,
            string? listenerUrl,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CloseConsumerRequestSessionServiceResult> CloseConsumerRequestSessionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(CloseConsumerRequestSessionServiceResult.Success());
        }

        public Task<PostRequestResult> PostRequestAsync(
            string sessionId,
            IReadOnlyList<string?>? topics,
            BinaryData messageContent,
            string? mediaType,
            string? expiry,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ExpireRequestResult> ExpireRequestAsync(
            string sessionId,
            string messageId,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ReadResponseResult> ReadResponseAsync(
            string sessionId,
            string requestMessageId,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<RemoveResponseResult> RemoveResponseAsync(
            string sessionId,
            string requestMessageId,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
