using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Controllers;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Messaging;
using RapidRedPanda.ISBM.Host.Services;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class FilterExpressionsTests
{
    private const string ValidFilterExpressionsJson = """
        [
          {
            "applicableMediaTypes": ["application/json"],
            "expressionString": {
              "expression": "$.asset",
              "language": "JsonPath"
            },
            "namespaces": [
              {
                "prefix": "x",
                "name": "urn:example"
              }
            ]
          }
        ]
        """;

    public static IEnumerable<object[]> ValidFilterExpressionBodies()
    {
        yield return ["""{"topics":["Topic-A"]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":null}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":{"expression":"$.asset","language":"JsonPath"}}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":{"expression":"$.asset","language":"UnsupportedLanguage"}}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":{"expression":"","language":"JsonPath"}}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":{"expression":"","language":"UnsupportedLanguage"}}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":{"expression":"$.asset","language":"JsonPath","languageVersion":"1.0"}}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"applicableMediaTypes":[],"expressionString":{"expression":"$.asset","language":"JsonPath"}}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"applicableMediaTypes":["application/json"],"expressionString":{"expression":"$.asset","language":"JsonPath"}}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":{"expression":"$.asset","language":"JsonPath"},"namespaces":[]}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":{"expression":"$.asset","language":"JsonPath"},"namespaces":[{"prefix":"x","name":"urn:example"}]}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":{"expression":"$.asset","language":"JsonPath"},"namespaces":[{"prefix":"x","name":"urn:example"},{"prefix":"x","name":"urn:example"}]}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":{"expression":"$.asset","language":"JsonPath"}},{"applicableMediaTypes":["application/xml"],"expressionString":{"expression":"/x:asset","language":"XPath","languageVersion":""},"namespaces":[{"prefix":"x","name":"urn:example"}]}]}"""];
    }

    public static IEnumerable<object[]> InvalidFilterExpressionBodies()
    {
        yield return ["""{"topics":["Topic-A"],"filterExpressions":{}}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[null]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[1]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"applicableMediaTypes":[{"mediaType":"application/json"}],"expressionString":{"expression":"$.asset","language":"JsonPath"}}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":null}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":{"language":"JsonPath"}}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":{"expression":" ","language":"JsonPath"}}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":{"expression":"$.asset"}}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":{"expression":"$.asset","language":" "}}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"applicableMediaTypes":[" "],"expressionString":{"expression":"$.asset","language":"JsonPath"}}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":{"expression":"$.asset","language":"JsonPath"},"namespaces":[null]}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":{"expression":"$.asset","language":"JsonPath"},"namespaces":[{"prefix":" ","name":"urn:example"}]}]}"""];
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":{"expression":"$.asset","language":"JsonPath"},"namespaces":[{"prefix":"x","name":" "}]}]}"""];
    }

    public static IEnumerable<object[]> NamespaceConflictFilterExpressionBodies()
    {
        yield return ["""{"topics":["Topic-A"],"filterExpressions":[{"expressionString":{"expression":"$.asset","language":"JsonPath"},"namespaces":[{"prefix":"x","name":"urn:one"},{"prefix":"x","name":"urn:two"}]}]}"""];
    }

    [Theory]
    [MemberData(nameof(ValidFilterExpressionBodies))]
    public async Task OpenSubscriptionSession_ValidFilterExpressions_AcceptsAndPassesCanonicalJson(string body)
    {
        CapturingSubscriptionSessionService service = new();
        ConsumerPublicationServiceController controller = CreateConsumerPublicationController(service);
        SetJsonBody(controller, body);

        IActionResult result = await controller.OpenSubscriptionSession("%2Fpublication%2Fchannel", CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(["Topic-A"], service.OpenedTopics);
        AssertJsonEquivalent(ExpectedStoredFilters(body), service.OpenedFilterExpressionsJson);
    }

    [Theory]
    [MemberData(nameof(InvalidFilterExpressionBodies))]
    public async Task OpenSubscriptionSession_InvalidFilterExpressions_ReturnsParameterFault(string body)
    {
        CapturingSubscriptionSessionService service = new();
        ConsumerPublicationServiceController controller = CreateConsumerPublicationController(service);
        SetJsonBody(controller, body);

        IActionResult result = await controller.OpenSubscriptionSession("%2Fpublication%2Fchannel", CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(badRequest.Value);
        Assert.Equal("ParameterFault", fault.Fault);
        Assert.Null(service.OpenedTopics);
    }

    [Theory]
    [MemberData(nameof(ValidFilterExpressionBodies))]
    public async Task OpenProviderRequestSession_ValidFilterExpressions_AcceptsAndPassesCanonicalJson(string body)
    {
        CapturingProviderRequestSessionService service = new();
        ProviderRequestServiceController controller = CreateProviderRequestController(service);
        SetJsonBody(controller, body);

        IActionResult result = await controller.OpenProviderRequestSession("%2Frequest%2Fchannel", CancellationToken.None);

        ObjectResult created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(["Topic-A"], service.OpenedTopics);
        AssertJsonEquivalent(ExpectedStoredFilters(body), service.OpenedFilterExpressionsJson);
    }

    [Theory]
    [MemberData(nameof(InvalidFilterExpressionBodies))]
    public async Task OpenProviderRequestSession_InvalidFilterExpressions_ReturnsParameterFault(string body)
    {
        CapturingProviderRequestSessionService service = new();
        ProviderRequestServiceController controller = CreateProviderRequestController(service);
        SetJsonBody(controller, body);

        IActionResult result = await controller.OpenProviderRequestSession("%2Frequest%2Fchannel", CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(badRequest.Value);
        Assert.Equal("ParameterFault", fault.Fault);
        Assert.Null(service.OpenedTopics);
    }

    [Theory]
    [MemberData(nameof(NamespaceConflictFilterExpressionBodies))]
    public async Task OpenSubscriptionSession_NamespaceConflict_ReturnsNamespaceFault(string body)
    {
        CapturingSubscriptionSessionService service = new();
        ConsumerPublicationServiceController controller = CreateConsumerPublicationController(service);
        SetJsonBody(controller, body);

        IActionResult result = await controller.OpenSubscriptionSession("%2Fpublication%2Fchannel", CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(badRequest.Value);
        Assert.Equal("NamespaceFault", fault.Fault);
        Assert.Null(service.OpenedTopics);
    }

    [Theory]
    [MemberData(nameof(NamespaceConflictFilterExpressionBodies))]
    public async Task OpenProviderRequestSession_NamespaceConflict_ReturnsNamespaceFault(string body)
    {
        CapturingProviderRequestSessionService service = new();
        ProviderRequestServiceController controller = CreateProviderRequestController(service);
        SetJsonBody(controller, body);

        IActionResult result = await controller.OpenProviderRequestSession("%2Frequest%2Fchannel", CancellationToken.None);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        FaultResponse fault = Assert.IsType<FaultResponse>(badRequest.Value);
        Assert.Equal("NamespaceFault", fault.Fault);
        Assert.Null(service.OpenedTopics);
    }

    [Fact]
    public void FilterExpressionJson_SerializesStandardShapeAndRejectsObjectMediaTypes()
    {
        using JsonDocument validDocument = JsonDocument.Parse($$"""{"filterExpressions":{{ValidFilterExpressionsJson}}}""");
        OpenSubscriptionSessionRequest valid = JsonSerializer.Deserialize<OpenSubscriptionSessionRequest>(
            validDocument.RootElement,
            SerializerOptions())!;

        Assert.True(FilterExpressionJson.TrySerializeValid(valid.FilterExpressions, out string? storedJson));
        AssertJsonEquivalent(ValidFilterExpressionsJson, storedJson);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OpenSubscriptionSessionRequest>(
            """{"filterExpressions":[{"applicableMediaTypes":[{"mediaType":"application/json"}],"expressionString":{"expression":"$.asset","language":"JsonPath"}}]}""",
            SerializerOptions()));
    }

    [Fact]
    public void FilterExpressionJson_DistinguishesNamespaceFaultFromParameterFault()
    {
        OpenSubscriptionSessionRequest namespaceConflict = JsonSerializer.Deserialize<OpenSubscriptionSessionRequest>(
            """{"filterExpressions":[{"expressionString":{"expression":"$.asset","language":"JsonPath"},"namespaces":[{"prefix":"x","name":"urn:one"},{"prefix":"x","name":"urn:two"}]}]}""",
            SerializerOptions())!;
        OpenSubscriptionSessionRequest malformed = JsonSerializer.Deserialize<OpenSubscriptionSessionRequest>(
            """{"filterExpressions":[{"expressionString":{"expression":"$.asset","language":"JsonPath"},"namespaces":[{"prefix":"x","name":" "}]}]}""",
            SerializerOptions())!;

        Assert.False(FilterExpressionJson.TrySerializeValid(
            namespaceConflict.FilterExpressions,
            out _,
            out FilterExpressionValidationFailureKind? namespaceFailureKind));
        Assert.Equal(FilterExpressionValidationFailureKind.NamespaceFault, namespaceFailureKind);

        Assert.False(FilterExpressionJson.TrySerializeValid(
            malformed.FilterExpressions,
            out _,
            out FilterExpressionValidationFailureKind? parameterFailureKind));
        Assert.Equal(FilterExpressionValidationFailureKind.ParameterFault, parameterFailureKind);
    }

    [Fact]
    public void FilterExpressionJson_EmptyExpressionIsAllowAllButWhitespaceOnlyIsMalformed()
    {
        OpenSubscriptionSessionRequest allowAll = JsonSerializer.Deserialize<OpenSubscriptionSessionRequest>(
            """{"filterExpressions":[{"expressionString":{"expression":"","language":"UnsupportedLanguage"}}]}""",
            SerializerOptions())!;
        OpenSubscriptionSessionRequest whitespaceOnly = JsonSerializer.Deserialize<OpenSubscriptionSessionRequest>(
            """{"filterExpressions":[{"expressionString":{"expression":" ","language":"UnsupportedLanguage"}}]}""",
            SerializerOptions())!;

        Assert.True(FilterExpressionJson.TrySerializeValid(allowAll.FilterExpressions, out string? storedJson));
        AssertJsonEquivalent(
            """[{"expressionString":{"expression":"","language":"UnsupportedLanguage"}}]""",
            storedJson);

        Assert.False(FilterExpressionJson.TrySerializeValid(
            whitespaceOnly.FilterExpressions,
            out _,
            out FilterExpressionValidationFailureKind? failureKind));
        Assert.Equal(FilterExpressionValidationFailureKind.ParameterFault, failureKind);
    }

    [Fact]
    public void ConfigurationDiscovery_DeclaresContentFilteringUnsupportedWithEmptyLanguageList()
    {
        ConfigurationDiscoveryController controller = new(
            Microsoft.Extensions.Options.Options.Create(new ConfigurationDiscoveryOptions()),
            new NoOpUsernameTokenParser());

        OkObjectResult result = Assert.IsType<OkObjectResult>(controller.GetSupportedOperations());
        SupportedOperationsResponse response = Assert.IsType<SupportedOperationsResponse>(result.Value);

        Assert.False(response.IsJsonFilteringEnabled);
        Assert.False(response.IsXmlFilteringEnabled);
        Assert.Empty(response.SupportedContentFilteringLanguages.ContentFilteringLanguages);
    }

    [Fact]
    public async Task SessionRepositories_PersistAndReadNullableFilterExpressionsJson()
    {
        string subscriptionSource = await ReadHostFileAsync("Data", "SqlSubscriptionSessionRepository.cs");
        string providerSource = await ReadHostFileAsync("Data", "SqlProviderRequestSessionRepository.cs");

        Assert.Contains("FilterExpressionsJson", subscriptionSource, StringComparison.Ordinal);
        Assert.Contains("@FilterExpressionsJson", subscriptionSource, StringComparison.Ordinal);
        Assert.Contains("SqlDbType.NVarChar, -1", subscriptionSource, StringComparison.Ordinal);
        Assert.Contains("(object?)session.FilterExpressionsJson ?? DBNull.Value", subscriptionSource, StringComparison.Ordinal);
        Assert.Contains("reader.IsDBNull(10) ? null : reader.GetString(10)", subscriptionSource, StringComparison.Ordinal);

        Assert.Contains("FilterExpressionsJson", providerSource, StringComparison.Ordinal);
        Assert.Contains("@FilterExpressionsJson", providerSource, StringComparison.Ordinal);
        Assert.Contains("SqlDbType.NVarChar, -1", providerSource, StringComparison.Ordinal);
        Assert.Contains("(object?)session.FilterExpressionsJson ?? DBNull.Value", providerSource, StringComparison.Ordinal);
        Assert.Contains("reader.IsDBNull(9) ? null : reader.GetString(9)", providerSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreshInstall_AddsNullableSessionFilterExpressionJsonColumns()
    {
        string install = await ReadScriptAsync("Install", "001_CreateDatabase.sql");

        Assert.Contains("FilterExpressionsJson nvarchar(max) null", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SessionFilterExpressions-001", install, StringComparison.Ordinal);
        Assert.Contains("CK_ConsumerSubscriptionSessions_FilterExpressionsJson", install, StringComparison.Ordinal);
        Assert.Contains("CK_ProviderRequestSessions_FilterExpressionsJson", install, StringComparison.Ordinal);
        Assert.Contains("isjson(FilterExpressionsJson) = 1", install, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AzureServiceBusSubscriptionRules_RemainTopicOnly()
    {
        string source = await ReadHostFileAsync("Services", "AzureServiceBusSubscriptionTransport.cs");

        Assert.Contains("TopicSelectionRules.BuildAzureSqlFilter(consumerTopics)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FilterExpressionsJson", source, StringComparison.Ordinal);
        Assert.DoesNotContain("filterExpressions", source, StringComparison.OrdinalIgnoreCase);
    }

    private static ConsumerPublicationServiceController CreateConsumerPublicationController(
        CapturingSubscriptionSessionService service)
    {
        ConsumerPublicationServiceController controller = new(
            service,
            new NoOpUsernameTokenParser(),
            NullLogger<ConsumerPublicationServiceController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static ProviderRequestServiceController CreateProviderRequestController(
        CapturingProviderRequestSessionService service)
    {
        ProviderRequestServiceController controller = new(
            service,
            new NoOpUsernameTokenParser(),
            NullLogger<ProviderRequestServiceController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static void SetJsonBody(Controller controller, string json)
    {
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        controller.Request.ContentType = "application/json";
    }

    private static string? ExpectedStoredFilters(string body)
    {
        JsonNode? root = JsonNode.Parse(body);
        JsonNode? filters = root?["filterExpressions"];
        return filters is JsonArray { Count: > 0 } ? filters.ToJsonString() : null;
    }

    private static void AssertJsonEquivalent(string? expectedJson, string? actualJson)
    {
        if (expectedJson is null)
        {
            Assert.Null(actualJson);
            return;
        }

        Assert.NotNull(actualJson);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expectedJson), JsonNode.Parse(actualJson!)));
    }

    private static JsonSerializerOptions SerializerOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
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

    private static async Task<string> ReadScriptAsync(params string[] pathSegments)
    {
        string path = Path.Combine([AppContext.BaseDirectory, "Scripts", .. pathSegments]);
        return await File.ReadAllTextAsync(path);
    }

    private sealed class CapturingSubscriptionSessionService : IConsumerSubscriptionSessionService
    {
        public IReadOnlyList<string>? OpenedTopics { get; private set; }
        public string? OpenedFilterExpressionsJson { get; private set; }

        public Task<OpenSubscriptionSessionResult> OpenSubscriptionSessionAsync(
            string channelId,
            IReadOnlyList<string> topics,
            string? listenerUrl,
            string? filterExpressionsJson,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            OpenedTopics = topics;
            OpenedFilterExpressionsJson = filterExpressionsJson;
            return Task.FromResult(OpenSubscriptionSessionResult.Success(Guid.NewGuid()));
        }

        public Task<CloseConsumerSubscriptionSessionResult> CloseSubscriptionSessionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ReadPublicationResult> ReadPublicationAsync(string sessionId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<RemovePublicationResult> RemovePublicationAsync(string sessionId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CapturingProviderRequestSessionService : IProviderRequestSessionService
    {
        public IReadOnlyList<string>? OpenedTopics { get; private set; }
        public string? OpenedFilterExpressionsJson { get; private set; }

        public Task<OpenProviderRequestSessionResult> OpenProviderRequestSessionAsync(
            string channelId,
            IReadOnlyList<string> topics,
            string? listenerUrl,
            string? filterExpressionsJson,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            OpenedTopics = topics;
            OpenedFilterExpressionsJson = filterExpressionsJson;
            return Task.FromResult(OpenProviderRequestSessionResult.Success(Guid.NewGuid()));
        }

        public Task<CloseProviderRequestSessionResult> CloseProviderRequestSessionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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
            InternalMessageContent messageContent,
            ChannelCredentials? credentials,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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
