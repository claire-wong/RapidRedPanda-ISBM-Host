namespace RapidRedPanda.ISBM.Host.Tests;

public sealed class PhaseSevenComplianceRegressionTests
{
    [Fact]
    public async Task RequiredEndToEndScenarioCoverage_RemainsPresent()
    {
        IReadOnlyDictionary<string, string> sources = await ReadTestSourcesAsync();

        AssertCoverage(
            sources,
            "Publication already-read expiry preservation",
            "ProviderPublicationExpirationTests.cs",
            "ReadPublication_OutstandingPublication_ReturnsWithoutCheckingExpiration");
        AssertCoverage(
            sources,
            "Publication restart-safe outstanding read",
            "ProviderPublicationExpirationTests.cs",
            "ReadPublication_RecreatedServiceReturnsDurableOutstandingWithoutBrokerLock");
        AssertCoverage(
            sources,
            "Publication unread expired suppression",
            "ProviderPublicationExpirationTests.cs",
            "ReadPublication_ExpiredHead_CompletesItAndContinuesSequentially");
        AssertCoverage(
            sources,
            "Publication remove uses durable state",
            "ProviderPublicationExpirationTests.cs",
            "RemovePublication_DurableOutstandingWithoutBrokerLock_MarksRemovedOnly");
        AssertCoverage(
            sources,
            "Publication close expires owned unexpired publications",
            "PhaseTwoLifecycleComplianceTests.cs",
            "PublicationClose_ExpiresOwnedUnexpiredMessagesInSameTransaction");

        AssertCoverage(
            sources,
            "Request restart-safe outstanding read",
            "PhaseThreeDurableRequestLifecycleTests.cs",
            "ReadRequest_DurableOutstandingStateSurvivesServiceAndTransportRecreation");
        AssertCoverage(
            sources,
            "Request remove without active broker lock",
            "PhaseThreeDurableRequestLifecycleTests.cs",
            "RemoveRequest_RemovesDurableOutstandingStateWithoutActiveBrokerLock");
        AssertCoverage(
            sources,
            "Request expiry allows owning provider response",
            "ProviderConsumerResponseRoundTripTests.cs",
            "ProviderPostResponse_CanBeReadByConsumerWithSameBinaryPayload");
        AssertCoverage(
            sources,
            "Consumer request close expires owned unexpired requests",
            "PhaseTwoLifecycleComplianceTests.cs",
            "ConsumerRequestClose_ExpiresOwnedUnexpiredRequestsInSameTransaction");

        AssertCoverage(
            sources,
            "All close variants route through shared close service",
            "SessionCloseServiceTests.cs",
            "CloseSession_OpenProviderSession_ClosesProviderOnly");
        AssertCoverage(
            sources,
            "All close variants include consumer subscription close",
            "SessionCloseServiceTests.cs",
            "CloseSession_OpenConsumerSession_DelegatesToConsumerClosePreservingCleanupPath");
        AssertCoverage(
            sources,
            "Secured operations use shared session authorization",
            "PhaseOneAuthenticationCoverageTests.cs",
            "ProtectedServiceOperations_InvokeSharedSessionAuthorization");
        AssertCoverage(
            sources,
            "Secured channels use shared channel authorization",
            "PhaseOneAuthenticationCoverageTests.cs",
            "ChannelManagementProtectedOperations_InvokeSharedChannelAuthorization");
        AssertCoverage(
            sources,
            "FilterExpression declaration and runtime alignment",
            "FilterExpressionsTests.cs",
            "ConfigurationDiscovery_DeclaresContentFilteringUnsupportedWithEmptyLanguageList");

        AssertCoverage(
            sources,
            "Publication notification flow",
            "PublicationNotificationDispatcherTests.cs",
            "Dispatcher_DeliveredResult_MarksNotificationDelivered");
        AssertCoverage(
            sources,
            "Provider-request notification flow",
            "PublicationNotificationDispatcherTests.cs",
            "Dispatcher_RequestDeliveredResult_MarksNotificationDelivered");
        AssertCoverage(
            sources,
            "Consumer-response notification flow",
            "PublicationNotificationDispatcherTests.cs",
            "Dispatcher_ResponseDeliveredResult_MarksNotificationDelivered");
    }

    [Fact]
    public async Task ComplianceDiscoveryAndNotificationContracts_RemainAlignedWithRuntime()
    {
        IReadOnlyDictionary<string, string> sources = await ReadTestSourcesAsync();

        Assert.Contains("Assert.True(response.IsDeadLetteringEnabled)", sources["ConfigurationDiscoveryControllerTests.cs"], StringComparison.Ordinal);
        Assert.Contains("Assert.False(response.IsXmlFilteringEnabled)", sources["ConfigurationDiscoveryControllerTests.cs"], StringComparison.Ordinal);
        Assert.Contains("Assert.False(response.IsJsonFilteringEnabled)", sources["ConfigurationDiscoveryControllerTests.cs"], StringComparison.Ordinal);
        Assert.Contains("Assert.Empty(response.SupportedContentFilteringLanguages.ContentFilteringLanguages)", sources["ConfigurationDiscoveryControllerTests.cs"], StringComparison.Ordinal);

        Assert.Contains("SendAsync_ResponseNotification_UsesConsumerRequestSessionAndRequestMessageIdOnly", sources["PublicationNotificationClientTests.cs"], StringComparison.Ordinal);
        Assert.Contains("Assert.DoesNotContain(\"topics\"", sources["PublicationNotificationClientTests.cs"], StringComparison.Ordinal);
        Assert.Contains("NotificationServiceTypes.Response => notification.ConsumerRequestSessionId", sources["PublicationNotificationClient.cs"], StringComparison.Ordinal);

        Assert.Contains("NotificationServiceTypes.Response => GetResponseSuppressionReason(row)", sources["SqlPublicationNotificationRepository.cs"], StringComparison.Ordinal);
        Assert.Contains("response.MessageId is null or response.RemovedUtc is not null", sources["SqlPublicationNotificationRepository.cs"], StringComparison.Ordinal);
    }

    private static void AssertCoverage(
        IReadOnlyDictionary<string, string> sources,
        string scenario,
        string fileName,
        string testName)
    {
        Assert.True(sources.TryGetValue(fileName, out string? source), $"Missing test source for scenario '{scenario}': {fileName}");
        Assert.Contains(testName, source, StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadTestSourcesAsync()
    {
        string testRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            ".."));
        string hostRoot = Path.GetFullPath(Path.Combine(
            testRoot,
            "..",
            "RapidRedPanda-ISBM-Host"));

        Dictionary<string, string> sources = [];
        foreach (string path in Directory.EnumerateFiles(testRoot, "*.cs"))
        {
            sources[Path.GetFileName(path)] = await File.ReadAllTextAsync(path);
        }

        sources["PublicationNotificationClient.cs"] = await File.ReadAllTextAsync(Path.Combine(
            hostRoot,
            "Services",
            "PublicationNotificationClient.cs"));
        sources["SqlPublicationNotificationRepository.cs"] = await File.ReadAllTextAsync(Path.Combine(
            hostRoot,
            "Data",
            "SqlPublicationNotificationRepository.cs"));

        return sources;
    }
}
