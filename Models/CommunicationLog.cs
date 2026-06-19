using System.ComponentModel.DataAnnotations;

namespace EventCampaignSystem.Models;

public class CommunicationLog
{
    [Key]
    public int Id { get; set; }

    public int? CampaignId { get; set; }

    public int? ContactId { get; set; }

    [MaxLength(40)]
    public string Channel { get; set; } = string.Empty;

    [MaxLength(320)]
    public string Recipient { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Status { get; set; } = string.Empty;

    public string? ProviderResponse { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Campaign? Campaign { get; set; }
    public Contact? Contact { get; set; }
}
