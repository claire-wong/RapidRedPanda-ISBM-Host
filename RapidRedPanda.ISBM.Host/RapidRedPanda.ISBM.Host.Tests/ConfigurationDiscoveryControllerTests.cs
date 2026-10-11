using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Options;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Controllers;
using RapidRedPanda.ISBM.Host.Services;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class ConfigurationDiscoveryControllerTests
{
    [Fact]
    public void Routes_UseSpecificationGetEndpoints()
    {
        RouteAttribute route = Assert.Single(typeof(ConfigurationDiscoveryController).GetCustomAttributes<RouteAttribute>());
        Assert.Equal("isbm/2.0/configuration", route.Template);
        Assert.Equal("supported-operations", GetTemplate(nameof(ConfigurationDiscoveryController.GetSupportedOperations)));
        Assert.Equal("security-details", GetTemplate(nameof(ConfigurationDiscoveryController.GetSecurityDetails)));
    }

    [Fact]
    public void GetSupportedOperations_DeclaresCurrentCapabilitiesWithExactSchema()
    {
        ConfigurationDiscoveryController controller = CreateController();

        OkObjectResult result = Assert.IsType<OkObjectResult>(controller.GetSupportedOperations());
        SupportedOperationsResponse response = Assert.IsType<SupportedOperationsResponse>(result.Value);
        Assert.False(response.IsXmlFilteringEnabled);
        Assert.False(response.IsJsonFilteringEnabled);
        Assert.Empty(response.SupportedContentFilteringLanguages.ContentFilteringLanguages);
        Assert.Empty(response.SupportedAuthentications.SoapSupportedTokenSchemas);
        AuthenticationSchemeResponse scheme = Assert.Single(response.SupportedAuthentications.RestSupportedAuthenticationSchemes);
        Assert.Equal("Basic", scheme.SchemeName);
        Assert.Equal(1, response.SecurityLevelConformance);
        Assert.True(response.IsChannelCreationEnabled);
        Assert.False(response.IsOpenChannelSecuringEnabled);
        Assert.True(response.IsDeadLetteringEnabled);
        Assert.False(response.IsWhitelistRequired);
        Assert.Null(response.DefaultExpiryDuration);
        Assert.Equal("https://github.com/claire-wong/RapidRedPanda-ISBM-Host", response.AdditionalInformationUrl);

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(response));
        string[] names = json.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray();
        Assert.Equal(new[]
        {
            "additionalInformationURL", "defaultExpiryDuration", "isChannelCreationEnabled",
            "isDeadLetteringEnabled", "isJSONFilteringEnabled", "isOpenChannelSecuringEnabled",
            "isWhitelistRequired", "isXMLFilteringEnabled", "securityLevelConformance",
            "supportedAuthentications", "supportedContentFilteringLanguages"
        }.Order(), names);

        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("defaultExpiryDuration").ValueKind);
        Assert.Equal("Basic", json.RootElement
            .GetProperty("supportedAuthentications")
            .GetProperty("restSupportedAuthenticationSchemes")[0]
            .GetProperty("schemeName").GetString());
        Assert.Equal(0, json.RootElement
            .GetProperty("supportedAuthentications")
            .GetProperty("soapSupportedTokenSchemas").GetArrayLength());
        Assert.Equal(0, json.RootElement
            .GetProperty("supportedContentFilteringLanguages")
            .GetProperty("contentFilteringLanguages").GetArrayLength());
    }

    [Fact]
    public void GetSecurityDetails_MissingCredentials_ReturnsSecurityTokenFault()
    {
        ConfigurationDiscoveryController controller = CreateController();

        UnauthorizedObjectResult result = Assert.IsType<UnauthorizedObjectResult>(controller.GetSecurityDetails());
        Assert.Equal("SecurityTokenFault", Assert.IsType<FaultResponse>(result.Value).Fault);
    }

    [Fact]
    public void GetSecurityDetails_InvalidCredentials_ReturnsSecurityTokenFault()
    {
        ConfigurationDiscoveryController controller = CreateController();
        controller.Request.Headers.Authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("discovery-user:wrong"));

        UnauthorizedObjectResult result = Assert.IsType<UnauthorizedObjectResult>(controller.GetSecurityDetails());
        Assert.Equal("SecurityTokenFault", Assert.IsType<FaultResponse>(result.Value).Fault);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetSecurityDetails_ValidBasicCredentials_ReturnsConfiguredSecurityFacts(bool isTlsEnabled)
    {
        ConfigurationDiscoveryController controller = CreateController(isTlsEnabled);
        controller.Request.Headers.Authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("discovery-user:discovery-pass"));

        OkObjectResult result = Assert.IsType<OkObjectResult>(controller.GetSecurityDetails());
        SecurityDetailsResponse response = Assert.IsType<SecurityDetailsResponse>(result.Value);
        Assert.Equal(isTlsEnabled, response.IsTlsEnabled);
        Assert.False(response.IsSecurityTokenRequired);
        Assert.False(response.IsSecurityTokenEncryptionEnabled);
        Assert.False(response.IsCertificateRequired);
        Assert.False(response.IsRbacEnabled);
        Assert.False(response.IsKeyManagementServiceEnabled);
        Assert.False(response.IsEndToEndMessageEncryptionEnabled);

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(response));
        Assert.Equal(isTlsEnabled, json.RootElement.GetProperty("isTLSEnabled").GetBoolean());
        Assert.Equal(new[]
        {
            "isCertificateRequired", "isEndToEndMessageEncryptionEnabled", "isKeyManagementServiceEnabled",
            "isRBACEnabled", "isSecurityTokenEncryptionEnabled", "isSecurityTokenRequired", "isTLSEnabled"
        }.Order(), json.RootElement.EnumerateObject().Select(property => property.Name).Order());
    }

    [Fact]
    public void ConfigurationValidation_RejectsInvalidSecurityLevelsAndUrls()
    {
        Assert.True(ConfigurationDiscoveryOptions.IsValid(new ConfigurationDiscoveryOptions()));
        Assert.False(ConfigurationDiscoveryOptions.IsValid(new ConfigurationDiscoveryOptions { SecurityLevelConformance = 0 }));
        Assert.False(ConfigurationDiscoveryOptions.IsValid(new ConfigurationDiscoveryOptions { SecurityLevelConformance = 2 }));
        Assert.True(ConfigurationDiscoveryOptions.IsValid(new ConfigurationDiscoveryOptions { IsTlsEnabled = true }));
        Assert.False(ConfigurationDiscoveryOptions.IsValid(new ConfigurationDiscoveryOptions { AdditionalInformationUrl = "not-a-url" }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer token")]
    [InlineData("Basic not-base64")]
    public void BasicCredentialReader_InvalidHeader_ReturnsNull(string? header)
    {
        DefaultHttpContext context = new();
        if (header is not null) context.Request.Headers.Authorization = header;
        Assert.Null(BasicAuthenticationCredentials.Read(context.Request, new UsernameTokenParser()));
    }

    private static ConfigurationDiscoveryController CreateController(bool isTlsEnabled = false)
    {
        ConfigurationDiscoveryOptions options = new()
        {
            IsTlsEnabled = isTlsEnabled,
            SecurityDetailsUsername = "discovery-user",
            SecurityDetailsPassword = "discovery-pass"
        };
        ConfigurationDiscoveryController controller = new(Options.Create(options), new UsernameTokenParser())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return controller;
    }

    private static string? GetTemplate(string methodName) =>
        typeof(ConfigurationDiscoveryController).GetMethod(methodName)!.GetCustomAttribute<HttpGetAttribute>()!.Template;
}
