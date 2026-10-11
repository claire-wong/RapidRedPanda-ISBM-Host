namespace RapidRedPanda.ISBM.Host.Services;

public sealed class ConfigurationDiscoveryOptions
{
    public const string SectionName = "Isbm:ConfigurationDiscovery";

    public int SecurityLevelConformance { get; set; } = 1;

    // Advertises TLS and requires HTTPS for ISBM requests; hosting supplies TLS.
    public bool IsTlsEnabled { get; set; }

    public bool IsSecurityTokenEncryptionEnabled { get; set; }

    public string AdditionalInformationUrl { get; set; } = "https://github.com/claire-wong/RapidRedPanda-ISBM-Host";

    public string? SecurityDetailsUsername { get; set; }

    public string? SecurityDetailsPassword { get; set; }

    public static bool IsValid(ConfigurationDiscoveryOptions options)
    {
        return options.SecurityLevelConformance == 1 &&
            Uri.TryCreate(options.AdditionalInformationUrl, UriKind.Absolute, out Uri? uri) &&
            uri.Scheme is "http" or "https";
    }
}
