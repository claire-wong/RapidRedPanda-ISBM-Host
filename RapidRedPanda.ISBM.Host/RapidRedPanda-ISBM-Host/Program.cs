using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Extensions.Options;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddScoped<IChannelRepository, SqlChannelRepository>();
builder.Services.AddScoped<ISubscriptionSessionRepository, SqlSubscriptionSessionRepository>();
builder.Services.AddScoped<IProviderRequestSessionRepository, SqlProviderRequestSessionRepository>();
builder.Services.AddScoped<IProviderPublicationSessionRepository, SqlProviderPublicationSessionRepository>();
builder.Services.AddScoped<IProviderPublicationRepository, SqlProviderPublicationRepository>();
builder.Services.AddScoped<IPublicationNotificationRepository, SqlPublicationNotificationRepository>();
builder.Services.AddScoped<IConsumerRequestSessionRepository, SqlConsumerRequestSessionRepository>();
builder.Services.AddScoped<IConsumerRequestRepository, SqlConsumerRequestRepository>();
builder.Services.AddScoped<IRequestResponseRepository, SqlRequestResponseRepository>();
builder.Services.AddScoped<IChannelLifecycleService, ChannelLifecycleService>();
builder.Services.AddScoped<IConsumerSubscriptionSessionService, ConsumerSubscriptionSessionService>();
builder.Services.AddScoped<IProviderPublicationSessionService, ProviderPublicationSessionService>();
builder.Services.AddScoped<IProviderRequestSessionService, ProviderRequestSessionService>();
builder.Services.AddScoped<IConsumerRequestSessionService, ConsumerRequestSessionService>();
builder.Services.AddScoped<ISessionCloseService, SessionCloseService>();
builder.Services.AddScoped<IIsbmOperationAuthorizationService, IsbmOperationAuthorizationService>();
builder.Services.AddSingleton<IUsernameTokenParser, UsernameTokenParser>();
builder.Services.AddOptions<ConfigurationDiscoveryOptions>()
    .Bind(builder.Configuration.GetSection(ConfigurationDiscoveryOptions.SectionName))
    .Validate(ConfigurationDiscoveryOptions.IsValid, "ISBM Configuration Discovery settings are invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPublicationNotificationUriBuilder, PublicationNotificationUriBuilder>();
builder.Services.AddSingleton<IPublicationNotificationClient, PublicationNotificationClient>();
builder.Services
    .AddOptions<PublicationNotificationOptions>()
    .Bind(builder.Configuration.GetSection(PublicationNotificationOptions.SectionName))
    .Validate(
        PublicationNotificationOptions.IsValid,
        "PublicationNotifications configuration is invalid. Ensure positive batch/concurrency/retry values, MaximumDeliveryAgeMinutes >= 1, and LeaseDurationSeconds >= HttpTimeoutSeconds * 3.")
    .ValidateOnStart();
builder.Services.AddHttpClient(PublicationNotificationClient.HttpClientName, (serviceProvider, httpClient) =>
{
    PublicationNotificationOptions options = serviceProvider.GetRequiredService<IOptions<PublicationNotificationOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseCookies = false
});
builder.Services.AddHostedService<PublicationNotificationDispatcher>();
builder.Services
    .AddOptions<AzureServiceBusOptions>()
    .Bind(builder.Configuration.GetSection(AzureServiceBusOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ConnectionString),
        "AzureServiceBus:ConnectionString is required.")
    .ValidateOnStart();
builder.Services.AddSingleton(serviceProvider =>
{
    AzureServiceBusOptions options = serviceProvider.GetRequiredService<IOptions<AzureServiceBusOptions>>().Value;
    return new ServiceBusClient(options.ConnectionString);
});
builder.Services.AddSingleton(serviceProvider =>
{
    AzureServiceBusOptions options = serviceProvider.GetRequiredService<IOptions<AzureServiceBusOptions>>().Value;
    return new ServiceBusAdministrationClient(options.ConnectionString);
});
builder.Services.AddSingleton<ISubscriptionTransport, AzureServiceBusSubscriptionTransport>();
builder.Services.AddSingleton<IChannelTopicTransport, AzureServiceBusChannelTopicTransport>();
builder.Services.AddSingleton<IPublicationTransport, AzureServiceBusPublicationTransport>();
//builder.Services.Configure<RouteOptions>(options =>
//{
//    options.ConstraintMap["channelId"] = typeof(RegexRouteConstraint); // Add Regex routing support
//});

//// Enable handling encoded characters in route parameters
//builder.WebHost.ConfigureKestrel(options =>
//{
//    options.Limits.MaxRequestLineSize = 4096; // Optional: Increase limits for long paths
//    options.AddServerHeader = false;
//});

// Allow encoded slashes in route parameters
builder.WebHost.ConfigureKestrel(options =>
{
    options.AllowSynchronousIO = true;
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Add authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
