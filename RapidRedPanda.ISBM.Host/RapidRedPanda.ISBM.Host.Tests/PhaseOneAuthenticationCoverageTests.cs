using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Controllers;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Services;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class PhaseOneAuthenticationCoverageTests
{
    [Fact]
    public void BasicCredentialReader_ValidHeader_NormalizesUsernameWithoutExposingPassword()
    {
        DefaultHttpContext context = new();
        context.Request.Headers.Authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(" Alice :secret"));

        ChannelCredentials credentials = Assert.IsType<ChannelCredentials>(
            BasicAuthenticationCredentials.Read(context.Request, new UsernameTokenParser()));

        Assert.Equal("ALICE", credentials.NormalizedUsername);
        Assert.Equal("secret", credentials.Password);
    }

    [Fact]
    public async Task SharedClose_InvalidCredentials_ReturnsSessionFaultWithoutClosing()
    {
        CapturingCloseService close = new();
        SessionServiceController controller = new(
            close,
            NullLogger<SessionServiceController>.Instance,
            new UsernameTokenParser(),
            new DenyingAuthorizationService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        IActionResult result = await controller.CloseSession(Guid.NewGuid().ToString(), CancellationToken.None);

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("SessionFault", Assert.IsType<FaultResponse>(notFound.Value).Fault);
        Assert.False(close.Called);
    }

    [Theory]
    [InlineData("ProviderPublicationServiceController.cs", "PostPublication", "ExpirePublication")]
    [InlineData("ConsumerPublicationServiceController.cs", "ReadPublication", "RemovePublication")]
    [InlineData("ProviderRequestServiceController.cs", "ReadRequest", "RemoveRequest", "PostResponse")]
    [InlineData("ConsumerRequestServiceController.cs", "PostRequest", "ExpireRequest", "ReadResponse", "RemoveResponse")]
    public async Task ProtectedServiceOperations_InvokeSharedSessionAuthorization(string file, params string[] operations)
    {
        string source = await File.ReadAllTextAsync(FindHostFile(file));
        foreach (string operation in operations)
        {
            int method = source.IndexOf($" {operation}(", StringComparison.Ordinal);
            Assert.True(method >= 0, $"Operation {operation} was not found.");
            int nextMethod = source.IndexOf("public async Task<IActionResult>", method + 1, StringComparison.Ordinal);
            string body = nextMethod < 0 ? source[method..] : source[method..nextMethod];
            Assert.Contains("AuthorizeSessionAsync(sessionId", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ChannelManagementProtectedOperations_InvokeSharedChannelAuthorization()
    {
        string source = await File.ReadAllTextAsync(FindHostFile("ChannelManagementServicelController.cs"));
        Assert.Equal(4, Count(source, "AuthorizeChannelAsync(decodedChannelUri"));
    }

    private static string FindHostFile(string file)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "RapidRedPanda-ISBM-Host", "Controllers", file);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(file);
    }

    private static int Count(string value, string token) =>
        (value.Length - value.Replace(token, string.Empty, StringComparison.Ordinal).Length) / token.Length;

    private sealed class CapturingCloseService : ISessionCloseService
    {
        public bool Called { get; private set; }
        public Task<CloseSessionResult> CloseSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(CloseSessionResult.Success());
        }
    }

    private sealed class DenyingAuthorizationService : IIsbmOperationAuthorizationService
    {
        public Task<IsbmAuthorizationOutcome> AuthorizeChannelAsync(string channelUri, ChannelCredentials? credentials, CancellationToken cancellationToken) =>
            Task.FromResult(IsbmAuthorizationOutcome.NotFoundOrUnauthorized);
        public Task<IsbmAuthorizationOutcome> AuthorizeSessionAsync(string sessionId, IsbmSessionKind kind, ChannelCredentials? credentials, CancellationToken cancellationToken) =>
            Task.FromResult(IsbmAuthorizationOutcome.NotFoundOrUnauthorized);
        public Task<IsbmAuthorizationOutcome> AuthorizeAnySessionAsync(string sessionId, ChannelCredentials? credentials, CancellationToken cancellationToken) =>
            Task.FromResult(IsbmAuthorizationOutcome.NotFoundOrUnauthorized);
    }
}
