using RapidRedPanda.ISBM.Host.Services;

namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class PublicationNotificationUriBuilderTests
{
    [Theory]
    [InlineData("https://client.example.com/isbm")]
    [InlineData("https://client.example.com/isbm/")]
    [InlineData("http://localhost:5005/base/path")]
    public void Build_AppendsNotificationPathSafely(string listenerUrl)
    {
        Guid sessionId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();
        PublicationNotificationUriBuilder builder = new();

        Uri uri = builder.Build(listenerUrl, sessionId, messageId);

        Assert.EndsWith($"/notifications/{sessionId:D}/{messageId:D}", uri.AbsolutePath);
        Assert.DoesNotContain("//notifications", uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(new Uri(listenerUrl).Scheme, uri.Scheme);
        Assert.Equal(new Uri(listenerUrl).Host, uri.Host);
    }

    [Fact]
    public void Build_PreservesBasePathAndQuery()
    {
        Guid sessionId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();
        PublicationNotificationUriBuilder builder = new();

        Uri uri = builder.Build("https://client.example.com/base/path?tenant=a", sessionId, messageId);

        Assert.Equal($"/base/path/notifications/{sessionId:D}/{messageId:D}", uri.AbsolutePath);
        Assert.Equal("?tenant=a", uri.Query);
    }

    [Fact]
    public void Build_RejectsFragment()
    {
        PublicationNotificationUriBuilder builder = new();

        Assert.Throws<InvalidOperationException>(() =>
            builder.Build("https://client.example.com/isbm#fragment", Guid.NewGuid(), Guid.NewGuid()));
    }
}
