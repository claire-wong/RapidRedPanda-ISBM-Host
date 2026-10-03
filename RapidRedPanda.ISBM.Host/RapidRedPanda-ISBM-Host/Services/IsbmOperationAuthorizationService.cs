using RapidRedPanda.ISBM.Host.Data;

namespace RapidRedPanda.ISBM.Host.Services;

public enum IsbmSessionKind
{
    ConsumerPublication,
    ProviderPublication,
    ProviderRequest,
    ConsumerRequest
}

public enum IsbmAuthorizationOutcome
{
    Authorized,
    NotFoundOrUnauthorized,
    WrongSessionKind,
    Failure
}

public interface IIsbmOperationAuthorizationService
{
    Task<IsbmAuthorizationOutcome> AuthorizeChannelAsync(string channelUri, ChannelCredentials? credentials, CancellationToken cancellationToken);
    Task<IsbmAuthorizationOutcome> AuthorizeSessionAsync(string sessionId, IsbmSessionKind kind, ChannelCredentials? credentials, CancellationToken cancellationToken);
    Task<IsbmAuthorizationOutcome> AuthorizeAnySessionAsync(string sessionId, ChannelCredentials? credentials, CancellationToken cancellationToken);
}

public sealed class IsbmOperationAuthorizationService : IIsbmOperationAuthorizationService
{
    private readonly IChannelRepository _channels;
    private readonly ISubscriptionSessionRepository _subscriptions;
    private readonly IProviderPublicationSessionRepository _publications;
    private readonly IProviderRequestSessionRepository _providerRequests;
    private readonly IConsumerRequestSessionRepository _consumerRequests;

    public IsbmOperationAuthorizationService(
        IChannelRepository channels,
        ISubscriptionSessionRepository subscriptions,
        IProviderPublicationSessionRepository publications,
        IProviderRequestSessionRepository providerRequests,
        IConsumerRequestSessionRepository consumerRequests)
    {
        _channels = channels;
        _subscriptions = subscriptions;
        _publications = publications;
        _providerRequests = providerRequests;
        _consumerRequests = consumerRequests;
    }

    public async Task<IsbmAuthorizationOutcome> AuthorizeChannelAsync(
        string channelUri,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        GetChannelTopicBindingResult channel = await _channels.GetChannelTopicBindingAsync(channelUri, cancellationToken);
        if (!channel.Succeeded || channel.Channel is null)
        {
            return channel.FailureKind == GetChannelTopicBindingFailureKind.NotFound
                ? IsbmAuthorizationOutcome.NotFoundOrUnauthorized
                : IsbmAuthorizationOutcome.Failure;
        }

        return await ValidateAsync(channel.Channel.ChannelId, null, credentials, cancellationToken);
    }

