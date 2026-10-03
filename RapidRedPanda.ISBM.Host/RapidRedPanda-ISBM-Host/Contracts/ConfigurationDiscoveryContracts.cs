using System.Text.Json.Serialization;

namespace RapidRedPanda.ISBM.Host.Contracts;

public sealed record AuthenticationSchemeResponse(
    [property: JsonPropertyName("schemeName")] string SchemeName,
    [property: JsonPropertyName("schemeInfoUrl")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SchemeInfoUrl = null);

public sealed record SupportedContentFilteringLanguagesResponse(
    [property: JsonPropertyName("contentFilteringLanguages")] IReadOnlyList<object> ContentFilteringLanguages);

public sealed record SupportedAuthenticationsResponse(
    [property: JsonPropertyName("soapSupportedTokenSchemas")] IReadOnlyList<object> SoapSupportedTokenSchemas,
    [property: JsonPropertyName("restSupportedAuthenticationSchemes")] IReadOnlyList<AuthenticationSchemeResponse> RestSupportedAuthenticationSchemes);

public sealed record SupportedOperationsResponse(
    [property: JsonPropertyName("isXMLFilteringEnabled")] bool IsXmlFilteringEnabled,
    [property: JsonPropertyName("isJSONFilteringEnabled")] bool IsJsonFilteringEnabled,
    [property: JsonPropertyName("supportedContentFilteringLanguages")] SupportedContentFilteringLanguagesResponse SupportedContentFilteringLanguages,
    [property: JsonPropertyName("supportedAuthentications")] SupportedAuthenticationsResponse SupportedAuthentications,
    [property: JsonPropertyName("securityLevelConformance")] int SecurityLevelConformance,
    [property: JsonPropertyName("isDeadLetteringEnabled")] bool IsDeadLetteringEnabled,
    [property: JsonPropertyName("isChannelCreationEnabled")] bool IsChannelCreationEnabled,
    [property: JsonPropertyName("isOpenChannelSecuringEnabled")] bool IsOpenChannelSecuringEnabled,
    [property: JsonPropertyName("isWhitelistRequired")] bool IsWhitelistRequired,
    [property: JsonPropertyName("defaultExpiryDuration")] string? DefaultExpiryDuration,
    [property: JsonPropertyName("additionalInformationURL")] string AdditionalInformationUrl);

public sealed record SecurityDetailsResponse(
    [property: JsonPropertyName("isTLSEnabled")] bool IsTlsEnabled,
    [property: JsonPropertyName("isSecurityTokenRequired")] bool IsSecurityTokenRequired,
    [property: JsonPropertyName("isSecurityTokenEncryptionEnabled")] bool IsSecurityTokenEncryptionEnabled,
    [property: JsonPropertyName("isCertificateRequired")] bool IsCertificateRequired,
    [property: JsonPropertyName("isRBACEnabled")] bool IsRbacEnabled,
    [property: JsonPropertyName("isKeyManagementServiceEnabled")] bool IsKeyManagementServiceEnabled,
    [property: JsonPropertyName("isEndToEndMessageEncryptionEnabled")] bool IsEndToEndMessageEncryptionEnabled);
