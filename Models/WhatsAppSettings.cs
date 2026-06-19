namespace EventCampaignSystem.Models;

public class WhatsAppSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Remitente { get; set; } = string.Empty;

    // Demora aleatoria entre envíos consecutivos para que la campaña no se vea como mensajería masiva ante WhatsApp.
    public int DelayMinMs { get; set; } = 30000;
    public int DelayMaxMs { get; set; } = 90000;
}
