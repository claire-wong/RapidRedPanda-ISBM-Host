using Microsoft.AspNetCore.Mvc;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Data;
using RapidRedPanda.ISBM.Host.Services;
using RapidRedPanda.ISBM.Host.Validation;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RapidRedPanda.ISBM.Host.Controllers
{
    public class ChannelManagementServicelController : Controller
    {
        private static readonly JsonSerializerOptions JsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IChannelRepository _channelRepository;
        private readonly IChannelLifecycleService _channelLifecycleService;
        private readonly IUsernameTokenParser _usernameTokenParser;
        private readonly ILogger<ChannelManagementServicelController> _logger;

        public ChannelManagementServicelController(
            IChannelRepository channelRepository,
            IChannelLifecycleService channelLifecycleService,
            IUsernameTokenParser usernameTokenParser,
            ILogger<ChannelManagementServicelController> logger)
        {
            _channelRepository = channelRepository;
            _channelLifecycleService = channelLifecycleService;
            _usernameTokenParser = usernameTokenParser;
            _logger = logger;
        }

        [HttpGet("isbm/2.0/channels")]
        public async Task<IActionResult> GetChannels(CancellationToken cancellationToken)
        {
            try
            {
                ChannelCredentials? credentials = TryReadBasicCredentials();
                IReadOnlyList<ChannelData> channels = await _channelRepository.GetChannelsAsync(credentials, cancellationToken);

                IEnumerable<ChannelResponse> response = channels.Select(channel =>
                    new ChannelResponse(channel.ChannelUri, channel.ChannelType, channel.Description));

                return Ok(response);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to retrieve channels.");
                return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
            }
        }

        [HttpGet("isbm/2.0/channels/{channelId}")]
        public async Task<IActionResult> GetChannel(string channelId, CancellationToken cancellationToken)
        {
            if (!TryDecodeAndValidateChannelUri(channelId, out string decodedChannelUri))
            {
                return BadRequest(new FaultResponse("ParameterFault"));
            }

            GetChannelResult result;
            try
            {
                result = await _channelRepository.GetChannelAsync(decodedChannelUri, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to retrieve channel.");
                return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
            }

            if (result.Succeeded && result.Channel is not null)
            {
                return Ok(ToChannelDetailResponse(result.Channel));
            }

            return result.FailureKind switch
            {
                GetChannelFailureKind.NotFound => NotFound(new FaultResponse("ChannelFault")),
                _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
            };
        }

        [HttpPost("isbm/2.0/channels")]
        public async Task<IActionResult> CreateChannel(CancellationToken cancellationToken)
        {
            CreateChannelRequest? request;
            try
            {
                request = await JsonSerializer.DeserializeAsync<CreateChannelRequest>(
                    Request.Body,
                    JsonSerializerOptions,
                    cancellationToken);
            }
            catch (JsonException)
            {
                return BadRequest(new FaultResponse("ParameterFault"));
            }

            if (request is null)
            {
                return BadRequest(new FaultResponse("ParameterFault"));
            }

            if (!ChannelValidationRules.IsValidChannelUri(request.Uri))
            {
                return BadRequest(new FaultResponse("ParameterFault"));
            }

            string channelUri = request.Uri!;

            if (request.Description?.Length > ChannelValidationRules.DescriptionMaxLength)
            {
                return BadRequest(new FaultResponse("ParameterFault"));
            }

            if (request.ChannelType is not ("Publication" or "Request"))
            {
                return BadRequest(new FaultResponse("ParameterFault"));
            }

            IReadOnlyList<UsernameTokenInput> parsedTokens;
            try
            {
                parsedTokens = _usernameTokenParser.Parse(request.SecurityTokens);
            }
            catch (SecurityTokenValidationException exception)
            {
                _logger.LogWarning(exception, "Invalid security token input while creating channel.");
                return Conflict(new FaultResponse("SecurityTokenFault"));
            }

            CreateChannelResult result;
            try
            {
                result = await _channelLifecycleService.CreateChannelAsync(
                    channelUri,
                    request.ChannelType,
                    request.Description,
                    parsedTokens.Select(token => new ChannelCreateToken(token.Username, token.NormalizedUsername, token.Password)).ToArray(),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to create channel.");
                return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
            }

            if (result.Succeeded && result.Channel is not null)
            {
                ChannelResponse response = new(result.Channel.ChannelUri, result.Channel.ChannelType, result.Channel.Description);
                return StatusCode(StatusCodes.Status201Created, response);
            }

            return result.FailureKind switch
            {
                CreateChannelFailureKind.DuplicateChannelUri => Conflict(new FaultResponse("ChannelFault")),
                CreateChannelFailureKind.DuplicateTokenUsername => Conflict(new FaultResponse("SecurityTokenFault")),
                CreateChannelFailureKind.InvalidChannelType => BadRequest(new FaultResponse("ParameterFault")),
                _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
            };
        }

        [HttpDelete("isbm/2.0/channels/{channelId}")]
        public async Task<IActionResult> DeleteChannel(string channelId, CancellationToken cancellationToken)
        {
            if (!TryDecodeAndValidateChannelUri(channelId, out string decodedChannelUri))
            {
                return BadRequest(new FaultResponse("ParameterFault"));
            }

            DeleteChannelResult result;
            try
            {
                result = await _channelLifecycleService.DeleteChannelAsync(decodedChannelUri, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to delete channel.");
                return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
            }

            if (result.Succeeded)
            {
                return NoContent();
            }

            return result.FailureKind switch
            {
                DeleteChannelFailureKind.NotFound => NotFound(new FaultResponse("ChannelFault")),
                _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
            };
        }

        [HttpPost("isbm/2.0/channels/{channelId}/security-tokens")]
        public async Task<IActionResult> AddSecurityTokens(string channelId, CancellationToken cancellationToken)
        {
            if (!TryDecodeAndValidateChannelUri(channelId, out string decodedChannelUri))
            {
                return BadRequest(new FaultResponse("ParameterFault"));
            }

            SecurityTokensRequest? tokenRequest = await TryReadSecurityTokensRequestAsync(cancellationToken);
            if (tokenRequest is null)
            {
                return BadRequest(new FaultResponse("ParameterFault"));
            }

            IReadOnlyList<UsernameTokenInput> parsedTokens;
            try
            {
                parsedTokens = _usernameTokenParser.Parse(tokenRequest.SecurityTokens);
                if (parsedTokens.Count == 0)
                {
                    return Conflict(new FaultResponse("SecurityTokenFault"));
                }
            }
            catch (SecurityTokenValidationException exception)
            {
                _logger.LogWarning(exception, "Invalid security token input while adding security tokens.");
                return Conflict(new FaultResponse("SecurityTokenFault"));
            }

            SecurityTokenMutationResult result;
            try
            {
                result = await _channelRepository.AddSecurityTokensAsync(
                    decodedChannelUri,
                    ToChannelCreateTokens(parsedTokens),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to add security tokens.");
                return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
            }

            if (result.Succeeded)
            {
                return StatusCode(StatusCodes.Status201Created);
            }

            return result.FailureKind switch
            {
                SecurityTokenMutationFailureKind.NotFound => NotFound(new FaultResponse("ChannelFault")),
                _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
            };
        }

        [HttpDelete("isbm/2.0/channels/{channelId}/security-tokens")]
        public async Task<IActionResult> RemoveSecurityTokens(string channelId, CancellationToken cancellationToken)
        {
            if (!TryDecodeAndValidateChannelUri(channelId, out string decodedChannelUri))
            {
                return BadRequest(new FaultResponse("ParameterFault"));
            }

            SecurityTokensRequest? tokenRequest = await TryReadSecurityTokensRequestAsync(cancellationToken);
            if (tokenRequest is null)
            {
                return BadRequest(new FaultResponse("ParameterFault"));
            }

            IReadOnlyList<UsernameTokenInput> parsedTokens;
            try
            {
                parsedTokens = _usernameTokenParser.Parse(tokenRequest.SecurityTokens);
                if (parsedTokens.Count == 0)
                {
                    return Conflict(new FaultResponse("SecurityTokenFault"));
                }
            }
            catch (SecurityTokenValidationException exception)
            {
                _logger.LogWarning(exception, "Invalid security token input while removing security tokens.");
                return Conflict(new FaultResponse("SecurityTokenFault"));
            }

            SecurityTokenMutationResult result;
            try
            {
                result = await _channelRepository.RemoveSecurityTokensAsync(
                    decodedChannelUri,
                    ToChannelCreateTokens(parsedTokens),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to remove security tokens.");
                return StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"));
            }

            if (result.Succeeded)
            {
                return NoContent();
            }

            return result.FailureKind switch
            {
                SecurityTokenMutationFailureKind.NotFound => NotFound(new FaultResponse("ChannelFault")),
                _ => StatusCode(StatusCodes.Status500InternalServerError, new FaultResponse("OperationFault"))
            };
        }

        private async Task<SecurityTokensRequest?> TryReadSecurityTokensRequestAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await JsonSerializer.DeserializeAsync<SecurityTokensRequest>(
                    Request.Body,
                    JsonSerializerOptions,
                    cancellationToken);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool TryDecodeAndValidateChannelUri(string channelId, out string decodedChannelUri)
        {
            try
            {
                decodedChannelUri = Uri.UnescapeDataString(channelId);
            }
            catch (UriFormatException)
            {
                decodedChannelUri = string.Empty;
                return false;
            }

            return ChannelValidationRules.IsValidChannelUri(decodedChannelUri);
        }

        private static ChannelDetailResponse ToChannelDetailResponse(ChannelDetailData channel)
        {
            return new ChannelDetailResponse(
                channel.ChannelUri,
                channel.ChannelType,
                channel.Description,
                channel.SecurityTokens
                    .Select(token => new SecurityTokenResponse(token.TokenType, token.Username))
                    .ToArray());
        }

        private static IReadOnlyList<ChannelCreateToken> ToChannelCreateTokens(IReadOnlyList<UsernameTokenInput> parsedTokens)
        {
            return parsedTokens
                .Select(token => new ChannelCreateToken(token.Username, token.NormalizedUsername, token.Password))
                .ToArray();
        }

        private ChannelCredentials? TryReadBasicCredentials()
        {
            string? authorizationHeader = Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authorizationHeader))
            {
                return null;
            }

            if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out AuthenticationHeaderValue? authenticationHeader) ||
                !string.Equals(authenticationHeader.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(authenticationHeader.Parameter))
            {
                return null;
            }

            try
            {
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authenticationHeader.Parameter));
                int separatorIndex = decoded.IndexOf(':', StringComparison.Ordinal);
                if (separatorIndex < 0)
                {
                    return null;
                }

                string username = decoded[..separatorIndex];
                string password = decoded[(separatorIndex + 1)..];

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                {
                    return null;
                }

                return new ChannelCredentials(_usernameTokenParser.NormalizeUsername(username), password);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
