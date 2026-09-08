using System.Text.Json;
using RapidRedPanda.ISBM.Host.Contracts;
using RapidRedPanda.ISBM.Host.Services;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class UsernameTokenParserTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly UsernameTokenParser _parser = new();

    [Fact]
    public void Parse_NullSecurityTokens_ReturnsEmptyList()
    {
        IReadOnlyList<UsernameTokenInput> tokens = _parser.Parse(null);

        Assert.Empty(tokens);
    }

    [Fact]
    public void Parse_EmptySecurityTokens_ReturnsEmptyList()
    {
        IReadOnlyList<UsernameTokenInput> tokens = _parser.Parse([]);

        Assert.Empty(tokens);
    }

    [Fact]
    public void Parse_FlatUsernameToken_NormalizesUsernameAndPreservesPassword()
    {
        IReadOnlyList<SecurityTokenRequest> request = DeserializeTokens("""
            [
              {
                "type": "UsernameToken",
                "username": "  Claire  ",
                "password": " PassWord "
              }
            ]
            """);

        UsernameTokenInput token = Assert.Single(_parser.Parse(request));

        Assert.Equal("Claire", token.Username);
        Assert.Equal("CLAIRE", token.NormalizedUsername);
        Assert.Equal(" PassWord ", token.Password);
    }

    [Fact]
    public void Parse_FlatMultipleDifferentUsernameTokens_ReturnsAllTokens()
    {
        IReadOnlyList<SecurityTokenRequest> request = DeserializeTokens("""
            [
              {
                "type": "UsernameToken",
                "username": "Tester21",
                "password": "123"
              },
              {
                "type": "UsernameToken",
                "username": "Tester22",
                "password": "123"
              }
            ]
            """);

        IReadOnlyList<UsernameTokenInput> tokens = _parser.Parse(request);

        Assert.Equal(2, tokens.Count);
        Assert.Contains(tokens, token => token.Username == "Tester21" && token.NormalizedUsername == "TESTER21");
        Assert.Contains(tokens, token => token.Username == "Tester22" && token.NormalizedUsername == "TESTER22");
    }

    [Fact]
    public void Parse_NestedUsernameToken_StillAcceptedForCompatibility()
    {
        IReadOnlyList<SecurityTokenRequest> request = DeserializeTokens("""
            [
              {
                "UsernameToken": {
                  "username": "Tester21",
                  "password": "123"
                }
              }
            ]
            """);

        UsernameTokenInput token = Assert.Single(_parser.Parse(request));

        Assert.Equal("Tester21", token.Username);
        Assert.Equal("TESTER21", token.NormalizedUsername);
        Assert.Equal("123", token.Password);
    }

    [Fact]
    public void Parse_MixedFlatAndNestedUsernameToken_RejectsRequest()
    {
        IReadOnlyList<SecurityTokenRequest> request = DeserializeTokens("""
            [
              {
                "type": "UsernameToken",
                "username": "Tester21",
                "password": "123",
                "UsernameToken": {
                  "username": "Tester22",
                  "password": "123"
                }
              }
            ]
            """);

        Assert.Throws<SecurityTokenValidationException>(() => _parser.Parse(request));
    }

    [Fact]
    public void Parse_UnsupportedFlatTokenType_RejectsRequest()
    {
        IReadOnlyList<SecurityTokenRequest> request = DeserializeTokens("""
            [
              {
                "type": "OtherToken",
                "username": "claire",
                "password": "secret"
              }
            ]
            """);

        Assert.Throws<SecurityTokenValidationException>(() => _parser.Parse(request));
    }

    [Fact]
    public void Parse_TokenTypeUsesOrdinalComparison()
    {
        IReadOnlyList<SecurityTokenRequest> request = DeserializeTokens("""
            [
              {
                "type": "usernametoken",
                "username": "claire",
                "password": "secret"
              }
            ]
            """);

        Assert.Throws<SecurityTokenValidationException>(() => _parser.Parse(request));
    }

    [Fact]
    public void Parse_MissingType_DefaultsToUsernameToken()
    {
        IReadOnlyList<SecurityTokenRequest> request = DeserializeTokens("""
            [
              {
                "username": "claire",
                "password": "secret"
              }
            ]
            """);

        UsernameTokenInput token = Assert.Single(_parser.Parse(request));

        Assert.Equal("claire", token.Username);
        Assert.Equal("CLAIRE", token.NormalizedUsername);
        Assert.Equal("secret", token.Password);
    }

    [Fact]
    public void Parse_NullType_DefaultsToUsernameToken()
    {
        IReadOnlyList<SecurityTokenRequest> request = DeserializeTokens("""
            [
              {
                "type": null,
                "username": "claire",
                "password": "secret"
              }
            ]
            """);

        UsernameTokenInput token = Assert.Single(_parser.Parse(request));

        Assert.Equal("claire", token.Username);
        Assert.Equal("CLAIRE", token.NormalizedUsername);
        Assert.Equal("secret", token.Password);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_BlankType_RejectsRequest(string type)
    {
        IReadOnlyList<SecurityTokenRequest> request =
        [
            new SecurityTokenRequest
            {
                Type = type,
                Username = "claire",
                Password = "secret"
            }
        ];

        Assert.Throws<SecurityTokenValidationException>(() => _parser.Parse(request));
    }

    [Fact]
    public void Parse_MissingUsername_RejectsRequest()
    {
        IReadOnlyList<SecurityTokenRequest> request = DeserializeTokens("""
            [
              {
                "type": "UsernameToken",
                "password": "secret"
              }
            ]
            """);

        Assert.Throws<SecurityTokenValidationException>(() => _parser.Parse(request));
    }

    [Fact]
    public void Parse_WhitespaceOnlyUsername_RejectsRequest()
    {
        IReadOnlyList<SecurityTokenRequest> request = DeserializeTokens("""
            [
              {
                "type": "UsernameToken",
                "username": "   ",
                "password": "secret"
              }
            ]
            """);

        Assert.Throws<SecurityTokenValidationException>(() => _parser.Parse(request));
    }

    [Fact]
    public void Parse_MissingPassword_RejectsRequest()
    {
        IReadOnlyList<SecurityTokenRequest> request = DeserializeTokens("""
            [
              {
                "type": "UsernameToken",
                "username": "claire"
              }
            ]
            """);

        Assert.Throws<SecurityTokenValidationException>(() => _parser.Parse(request));
    }

    [Fact]
    public void Parse_EmptyPassword_RejectsRequest()
    {
        IReadOnlyList<SecurityTokenRequest> request = DeserializeTokens("""
            [
              {
                "type": "UsernameToken",
                "username": "claire",
                "password": ""
              }
            ]
            """);

        Assert.Throws<SecurityTokenValidationException>(() => _parser.Parse(request));
    }

    [Fact]
    public void Parse_DuplicateSameUsernameDifferentCasingAndSamePassword_CollapsesDuplicate()
    {
        IReadOnlyList<SecurityTokenRequest> request = DeserializeTokens("""
            [
              {
                "type": "UsernameToken",
                "username": "claire",
                "password": "secret"
              },
              {
                "type": "UsernameToken",
                "username": "CLAIRE",
                "password": "secret"
              }
            ]
            """);

        Assert.Single(_parser.Parse(request));
    }

    [Fact]
    public void Parse_DuplicateSameUsernameWithSurroundingWhitespaceAndSamePassword_CollapsesDuplicate()
    {
        IReadOnlyList<SecurityTokenRequest> request = DeserializeTokens("""
            [
              {
                "type": "UsernameToken",
                "username": "claire",
                "password": "secret"
              },
              {
                "type": "UsernameToken",
                "username": " CLAIRE ",
                "password": "secret"
              }
            ]
            """);

        Assert.Single(_parser.Parse(request));
    }

    [Fact]
    public void Parse_DuplicateSameNormalizedUsernameDifferentPassword_RejectsRequestWithoutLeakingPassword()
    {
        IReadOnlyList<SecurityTokenRequest> request = DeserializeTokens("""
            [
              {
                "type": "UsernameToken",
                "username": "claire",
                "password": "secret-1"
              },
              {
                "type": "UsernameToken",
                "username": "CLAIRE",
                "password": "secret-2"
              }
            ]
            """);

        SecurityTokenValidationException exception = Assert.Throws<SecurityTokenValidationException>(() => _parser.Parse(request));

        Assert.DoesNotContain("secret-1", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-2", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(" claire ", "CLAIRE")]
    [InlineData("PlantUser", "PLANTUSER")]
    public void NormalizeUsername_TrimsAndUppercasesInvariant(string input, string expected)
    {
        Assert.Equal(expected, _parser.NormalizeUsername(input));
    }

    [Fact]
    public void Parse_UsernameLongerThanApprovedLength_RejectsRequest()
    {
        string longUsername = new('a', 257);
        IReadOnlyList<SecurityTokenRequest> request = DeserializeTokens($$"""
            [
              {
                "type": "UsernameToken",
                "username": "{{longUsername}}",
                "password": "secret"
              }
            ]
            """);

        Assert.Throws<SecurityTokenValidationException>(() => _parser.Parse(request));
    }

    [Fact]
    public void CreateChannelRequest_DeserializesFlatCanonicalSecurityTokenShape()
    {
        CreateChannelRequest request = DeserializeRequest("""
            {
              "uri": "/test3",
              "channelType": "Publication",
              "description": "",
              "securityTokens": [
                {
                  "type": "UsernameToken",
                  "username": "Tester21",
                  "password": "123"
                }
              ]
            }
            """);

        Assert.NotNull(request.SecurityTokens);
        SecurityTokenRequest token = Assert.Single(request.SecurityTokens);

        Assert.Equal("UsernameToken", token.Type);
        Assert.Equal("Tester21", token.Username);
        Assert.Equal("123", token.Password);
    }

    [Fact]
    public void ChannelResponse_DoesNotExposeSecurityTokens()
    {
        string[] propertyNames = typeof(ChannelResponse)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("SecurityTokens", propertyNames);
        Assert.DoesNotContain("Password", propertyNames);
    }

    private static IReadOnlyList<SecurityTokenRequest> DeserializeTokens(string json)
    {
        return JsonSerializer.Deserialize<IReadOnlyList<SecurityTokenRequest>>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Test token JSON did not deserialize.");
    }

    private static CreateChannelRequest DeserializeRequest(string json)
    {
        return JsonSerializer.Deserialize<CreateChannelRequest>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Test request JSON did not deserialize.");
    }
}
