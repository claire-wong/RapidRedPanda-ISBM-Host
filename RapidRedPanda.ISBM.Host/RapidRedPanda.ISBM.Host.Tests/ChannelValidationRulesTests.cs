using RapidRedPanda.ISBM.Host.Validation;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class ChannelValidationRulesTests
{
    [Theory]
    [InlineData("/channel/123/test")]
    [InlineData("/path+with+spaces/Some#Page?query=value+with&special=characters")]
    [InlineData("/Robot+Fleet/Ball+Shooter/Drive+System/ISO18435:D1.2/Publication")]
    [InlineData("/Robot+Fleet/Ball+Shooter/Drive+System/ISO18435:D1.2/Request")]
    [InlineData("/services/general/publication")]
    [InlineData("/test.123.channel")]
    [InlineData("/test.channel.301")]
    [InlineData("/test_channel_302")]
    [InlineData("/test1")]
    [InlineData("/tests")]
    [InlineData("testchannel.223")]
    [InlineData("/Robot Fleet/Ball Shooter/Drive System/ISO18435:D1.2/Request")]
    [InlineData("test123 mychannel")]
    public void IsValidChannelUri_AcceptsApprovedExamples(string channelUri)
    {
        Assert.True(ChannelValidationRules.IsValidChannelUri(channelUri));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("  ")]
    [InlineData(" /test1")]
    [InlineData("/test1 ")]
    [InlineData("\t/test1")]
    [InlineData("/test1\r")]
    [InlineData("/test\u0001channel")]
    public void IsValidChannelUri_RejectsInvalidValues(string channelUri)
    {
        Assert.False(ChannelValidationRules.IsValidChannelUri(channelUri));
    }

    [Fact]
    public void IsValidChannelUri_RejectsNull()
    {
        Assert.False(ChannelValidationRules.IsValidChannelUri(null));
    }

    [Fact]
    public void IsValidChannelUri_RejectsValuesLongerThanSqlColumn()
    {
        string channelUri = new('a', ChannelValidationRules.ChannelUriMaxLength + 1);

        Assert.False(ChannelValidationRules.IsValidChannelUri(channelUri));
    }

    [Fact]
    public void IsValidChannelUri_AcceptsValuesAtSqlColumnLength()
    {
        string channelUri = new('a', ChannelValidationRules.ChannelUriMaxLength);

        Assert.True(ChannelValidationRules.IsValidChannelUri(channelUri));
    }
}
