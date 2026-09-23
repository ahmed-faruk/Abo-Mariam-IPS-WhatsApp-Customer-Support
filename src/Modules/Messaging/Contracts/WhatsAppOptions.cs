namespace WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

/// <summary>Configuration needed by the Meta WhatsApp boundary.</summary>
public sealed class WhatsAppOptions
{
    public const string ConfigurationSectionName = "WhatsApp";

    public const int MaxTextBodyLength = 4096;

    public const int DefaultMaxWebhookBodyBytes = 3 * 1024 * 1024;

    public string ApiVersion { get; set; } = string.Empty;

    public string PhoneNumberId { get; set; } = string.Empty;

    public string WabaId { get; set; } = string.Empty;

    public string VerifyToken { get; set; } = string.Empty;

    public string AppSecret { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 20;

    public int MaxWebhookBodyBytes { get; set; } = DefaultMaxWebhookBodyBytes;

    public int WebhookPermitLimit { get; set; } = 120;

    public int WebhookWindowSeconds { get; set; } = 60;
}
