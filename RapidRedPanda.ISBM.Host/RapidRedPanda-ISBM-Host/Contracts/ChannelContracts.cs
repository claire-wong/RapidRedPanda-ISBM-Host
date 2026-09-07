using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace RapidRedPanda.ISBM.Host.Contracts;

public sealed record ChannelResponse(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("channelType")] string ChannelType,
    [property: JsonPropertyName("description")] string? Description);

public sealed record SecurityTokenResponse(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("username")] string Username);

public sealed record ChannelDetailResponse(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("channelType")] string ChannelType,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("securityTokens")] IReadOnlyList<SecurityTokenResponse> SecurityTokens);

public sealed record FaultResponse(
    [property: JsonPropertyName("fault")] string Fault);

public sealed record OpenSubscriptionSessionResponse(
    [property: JsonPropertyName("sessionId")] Guid SessionId);

public sealed record OpenPublicationSessionResponse(
    [property: JsonPropertyName("sessionId")] Guid SessionId);

public sealed record OpenProviderRequestSessionResponse(
    [property: JsonPropertyName("sessionId")] Guid SessionId);

public sealed record OpenConsumerRequestSessionResponse(
    [property: JsonPropertyName("sessionId")] Guid SessionId);

public sealed record ReadPublicationResponse(
    [property: JsonPropertyName("messageId")] Guid MessageId,
    [property: JsonPropertyName("messageContent")] JsonElement MessageContent,
    [property: JsonPropertyName("topics")] IReadOnlyList<string> Topics,
    [property: JsonPropertyName("expiry")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Expiry = null);

public sealed record PostPublicationResponse(
    [property: JsonPropertyName("messageId")] Guid MessageId);

public sealed record ReadRequestResponse(
    [property: JsonPropertyName("messageId")] Guid MessageId,
    [property: JsonPropertyName("messageContent")] JsonElement MessageContent,
    [property: JsonPropertyName("topics")] IReadOnlyList<string> Topics,
    [property: JsonPropertyName("expiry")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Expiry = null);

public sealed record PostRequestResponse(
    [property: JsonPropertyName("messageId")] Guid MessageId);

public sealed record PostResponseResponse(
    [property: JsonPropertyName("messageId")] Guid MessageId);

public sealed record ReadResponseResponse(
    [property: JsonPropertyName("requestMessageId")] Guid RequestMessageId,
    [property: JsonPropertyName("messageId")] Guid MessageId,
    [property: JsonPropertyName("messageContent")] JsonElement MessageContent);

public sealed class OpenSubscriptionSessionRequest
{
    [JsonPropertyName("topics")]
    public IReadOnlyList<string>? Topics { get; init; }

    [JsonPropertyName("listenerUrl")]
    public string? ListenerUrl { get; init; }

    [JsonPropertyName("filterExpressions")]
    public IReadOnlyList<JsonElement>? FilterExpressions { get; init; }
}

public sealed class OpenProviderRequestSessionRequest
{
    [JsonPropertyName("topics")]
    public IReadOnlyList<string>? Topics { get; init; }

    [JsonPropertyName("listenerUrl")]
    public string? ListenerUrl { get; init; }

    [JsonPropertyName("filterExpressions")]
    public IReadOnlyList<JsonElement>? FilterExpressions { get; init; }
}

public sealed class OpenConsumerRequestSessionRequest
{
    [JsonPropertyName("listenerUrl")]
    public string? ListenerUrl { get; init; }
}

public sealed class PostPublicationRequest
{
    [JsonPropertyName("topics")]
    public IReadOnlyList<string?>? Topics { get; init; }

    [JsonPropertyName("messageContent")]
    public JsonElement MessageContent { get; init; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; init; }
}

public sealed class PostRequestRequest
{
    [JsonPropertyName("topics")]
    public IReadOnlyList<string?>? Topics { get; init; }

    [JsonPropertyName("messageContent")]
    public JsonElement MessageContent { get; init; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; init; }
}

public sealed class PostResponseRequest
{
    [JsonPropertyName("messageContent")]
    public JsonElement MessageContent { get; init; }
}

public sealed class CreateChannelRequest
{
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    [JsonPropertyName("channelType")]
    public string? ChannelType { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("securityTokens")]
    public IReadOnlyList<SecurityTokenRequest>? SecurityTokens { get; init; }
}

public sealed class SecurityTokenRequest
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [JsonPropertyName("password")]
    [DataType(DataType.Password)]
    public string? Password { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed class SecurityTokensRequest
{
    [JsonPropertyName("securityTokens")]
    public IReadOnlyList<SecurityTokenRequest>? SecurityTokens { get; init; }
}
