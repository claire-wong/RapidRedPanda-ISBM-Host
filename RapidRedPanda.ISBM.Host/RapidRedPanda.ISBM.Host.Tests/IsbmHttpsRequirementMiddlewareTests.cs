using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RapidRedPanda.ISBM.Host.Services;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class IsbmHttpsRequirementMiddlewareTests
{
    [Theory]
    [InlineData("/isbm/2.0", "GET")]
    [InlineData("/isbm/2.0/channels", "POST")]
    [InlineData("/isbm/2.0/sessions/example/publications", "POST")]
    [InlineData("/isbm/2.0/configuration/security-details", "GET")]
    [InlineData("/ISBM/2.0/channels", "DELETE")]
    public async Task TlsRequired_HttpIsbmRequest_IsRejectedBeforeNext(string path, string method)
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        DefaultHttpContext context = CreateContext(services, path, "http");
        context.Request.Method = method;
        bool nextCalled = false;
        IsbmHttpsRequirementMiddleware middleware = new(
            _ => { nextCalled = true; return Task.CompletedTask; },
            Options.Create(new ConfigurationDiscoveryOptions { IsTlsEnabled = true }));

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
        context.Response.Body.Position = 0;
        using JsonDocument json = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("HTTPS is required for ISBM requests.", json.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData(true, "https", "/isbm/2.0/channels")]
    [InlineData(false, "http", "/isbm/2.0/channels")]
    [InlineData(false, "https", "/isbm/2.0/channels")]
    [InlineData(true, "http", "/swagger")]
    [InlineData(true, "http", "/isbm/2.00/channels")]
    public async Task AllowedRequest_ContinuesWithoutChangingResponse(bool isTlsEnabled, string scheme, string path)
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        DefaultHttpContext context = CreateContext(services, path, scheme);
        bool nextCalled = false;
        IsbmHttpsRequirementMiddleware middleware = new(
            _ => { nextCalled = true; return Task.CompletedTask; },
            Options.Create(new ConfigurationDiscoveryOptions { IsTlsEnabled = isTlsEnabled }));

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task RawForwardedHttpsHeader_DoesNotBypassRequirement()
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        DefaultHttpContext context = CreateContext(services, "/isbm/2.0/channels", "http");
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        bool nextCalled = false;
        IsbmHttpsRequirementMiddleware middleware = new(
            _ => { nextCalled = true; return Task.CompletedTask; },
            Options.Create(new ConfigurationDiscoveryOptions { IsTlsEnabled = true }));

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task GuardBeforeHttpsRedirection_RejectsWithoutRedirectOrEndpointExecution()
    {
        ServiceCollection registrations = new();
        registrations.AddLogging();
        using ServiceProvider services = registrations.BuildServiceProvider();
        bool endpointCalled = false;
        HttpsRedirectionMiddleware redirection = new(
            _ => { endpointCalled = true; return Task.CompletedTask; },
            Options.Create(new HttpsRedirectionOptions { HttpsPort = 443 }),
            new ConfigurationBuilder().Build(),
            services.GetRequiredService<ILoggerFactory>());
        IsbmHttpsRequirementMiddleware guard = new(
            redirection.Invoke,
            Options.Create(new ConfigurationDiscoveryOptions { IsTlsEnabled = true }));
        DefaultHttpContext context = CreateContext(services, "/isbm/2.0/channels", "http");

        await guard.InvokeAsync(context);

        Assert.False(endpointCalled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    private static DefaultHttpContext CreateContext(IServiceProvider services, string path, string scheme)
    {
        DefaultHttpContext context = new();
        context.RequestServices = services;
        context.Request.Path = path;
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString("localhost");
        context.Response.Body = new MemoryStream();
        return context;
    }
}
