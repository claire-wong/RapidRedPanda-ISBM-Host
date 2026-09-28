using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Services;

namespace RapidRedPanda.ISBM.Host.Controllers;

[ApiController]
[Route("isbm/2.0/configuration")]
public sealed class ConfigurationDiscoveryController : ControllerBase
{
    private readonly ConfigurationDiscoveryOptions _options;
    private readonly IUsernameTokenParser _usernameTokenParser;

    public ConfigurationDiscoveryController(
        IOptions<ConfigurationDiscoveryOptions> options,
        IUsernameTokenParser usernameTokenParser)
    {
        _options = options.Value;
        _usernameTokenParser = usernameTokenParser;
    }

    [HttpGet("supported-operations")]
    [ProducesResponseType<SupportedOperationsResponse>(StatusCodes.Status200OK)]
    public IActionResult GetSupportedOperations()
    {
        SupportedOperationsResponse response = new(
            false,
            false,
            new SupportedContentFilteringLanguagesResponse(Array.Empty<object>()),
            new SupportedAuthenticationsResponse(
                Array.Empty<object>(),
                new[] { new AuthenticationSchemeResponse("Basic", "https://www.rfc-editor.org/rfc/rfc7617") }),
            _options.SecurityLevelConformance,
            false,
            true,
            false,
            false,
            null,
            _options.AdditionalInformationUrl);

        return Ok(response);
    }

    [HttpGet("security-details")]
    [ProducesResponseType<SecurityDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<FaultResponse>(StatusCodes.Status401Unauthorized)]
    public IActionResult GetSecurityDetails()
    {
        Data.ChannelCredentials? credentials = BasicAuthenticationCredentials.Read(Request, _usernameTokenParser);
        if (credentials is null ||
            string.IsNullOrWhiteSpace(_options.SecurityDetailsUsername) ||
            string.IsNullOrEmpty(_options.SecurityDetailsPassword) ||
            !string.Equals(credentials.NormalizedUsername, _usernameTokenParser.NormalizeUsername(_options.SecurityDetailsUsername), StringComparison.Ordinal) ||
            !string.Equals(credentials.Password, _options.SecurityDetailsPassword, StringComparison.Ordinal))
        {
            return Unauthorized(new FaultResponse("SecurityTokenFault"));
        }

        SecurityDetailsResponse response = new(
            _options.IsTlsEnabled,
            false,
            _options.IsSecurityTokenEncryptionEnabled,
            false,
            false,
            false,
            false);

        return Ok(response);
    }
}
