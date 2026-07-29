namespace RapidRedPanda.ISBM.Host.Services;

public sealed class PublicationNotificationOptions
{
    public const string SectionName = "PublicationNotifications";

    public bool Enabled { get; init; } = true;
    public int BatchSize { get; init; } = 20;
    public int MaxConcurrency { get; init; } = 4;
    public int IdlePollIntervalSeconds { get; init; } = 5;
    public int ErrorPollIntervalSeconds { get; init; } = 10;
    public int LeaseDurationSeconds { get; init; } = 60;
    public int HttpTimeoutSeconds { get; init; } = 10;
    public int MaximumAttempts { get; init; } = 8;
    public int MaximumDeliveryAgeMinutes { get; init; } = 1440;
    public int InitialRetryDelaySeconds { get; init; } = 10;
    public int MaximumRetryDelaySeconds { get; init; } = 900;
    public double BackoffMultiplier { get; init; } = 2.0;
    public int JitterPercent { get; init; } = 20;

    public static bool IsValid(PublicationNotificationOptions options)
    {
        return options.BatchSize is >= 1 and <= 500 &&
            options.MaxConcurrency is >= 1 and <= 100 &&
            options.IdlePollIntervalSeconds is >= 1 and <= 3600 &&
            options.ErrorPollIntervalSeconds is >= 1 and <= 3600 &&
            options.LeaseDurationSeconds is >= 1 and <= 3600 &&
            options.HttpTimeoutSeconds is >= 1 and <= 300 &&
            options.LeaseDurationSeconds >= options.HttpTimeoutSeconds * 3 &&
            options.MaximumAttempts is >= 1 and <= 100 &&
            options.MaximumDeliveryAgeMinutes >= 1 &&
            options.InitialRetryDelaySeconds is >= 0 and <= 3600 &&
            options.MaximumRetryDelaySeconds >= options.InitialRetryDelaySeconds &&
            options.MaximumRetryDelaySeconds <= 86400 &&
            options.BackoffMultiplier >= 1.0 &&
            options.BackoffMultiplier <= 10.0 &&
            options.JitterPercent is >= 0 and <= 100;
    }
}