    public async Task<IsbmAuthorizationOutcome> AuthorizeSessionAsync(
        string sessionId,
        IsbmSessionKind kind,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sessionId, out Guid id))
        {
            return IsbmAuthorizationOutcome.NotFoundOrUnauthorized;
        }

        IsbmAuthorizationOutcome requestedKindOutcome = kind switch
        {
            IsbmSessionKind.ConsumerPublication => await AuthorizeSubscriptionAsync(id, credentials, cancellationToken),
            IsbmSessionKind.ProviderPublication => await AuthorizePublicationAsync(id, credentials, cancellationToken),
            IsbmSessionKind.ProviderRequest => await AuthorizeProviderRequestAsync(id, credentials, cancellationToken),
            IsbmSessionKind.ConsumerRequest => await AuthorizeConsumerRequestAsync(id, credentials, cancellationToken),
            _ => IsbmAuthorizationOutcome.Failure
        };

        if (requestedKindOutcome != IsbmAuthorizationOutcome.NotFoundOrUnauthorized)
        {
            return requestedKindOutcome;
        }

        SessionExistenceProbe otherSessionProbe = await ProbeAnyOtherSessionAsync(id, exceptKind: kind, cancellationToken);
        return otherSessionProbe switch
        {
            SessionExistenceProbe.Exists => IsbmAuthorizationOutcome.WrongSessionKind,
            SessionExistenceProbe.Missing => IsbmAuthorizationOutcome.NotFoundOrUnauthorized,
            _ => IsbmAuthorizationOutcome.Failure
        };
    }

    public async Task<IsbmAuthorizationOutcome> AuthorizeAnySessionAsync(
        string sessionId,
        ChannelCredentials? credentials,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sessionId, out Guid id))
        {
            return IsbmAuthorizationOutcome.NotFoundOrUnauthorized;
        }

        foreach (IsbmSessionKind kind in Enum.GetValues<IsbmSessionKind>())
        {
            IsbmAuthorizationOutcome result = await AuthorizeSessionAsync(id.ToString("D"), kind, credentials, cancellationToken);
            if (result is IsbmAuthorizationOutcome.Authorized or IsbmAuthorizationOutcome.Failure)
            {
                return result;
            }
        }

        return IsbmAuthorizationOutcome.NotFoundOrUnauthorized;
    }

    private async Task<IsbmAuthorizationOutcome> AuthorizeSubscriptionAsync(Guid id, ChannelCredentials? credentials, CancellationToken cancellationToken)
    {
        GetSubscriptionSessionResult result = await _subscriptions.GetSubscriptionSessionAsync(id, cancellationToken);
        return result.Session is null
            ? MapLookup(result.FailureKind == GetSubscriptionSessionFailureKind.NotFound)
            : await ValidateAsync(result.Session.ChannelId, result.Session.AuthenticatedTokenUsername, credentials, cancellationToken);
    }

    private async Task<IsbmAuthorizationOutcome> AuthorizePublicationAsync(Guid id, ChannelCredentials? credentials, CancellationToken cancellationToken)
    {
        GetProviderPublicationSessionResult result = await _publications.GetProviderPublicationSessionAsync(id, cancellationToken);
        return result.Session is null
            ? MapLookup(result.FailureKind == GetProviderPublicationSessionFailureKind.NotFound)
            : await ValidateAsync(result.Session.ChannelId, result.Session.AuthenticatedTokenUsername, credentials, cancellationToken);
    }

    private async Task<IsbmAuthorizationOutcome> AuthorizeProviderRequestAsync(Guid id, ChannelCredentials? credentials, CancellationToken cancellationToken)
    {
        GetProviderRequestSessionResult result = await _providerRequests.GetProviderRequestSessionAsync(id, cancellationToken);
        return result.Session is null
            ? MapLookup(result.FailureKind == GetProviderRequestSessionFailureKind.NotFound)
            : await ValidateAsync(result.Session.ChannelId, result.Session.AuthenticatedTokenUsername, credentials, cancellationToken);
    }

    private async Task<IsbmAuthorizationOutcome> AuthorizeConsumerRequestAsync(Guid id, ChannelCredentials? credentials, CancellationToken cancellationToken)
    {
        GetConsumerRequestSessionResult result = await _consumerRequests.GetConsumerRequestSessionAsync(id, cancellationToken);
        return result.Session is null
            ? MapLookup(result.FailureKind == GetConsumerRequestSessionFailureKind.NotFound)
            : await ValidateAsync(result.Session.ChannelId, result.Session.AuthenticatedTokenUsername, credentials, cancellationToken);
    }

    private async Task<SessionExistenceProbe> ProbeAnyOtherSessionAsync(
        Guid id,
        IsbmSessionKind exceptKind,
        CancellationToken cancellationToken)
    {
        if (exceptKind != IsbmSessionKind.ConsumerPublication)
        {
            GetSubscriptionSessionResult subscription = await _subscriptions.GetSubscriptionSessionAsync(id, cancellationToken);
            if (subscription.Session is not null)
            {
                return SessionExistenceProbe.Exists;
            }

            if (subscription.FailureKind != GetSubscriptionSessionFailureKind.NotFound)
            {
                return SessionExistenceProbe.Failure;
            }
        }

        if (exceptKind != IsbmSessionKind.ProviderPublication)
        {
            GetProviderPublicationSessionResult publication = await _publications.GetProviderPublicationSessionAsync(id, cancellationToken);
            if (publication.Session is not null)
            {
                return SessionExistenceProbe.Exists;
            }

            if (publication.FailureKind != GetProviderPublicationSessionFailureKind.NotFound)
            {
                return SessionExistenceProbe.Failure;
            }
        }

        if (exceptKind != IsbmSessionKind.ProviderRequest)
        {
            GetProviderRequestSessionResult providerRequest = await _providerRequests.GetProviderRequestSessionAsync(id, cancellationToken);
            if (providerRequest.Session is not null)
            {
                return SessionExistenceProbe.Exists;
            }

            if (providerRequest.FailureKind != GetProviderRequestSessionFailureKind.NotFound)
            {
                return SessionExistenceProbe.Failure;
            }
        }

        if (exceptKind != IsbmSessionKind.ConsumerRequest)
        {
            GetConsumerRequestSessionResult consumerRequest = await _consumerRequests.GetConsumerRequestSessionAsync(id, cancellationToken);
            if (consumerRequest.Session is not null)
            {
                return SessionExistenceProbe.Exists;
            }

            if (consumerRequest.FailureKind != GetConsumerRequestSessionFailureKind.NotFound)
            {
                return SessionExistenceProbe.Failure;
            }
        }

        return SessionExistenceProbe.Missing;
    }

    private async Task<IsbmAuthorizationOutcome> ValidateAsync(Guid channelId, string? expectedUsername, ChannelCredentials? credentials, CancellationToken cancellationToken)
    {
        ValidateChannelCredentialsResult result = await _channels.ValidateChannelCredentialsAsync(channelId, expectedUsername, credentials, cancellationToken);
        return result.Succeeded
            ? IsbmAuthorizationOutcome.Authorized
            : result.FailureKind == ValidateChannelCredentialsFailureKind.DatabaseFailure
                ? IsbmAuthorizationOutcome.Failure
                : IsbmAuthorizationOutcome.NotFoundOrUnauthorized;
    }

    private static IsbmAuthorizationOutcome MapLookup(bool notFound) =>
        notFound ? IsbmAuthorizationOutcome.NotFoundOrUnauthorized : IsbmAuthorizationOutcome.Failure;

    private enum SessionExistenceProbe
    {
        Missing,
        Exists,
        Failure
    }
}
