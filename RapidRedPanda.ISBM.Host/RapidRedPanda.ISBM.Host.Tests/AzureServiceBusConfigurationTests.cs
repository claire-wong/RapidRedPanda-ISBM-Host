using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Services;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class AzureServiceBusConfigurationTests
{
    [Fact]
    public void AzureServiceBusOptions_BindsConnectionStringFromExpectedSection()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureServiceBus:ConnectionString"] = "UseDevelopmentEmulator=true"
            })
            .Build();

        AzureServiceBusOptions options = new();
        configuration.GetSection(AzureServiceBusOptions.SectionName).Bind(options);

        Assert.Equal("UseDevelopmentEmulator=true", options.ConnectionString);
    }

    [Fact]
    public void AzureServiceBusOptions_MissingConnectionString_FailsWithClearConfigurationKey()
    {
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder().Build();

        services
            .AddOptions<AzureServiceBusOptions>()
            .Bind(configuration.GetSection(AzureServiceBusOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                "AzureServiceBus:ConnectionString is required.");

        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() =>
            serviceProvider.GetRequiredService<IOptions<AzureServiceBusOptions>>().Value);

        Assert.Contains("AzureServiceBus:ConnectionString is required.", exception.Message);
        Assert.DoesNotContain("SharedAccessKey", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FakeTransportTestPath_RequiresNoAzureClientsOrAzureCredentials()
    {
        ServiceCollection services = new();
        services.AddSingleton<IChannelRepository, FakeChannelRepository>();
        services.AddSingleton<ISubscriptionSessionRepository, FakeSubscriptionSessionRepository>();
        services.AddSingleton<ISubscriptionTransport, FakeSubscriptionTransport>();
        services.AddSingleton<IChannelTopicTransport, FakeChannelTopicTransport>();
        services.AddSingleton<IConsumerSubscriptionSessionService, ConsumerSubscriptionSessionService>();
        services.AddLogging();

        using ServiceProvider serviceProvider = services.BuildServiceProvider(validateScopes: true);

        Assert.IsType<FakeSubscriptionTransport>(serviceProvider.GetRequiredService<ISubscriptionTransport>());
        Assert.IsType<FakeChannelTopicTransport>(serviceProvider.GetRequiredService<IChannelTopicTransport>());
        Assert.IsType<ConsumerSubscriptionSessionService>(serviceProvider.GetRequiredService<IConsumerSubscriptionSessionService>());
        Assert.Empty(serviceProvider.GetServices<ServiceBusClient>());
        Assert.Empty(serviceProvider.GetServices<ServiceBusAdministrationClient>());
    }

    [Fact]
    public async Task Program_UsesAzureServiceBusOptionsSectionAndSingletonAzureClients()
    {
        string program = await ReadHostFileAsync("Program.cs");

        Assert.Contains("AzureServiceBusOptions.SectionName", program, StringComparison.Ordinal);
        Assert.Contains("IOptions<AzureServiceBusOptions>", program, StringComparison.Ordinal);
        Assert.Contains("AddSingleton(serviceProvider =>", program, StringComparison.Ordinal);
        Assert.Contains("new ServiceBusClient(options.ConnectionString)", program, StringComparison.Ordinal);
        Assert.Contains("new ServiceBusAdministrationClient(options.ConnectionString)", program, StringComparison.Ordinal);
        Assert.DoesNotContain("GetConnectionString(\"AzureServiceBus\")", program, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceCode_DoesNotHardCodeAzureServiceBusConnectionString()
    {
        string hostRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "RapidRedPanda-ISBM-Host"));

        string testsRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            ".."));

        IEnumerable<string> sourceFiles = Directory.GetFiles(hostRoot, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(testsRoot, "*.cs", SearchOption.TopDirectoryOnly));

        string source = string.Join(Environment.NewLine, await Task.WhenAll(sourceFiles.Select(path => File.ReadAllTextAsync(path))));

        string endpointConnectionStringPrefix = "Endpoint=sb" + "://";
        string sharedAccessKeySegment = "SharedAccessKey" + "=";

        Assert.DoesNotContain(endpointConnectionStringPrefix, source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(sharedAccessKeySegment, source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HostProject_CopiesSqlMigrationsToBuildAndPublishOutput()
    {
        string project = await ReadHostFileAsync("RapidRedPanda.ISBM.Host.csproj");

        Assert.Contains("..\\Database\\Scripts\\**\\*.sql", project, StringComparison.Ordinal);
        Assert.Contains("CopyToOutputDirectory=\"PreserveNewest\"", project, StringComparison.Ordinal);
        Assert.Contains("CopyToPublishDirectory=\"PreserveNewest\"", project, StringComparison.Ordinal);
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

    private sealed class FakeChannelRepository : IChannelRepository
    {
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
            return Task.FromResult(GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.NotFound, "missing"));
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForProviderPublicationSessionAsync(
            string channelUri,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelForSubscriptionResult.Failure(GetChannelForSubscriptionFailureKind.NotFound, "missing"));
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

    private sealed class FakeSubscriptionSessionRepository : ISubscriptionSessionRepository
    {
        public Task<InsertSubscriptionSessionResult> InsertConsumerSubscriptionSessionAsync(
            ConsumerSubscriptionSessionCreateData session,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<GetSubscriptionSessionResult> GetSubscriptionSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CloseSubscriptionSessionResult> CloseSubscriptionSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeSubscriptionTransport : ISubscriptionTransport
    {
        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task CreateSubscriptionAsync(
            string topicName,
            string subscriptionName,
            IReadOnlyList<string> consumerTopics,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DeleteSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TransportPublication?> ReadPublicationAsync(
            Guid sessionId,
            string topicName,
            string subscriptionName,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task RemovePublicationAsync(
            Guid sessionId,
            string topicName,
            string subscriptionName,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeChannelTopicTransport : IChannelTopicTransport
    {
        public Task CreateTopicAsync(string topicName, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<bool> TopicExistsAsync(string topicName, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DeleteTopicAsync(string topicName, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
