using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Controllers;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Services;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class ChannelManagementControllerTests
{
    private const string ComplexChannelUri = "/Robot Fleet/Ball Shooter/Drive System/ISO18435:D1.2/Request";
    private const string EncodedComplexChannelUri = "%2FRobot%20Fleet%2FBall%20Shooter%2FDrive%20System%2FISO18435%3AD1.2%2FRequest";

    [Fact]
    public async Task GetChannel_ExistingChannel_OmitsSecurityTokensFromResponse()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success())
        {
            GetChannelResult = GetChannelResult.Success(new ChannelDetailData(
                Guid.NewGuid(),
                ComplexChannelUri,
                "Request",
                "description",
                "isbm-ch-test",
                [new ChannelSecurityTokenData("UsernameToken", "Tester21")]))
        };
        ChannelManagementServicelController controller = CreateController(repository);

        IActionResult result = await controller.GetChannel(EncodedComplexChannelUri, CancellationToken.None);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        ChannelResponse response = Assert.IsType<ChannelResponse>(ok.Value);
        Assert.Equal(ComplexChannelUri, response.Uri);
        Assert.Equal("Request", response.ChannelType);
        Assert.Equal("description", response.Description);
        string json = System.Text.Json.JsonSerializer.Serialize(response);
        Assert.DoesNotContain("securityTokens", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Tester21", json, StringComparison.Ordinal);
        Assert.Equal(ComplexChannelUri, repository.GetChannelUri);
    }

    [Fact]
    public async Task GetChannel_UnknownChannel_ReturnsChannelFault()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success())
        {
            GetChannelResult = GetChannelResult.Failure(GetChannelFailureKind.NotFound, "Not found.")
        };
        ChannelManagementServicelController controller = CreateController(repository);

        IActionResult result = await controller.GetChannel("%2Fmissing", CancellationToken.None);

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(notFound.Value);
        Assert.Equal("ChannelFault", fault.Fault);
    }

    [Fact]
    public async Task GetChannel_InvalidRouteValue_ReturnsParameterFault()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);

        IActionResult result = await controller.GetChannel("%20%2Ftest1", CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(badRequest.Value);
        Assert.Equal("ParameterFault", fault.Fault);
        Assert.Null(repository.GetChannelUri);
    }

    [Fact]
    public async Task GetChannel_DoesNotRepeatedlyDecodeRouteParameter()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success())
        {
            GetChannelResult = GetChannelResult.Success(new ChannelDetailData(
                Guid.NewGuid(),
                "%2Ftest1",
                "Publication",
                null,
                "isbm-ch-test",
                []))
        };
        ChannelManagementServicelController controller = CreateController(repository);

        IActionResult result = await controller.GetChannel("%252Ftest1", CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("%2Ftest1", repository.GetChannelUri);
    }

    [Fact]
    public async Task DeleteChannel_DecodesRouteParameterExactlyOnceBeforeRepositoryLookup()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);

        IActionResult result = await controller.DeleteChannel("%2Ftest1", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("/test1", repository.DeletedChannelUri);
    }

    [Fact]
    public async Task DeleteChannel_DoesNotRepeatedlyDecodeRouteParameter()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);

        IActionResult result = await controller.DeleteChannel("%252Ftest1", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("%2Ftest1", repository.DeletedChannelUri);
    }

    [Theory]
    [InlineData("%20%2Ftest1")]
    [InlineData("%2Ftest1%20")]
    [InlineData("%2Ftest%0A1")]
    public async Task DeleteChannel_InvalidDecodedChannelUri_ReturnsParameterFaultAndDoesNotCallRepository(string encodedChannelUri)
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);

        IActionResult result = await controller.DeleteChannel(encodedChannelUri, CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(badRequest.Value);
        Assert.Equal("ParameterFault", fault.Fault);
        Assert.Null(repository.DeletedChannelUri);
    }

    [Theory]
    [InlineData("/channel/123/test")]
    [InlineData("/path+with+spaces/Some#Page?query=value+with&special=characters")]
    [InlineData("/Robot Fleet/Ball Shooter/Drive System/ISO18435:D1.2/Request")]
    [InlineData("test123 mychannel")]
    public async Task CreateChannel_ApprovedChannelUriExamples_ArePassedToRepositoryUnchanged(string channelUri)
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, $$"""
            {
              "uri": "{{channelUri}}",
              "channelType": "Publication",
              "description": ""
            }
            """);

        IActionResult result = await controller.CreateChannel(CancellationToken.None);

        Assert.IsType<ObjectResult>(result);
        Assert.Equal(channelUri, repository.CreatedChannelUri);
    }

    [Fact]
    public async Task CreateChannel_MissingSecurityTokenType_DefaultsToUsernameToken()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, """
            {
              "uri": "/compat-test",
              "channelType": "Publication",
              "description": "",
              "securityTokens": [
                {
                  "username": "Tester",
                  "password": "12345"
                }
              ]
            }
            """);

        IActionResult result = await controller.CreateChannel(CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal("/compat-test", repository.CreatedChannelUri);
        ChannelCreateToken token = Assert.Single(repository.CreatedTokens);
        Assert.Equal("Tester", token.Username);
        Assert.Equal("TESTER", token.NormalizedUsername);
        Assert.Equal("12345", token.Password);
    }

    [Fact]
    public async Task CreateChannel_DuplicateAndDistinctTokens_PersistsDistinctSet()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, """
            {
              "uri": "/dedupe-test",
              "channelType": "Publication",
              "securityTokens": [
                { "username": "Alice", "password": "same" },
                { "username": " alice ", "password": "same" },
                { "username": "Bob", "password": "different" }
              ]
            }
            """);

        IActionResult result = await controller.CreateChannel(CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal(2, repository.CreatedTokens.Count);
        Assert.Equal(["ALICE", "BOB"], repository.CreatedTokens.Select(token => token.NormalizedUsername));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" /test1")]
    [InlineData("/test1 ")]
    [InlineData("/test\n1")]
    public async Task CreateChannel_InvalidChannelUri_ReturnsParameterFaultAndDoesNotCallRepository(string channelUri)
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, $$"""
            {
              "uri": {{JsonString(channelUri)}},
              "channelType": "Publication",
              "description": ""
            }
            """);

        IActionResult result = await controller.CreateChannel(CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(badRequest.Value);
        Assert.Equal("ParameterFault", fault.Fault);
        Assert.Null(repository.CreatedChannelUri);
    }

    [Fact]
    public async Task AddSecurityTokens_FlatUsernameToken_PassesParsedTokenToRepository()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, """
            {
              "securityTokens": [
                {
                  "type": "UsernameToken",
                  "username": " Tester21 ",
                  "password": "fake-password"
                }
              ]
            }
            """);

        IActionResult result = await controller.AddSecurityTokens(EncodedComplexChannelUri, CancellationToken.None);

        StatusCodeResult created = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(ComplexChannelUri, repository.AddTokensChannelUri);
        ChannelCreateToken token = Assert.Single(repository.AddedTokens);
        Assert.Equal("Tester21", token.Username);
        Assert.Equal("TESTER21", token.NormalizedUsername);
    }

    [Fact]
    public async Task AddSecurityTokens_MissingType_DefaultsToUsernameToken()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, OmittedTypeTokenBody());

        IActionResult result = await controller.AddSecurityTokens(EncodedComplexChannelUri, CancellationToken.None);

        StatusCodeResult created = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        ChannelCreateToken token = Assert.Single(repository.AddedTokens);
        Assert.Equal("Tester", token.Username);
        Assert.Equal("TESTER", token.NormalizedUsername);
        Assert.Equal("12345", token.Password);
    }

    [Fact]
    public async Task AddSecurityTokens_NestedUsernameToken_PassesParsedTokenToRepository()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, """
            {
              "securityTokens": [
                {
                  "UsernameToken": {
                    "username": "Tester21",
                    "password": "fake-password"
                  }
                }
              ]
            }
            """);

        IActionResult result = await controller.AddSecurityTokens("%2Ftest1", CancellationToken.None);

        StatusCodeResult created = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Single(repository.AddedTokens);
    }

    [Fact]
    public async Task AddSecurityTokens_MultipleTokens_PassesAllTokensToRepository()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, """
            {
              "securityTokens": [
                {
                  "type": "UsernameToken",
                  "username": "Tester21",
                  "password": "fake-password-1"
                },
                {
                  "type": "UsernameToken",
                  "username": "Tester22",
                  "password": "fake-password-2"
                }
              ]
            }
            """);

        IActionResult result = await controller.AddSecurityTokens("%2Ftest1", CancellationToken.None);

        StatusCodeResult created = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(2, repository.AddedTokens.Count);
    }

    [Fact]
    public async Task AddSecurityTokens_UnknownChannel_ReturnsChannelFault()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success())
        {
            AddTokensResult = SecurityTokenMutationResult.Failure(SecurityTokenMutationFailureKind.NotFound, "Not found.")
        };
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, ValidFlatTokenBody());

        IActionResult result = await controller.AddSecurityTokens("%2Fmissing", CancellationToken.None);

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(notFound.Value);
        Assert.Equal("ChannelFault", fault.Fault);
    }

    [Fact]
    public async Task AddSecurityTokens_OpenChannel_ReturnsOperationFault()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success())
        {
            AddTokensResult = SecurityTokenMutationResult.Failure(SecurityTokenMutationFailureKind.OpenChannel, "Open channel.")
        };
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, ValidFlatTokenBody());

        IActionResult result = await controller.AddSecurityTokens("%2Fopen", CancellationToken.None);

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("OperationFault", Assert.IsType<FaultResponse>(conflict.Value).Fault);
    }

    [Theory]
    [InlineData("""{"securityTokens":[{"type":"OtherToken","username":"Tester21","password":"fake-password"}]}""")]
    [InlineData("""{"securityTokens":[{"type":"UsernameToken","password":"fake-password"}]}""")]
    [InlineData("""{"securityTokens":[]}""")]
    public async Task AddSecurityTokens_InvalidTokenInput_ReturnsSecurityTokenFaultAndDoesNotCallRepository(string body)
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, body);

        IActionResult result = await controller.AddSecurityTokens("%2Ftest1", CancellationToken.None);

        ObjectResult faultResult = Assert.IsType<ConflictObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(faultResult.Value);
        Assert.Equal("SecurityTokenFault", fault.Fault);
        Assert.Null(repository.AddTokensChannelUri);
    }

    [Fact]
    public async Task AddSecurityTokens_InvalidTokenInMultiTokenRequest_DoesNotCallRepository()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, """
            {
              "securityTokens": [
                {
                  "type": "UsernameToken",
                  "username": "Tester21",
                  "password": "fake-password"
                },
                {
                  "type": "OtherToken",
                  "username": "Tester22",
                  "password": "fake-password"
                }
              ]
            }
            """);

        IActionResult result = await controller.AddSecurityTokens("%2Ftest1", CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Null(repository.AddTokensChannelUri);
    }

    [Fact]
    public async Task RemoveSecurityTokens_FlatUsernameToken_PassesParsedTokenToRepository()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, ValidFlatTokenBody(password: "wrong-but-ignored-for-identity"));

        IActionResult result = await controller.RemoveSecurityTokens(EncodedComplexChannelUri, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(ComplexChannelUri, repository.RemoveTokensChannelUri);
        ChannelCreateToken token = Assert.Single(repository.RemovedTokens);
        Assert.Equal("Tester21", token.Username);
        Assert.Equal("TESTER21", token.NormalizedUsername);
        Assert.Equal("wrong-but-ignored-for-identity", token.Password);
    }

    [Fact]
    public async Task RemoveSecurityTokens_MissingType_DefaultsToUsernameToken()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, OmittedTypeTokenBody());

        IActionResult result = await controller.RemoveSecurityTokens(EncodedComplexChannelUri, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        ChannelCreateToken token = Assert.Single(repository.RemovedTokens);
        Assert.Equal("Tester", token.Username);
        Assert.Equal("TESTER", token.NormalizedUsername);
        Assert.Equal("12345", token.Password);
    }

    [Fact]
    public async Task RemoveSecurityTokens_NestedUsernameToken_PassesParsedTokenToRepository()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, """
            {
              "securityTokens": [
                {
                  "UsernameToken": {
                    "username": "Tester21",
                    "password": "fake-password"
                  }
                }
              ]
            }
            """);

        IActionResult result = await controller.RemoveSecurityTokens("%2Ftest1", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Single(repository.RemovedTokens);
    }

    [Fact]
    public async Task RemoveSecurityTokens_MultipleTokens_PassesAllTokensToRepository()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, """
            {
              "securityTokens": [
                {
                  "type": "UsernameToken",
                  "username": "Tester21",
                  "password": "fake-password-1"
                },
                {
                  "type": "UsernameToken",
                  "username": "Tester22",
                  "password": "fake-password-2"
                }
              ]
            }
            """);

        IActionResult result = await controller.RemoveSecurityTokens("%2Ftest1", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(2, repository.RemovedTokens.Count);
    }

    [Fact]
    public async Task RemoveSecurityTokens_AbsentToken_ReturnsSecurityTokenFault()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success())
        {
            RemoveTokensResult = SecurityTokenMutationResult.Failure(
                SecurityTokenMutationFailureKind.SecurityTokenNotFound,
                "Missing token.")
        };
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, ValidFlatTokenBody());

        IActionResult result = await controller.RemoveSecurityTokens("%2Ftest1", CancellationToken.None);

        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("SecurityTokenFault", Assert.IsType<FaultResponse>(conflict.Value).Fault);
    }

    [Fact]
    public async Task RemoveSecurityTokens_UnknownChannel_ReturnsChannelFault()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success())
        {
            RemoveTokensResult = SecurityTokenMutationResult.Failure(SecurityTokenMutationFailureKind.NotFound, "Not found.")
        };
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, ValidFlatTokenBody());

        IActionResult result = await controller.RemoveSecurityTokens("%2Fmissing", CancellationToken.None);

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(notFound.Value);
        Assert.Equal("ChannelFault", fault.Fault);
    }

    [Fact]
    public async Task RemoveSecurityTokens_InvalidTokenInMultiTokenRequest_DoesNotCallRepository()
    {
        CapturingChannelRepository repository = new(DeleteChannelResult.Success());
        ChannelManagementServicelController controller = CreateController(repository);
        SetJsonBody(controller, """
            {
              "securityTokens": [
                {
                  "type": "UsernameToken",
                  "username": "Tester21",
                  "password": "fake-password"
                },
                {
                  "type": "OtherToken",
                  "username": "Tester22",
                  "password": "fake-password"
                }
              ]
            }
            """);

        IActionResult result = await controller.RemoveSecurityTokens("%2Ftest1", CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Null(repository.RemoveTokensChannelUri);
    }

    private static ChannelManagementServicelController CreateController(IChannelRepository repository)
    {
        ChannelManagementServicelController controller = new(
            repository,
            new ChannelLifecycleService(
                repository,
                new CapturingChannelTopicTransport(),
                NullLogger<ChannelLifecycleService>.Instance),
            new UsernameTokenParser(),
            NullLogger<ChannelManagementServicelController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static void SetJsonBody(Controller controller, string json)
    {
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    private static string JsonString(string value)
    {
        return System.Text.Json.JsonSerializer.Serialize(value);
    }

    private static string ValidFlatTokenBody(string password = "fake-password")
    {
        return $$"""
            {
              "securityTokens": [
                {
                  "type": "UsernameToken",
                  "username": "Tester21",
                  "password": "{{password}}"
                }
              ]
            }
            """;
    }

    private static string OmittedTypeTokenBody()
    {
        return """
            {
              "securityTokens": [
                {
                  "username": "Tester",
                  "password": "12345"
                }
              ]
            }
            """;
    }

    private sealed class CapturingChannelRepository(DeleteChannelResult deleteResult) : IChannelRepository
    {
        public string? DeletedChannelUri { get; private set; }
        public string? CreatedChannelUri { get; private set; }
        public string? GetChannelUri { get; private set; }
        public string? AddTokensChannelUri { get; private set; }
        public string? RemoveTokensChannelUri { get; private set; }
        public IReadOnlyList<ChannelCreateToken> CreatedTokens { get; private set; } = [];
        public IReadOnlyList<ChannelCreateToken> AddedTokens { get; private set; } = [];
        public IReadOnlyList<ChannelCreateToken> RemovedTokens { get; private set; } = [];
        public GetChannelResult GetChannelResult { get; init; } = GetChannelResult.Failure(GetChannelFailureKind.NotFound, "Not found.");
        public SecurityTokenMutationResult AddTokensResult { get; init; } = SecurityTokenMutationResult.Success();
        public SecurityTokenMutationResult RemoveTokensResult { get; init; } = SecurityTokenMutationResult.Success();

        public Task<IReadOnlyList<ChannelData>> GetChannelsAsync(ChannelCredentials? credentials, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ChannelData>>([]);
        }

        public Task<GetChannelResult> GetChannelAsync(string channelUri, CancellationToken cancellationToken)
        {
            GetChannelUri = channelUri;
            return Task.FromResult(GetChannelResult);
        }

        public Task<GetChannelTopicBindingResult> GetChannelTopicBindingAsync(string channelUri, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelTopicBindingResult.Success(new ChannelTopicBindingData(
                Guid.NewGuid(),
                channelUri,
                "Publication",
                "isbm-ch-test")));
        }

        public Task<GetChannelTopicBindingResult> GetChannelTopicBindingByIdAsync(Guid channelId, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelTopicBindingResult.Failure(
                GetChannelTopicBindingFailureKind.NotFound,
                "Not found."));
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForConsumerPublicationSubscriptionAsync(
            string channelUri,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelForSubscriptionResult.Failure(
                GetChannelForSubscriptionFailureKind.NotFound,
                "Not found."));
        }

        public Task<GetChannelForSubscriptionResult> GetChannelForProviderPublicationSessionAsync(
            string channelUri,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(GetChannelForSubscriptionResult.Failure(
                GetChannelForSubscriptionFailureKind.NotFound,
                "Not found."));
        }

        public Task<CreateChannelResult> CreateChannelAsync(ChannelCreateRequestData request, CancellationToken cancellationToken)
        {
            CreatedChannelUri = request.ChannelUri;
            CreatedTokens = request.SecurityTokens;
            ChannelData channel = new(request.ChannelId, request.ChannelUri, request.ChannelType, request.Description, request.AzureTopicName);
            return Task.FromResult(CreateChannelResult.Success(channel));
        }

        public Task<DeleteChannelResult> DeleteChannelAsync(string channelUri, CancellationToken cancellationToken)
        {
            DeletedChannelUri = channelUri;
            return Task.FromResult(deleteResult);
        }

        public Task<SecurityTokenMutationResult> AddSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken)
        {
            AddTokensChannelUri = channelUri;
            AddedTokens = tokens;
            return Task.FromResult(AddTokensResult);
        }

        public Task<SecurityTokenMutationResult> RemoveSecurityTokensAsync(string channelUri, IReadOnlyList<ChannelCreateToken> tokens, CancellationToken cancellationToken)
        {
            RemoveTokensChannelUri = channelUri;
            RemovedTokens = tokens;
            return Task.FromResult(RemoveTokensResult);
        }
    }

    private sealed class CapturingChannelTopicTransport : IChannelTopicTransport
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
}
