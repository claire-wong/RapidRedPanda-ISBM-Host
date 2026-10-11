using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace RapidRedPanda.ISBM.Host.Services;

public sealed class IsbmHttpsRequirementMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ConfigurationDiscoveryOptions _options;

    public IsbmHttpsRequirementMiddleware(
        RequestDelegate next,
        IOptions<ConfigurationDiscoveryOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_options.IsTlsEnabled &&
            context.Request.Path.StartsWithSegments("/isbm/2.0", StringComparison.OrdinalIgnoreCase) &&
            !context.Request.IsHttps)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "HTTPS is required for ISBM requests."
                },
                options: null,
                contentType: "application/problem+json",
                cancellationToken: context.RequestAborted);
            return;
        }

        await _next(context);
    }
}
